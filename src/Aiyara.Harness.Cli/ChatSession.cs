using System.Text;

using Aiyara.Harness.Cli.Commands;
using Aiyara.Harness.Models.Config;
using Aiyara.Harness.Tools;
using Aiyara.Harness.Tools.Providers;

using Serilog;

namespace Aiyara.Harness.Cli;

/// <summary>
/// Drives the console read-eval-print loop with Claude Code-inspired output formatting.
/// </summary>
public sealed class ChatSession(IChatEngine chat, ToolRegistry toolRegistry, SlashCommandRegistry registry, SlashCommandContext context, TerminalUI ui)
{
    private readonly StringBuilder _thinkBuffer = new();
    private readonly Queue<string> _pendingImageAttachments = new();
    private bool _eventsWired;

    // Set only while a turn's chat.SendAsync is actually in flight - null the rest of the time
    // (between turns, or while the user is typing). Doubles as the signal for the CancelKeyPress
    // handler below: Ctrl+C interrupts the current turn when one is running, and falls through to
    // its normal "terminate the process" behavior otherwise.
    private CancellationTokenSource? _currentTurnCts;

    /// <summary>
    /// Sends a single message and streams/prints the reply, without entering the interactive
    /// read-eval-print loop. Used to run the one-off "generate AIYARA.md" turn at startup, with the
    /// same tool-call/thinking formatting as the normal chat loop.
    /// </summary>
    public async Task RunInitTurnAsync(string message)
    {
        EnsureEventsWired();
        await SendMessageAsync(message);
    }

    public async Task RunAsync()
    {
        EnsureEventsWired();

        while (true)
        {
            var message = SlashInputReader.ReadLine(registry, context.SkillRegistry, ui, await BuildStatusTextAsync());
            if (string.IsNullOrEmpty(message) || message.Equals("exit", StringComparison.OrdinalIgnoreCase)) return;

            if (message[0] == '/')
            {
                await RunSlashCommandAsync(message);
                continue;
            }

            await SendMessageAsync(message);
        }
    }

    private void EnsureEventsWired()
    {
        if (_eventsWired) return;
        _eventsWired = true;

        // Only takes effect mid-turn (_currentTurnCts is non-null): suppresses the default
        // process-kill behavior and cancels the turn instead, same as pressing Esc. Idle between
        // turns, _currentTurnCts is null and Ctrl+C is left to terminate the process as normal.
        Console.CancelKeyPress += (_, e) =>
        {
            var cts = _currentTurnCts;
            if (cts is null) return;

            e.Cancel = true;
            cts.Cancel();
        };

        chat.OnThink += (_, thought) => _thinkBuffer.Append(thought);

        chat.OnToolCall += (_, call) =>
        {
            FlushThinking();
            if (call.Function?.Name is { } name)
                ConsoleTheme.WriteToolCallHeader(name);
        };

        chat.OnToolResult += (_, result) =>
        {
            if (result.Result is string text && !string.IsNullOrWhiteSpace(text))
            {
                // The task list and a sub-agent's final report both need to actually be readable,
                // not chopped mid-line at 200 chars.
                var needsLongerDisplay = result.Tool?.Function?.Name is "write_tasks" or "update_task" or "dispatch_agent";
                var cap = needsLongerDisplay ? 2000 : 200;
                var display = text.Length > cap ? text[..cap] + "..." : text;
                ConsoleTheme.WriteToolResult(display);
            }

            if (result.Tool?.Function?.Name == "open_image" &&
                result.Result is string base64 &&
                !base64.StartsWith("Error:", StringComparison.Ordinal))
            {
                _pendingImageAttachments.Enqueue(base64);
            }
        };

        // A dispatch_agent call otherwise runs as a silent black box until it returns its final
        // report - forward its sub-agent's own thinking/tool-call/tool-result events through the
        // same display path as the primary conversation's, prefixed so it's clear which is which.
        // Safe to reuse _thinkBuffer/FlushThinking as-is: DispatchAgentTool.Execute runs
        // synchronously inside the primary tool-call loop, so there's never a primary stream and a
        // sub-agent stream in flight on the console at the same time.
        foreach (var dispatchTool in toolRegistry.All.OfType<DispatchAgentTool>())
        {
            dispatchTool.OnSubAgentThink += (_, thought) => _thinkBuffer.Append(thought);

            dispatchTool.OnSubAgentToolCall += (_, call) =>
            {
                FlushThinking();
                if (call.Function?.Name is { } name)
                    ConsoleTheme.WriteToolCallHeader($"agent: {name}");
            };

            dispatchTool.OnSubAgentToolResult += (_, result) =>
            {
                if (result.Result is string text && !string.IsNullOrWhiteSpace(text))
                {
                    var display = text.Length > 200 ? text[..200] + "..." : text;
                    ConsoleTheme.WriteToolResult(display);
                }
            };
        }
    }

    private async Task SendMessageAsync(string message)
    {
        ConsoleTheme.WriteAssistantHeader();
        ConsoleTheme.WriteInterruptHint();
        Console.Write("  ");

        var messageCountBeforeSend = chat.Messages.Count;

        // Not disposed: Console.CancelKeyPress can fire on its own thread at any time, including
        // after this method has already moved on to the finally block below - disposing here would
        // risk an ObjectDisposedException race on cts.Cancel() from that handler. One CTS per turn
        // is cheap enough to just let the GC reclaim.
        var cts = new CancellationTokenSource();
        _currentTurnCts = cts;

        // Console.KeyAvailable throws when stdin is redirected (piped input, non-interactive
        // runs) - same condition SlashInputReader/TerminalUI already gate raw key reads on. Ctrl+C
        // still works to interrupt in that case; only the Esc-key poll is skipped.
        var escapeWatcher = ui.IsActive ? WatchForEscapeAsync(cts) : Task.CompletedTask;

        // Puts cts.Token within reach of a running tool call (e.g. run_command's process wait,
        // dispatch_agent's nested sub-agent turn) via ToolCancellation.Current, since
        // IInvokableTool.InvokeMethod itself has no CancellationToken parameter to pass one
        // through directly - see ToolCancellation's own remarks.
        using var toolCancellationScope = ToolCancellation.Scope(cts.Token);

        try
        {
            await foreach (var token in chat.SendAsync(message, toolRegistry.Enabled, cts.Token))
            {
                FlushThinking();
                Console.Write(token);
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            FlushThinking();
            Console.WriteLine();
            ConsoleTheme.WriteInterrupted();
            Log.Information("Chat turn interrupted by user");
        }
        catch (Exception ex)
        {
            // A single failed turn (timeout, dropped connection, Ollama restart) shouldn't take
            // the whole interactive session down with it - report it and let the loop continue.
            FlushThinking();
            Console.WriteLine();
            ConsoleTheme.WriteError($"Request failed: {ex.Message}");
            Log.Error(ex, "Chat turn failed");
        }
        finally
        {
            _currentTurnCts = null;
            cts.Cancel(); // stop the escape-key watcher if the turn ended on its own
            await escapeWatcher;
        }

        FlushThinking();
        Console.WriteLine();
        Console.WriteLine();
        AttachPendingImages(messageCountBeforeSend);
    }

    /// <summary>
    /// Polls for the Esc key while a turn is streaming and cancels <paramref name="cts"/> the
    /// moment it's pressed, so <see cref="SendMessageAsync"/>'s await foreach unwinds mid-response.
    /// Polls rather than blocking on <see cref="Console.ReadKey()"/> because that call has no way
    /// to be cancelled from another thread; Task.Delay's own cancellation doubles as the exit
    /// signal once the turn ends on its own (see the <c>finally</c> block above).
    /// </summary>
    private static async Task WatchForEscapeAsync(CancellationTokenSource cts)
    {
        try
        {
            while (true)
            {
                if (Console.KeyAvailable && Console.ReadKey(intercept: true).Key == ConsoleKey.Escape)
                {
                    cts.Cancel();
                    return;
                }

                await Task.Delay(50, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Turn ended - normally, on error, or via this same Escape key - stop polling.
        }
    }

    private void AttachPendingImages(int messageCountBeforeSend)
    {
        if (_pendingImageAttachments.Count == 0) return;

        foreach (var toolMessage in chat.Messages.Skip(messageCountBeforeSend))
        {
            if (_pendingImageAttachments.Count == 0) break;
            if (toolMessage.ToolName != "open_image") continue;

            toolMessage.Images = [_pendingImageAttachments.Dequeue()];
            toolMessage.Content = "(image attached)";
        }

        _pendingImageAttachments.Clear();
    }

    private async Task<string> BuildStatusTextAsync()
    {
        var enabled = toolRegistry.Enabled.Count;
        var total = toolRegistry.All.Count;
        var defaultText = $"model: {chat.Model} · tools {enabled}/{total}";

        // Reloaded fresh each turn (rather than cached at startup) so a "/config set statusline
        // Command ..." takes effect on the very next prompt without restarting the harness.
        var options = UserConfigStore.Load("statusline.json", new StatuslineOptions());
        if (string.IsNullOrWhiteSpace(options.Command))
            return defaultText;

        var statuslineContext = new StatuslineContext(chat.Model, enabled, total, Workspace.Root);
        var custom = await StatuslineRunner.RunAsync(options.Command, statuslineContext, TimeSpan.FromSeconds(2));
        return custom ?? defaultText;
    }

    private void FlushThinking()
    {
        if (_thinkBuffer.Length == 0) return;

        var thought = _thinkBuffer.ToString();

        // Reloaded fresh each flush (rather than cached once) so "/config set logging ShowThinking
        // true" (or LogThinking) takes effect immediately, same as the statusline command reload
        // in BuildStatusTextAsync - both off by default: ShowThinking because most reasoning
        // traces are verbose and not meant to be read, LogThinking because it can otherwise bloat
        // the log file quickly. The two are independent - showing it live doesn't imply logging it.
        var loggingOptions = UserConfigStore.Load("logging.json", new LoggingOptions());

        if (loggingOptions.ShowThinking)
            ConsoleTheme.WriteThinking(thought);

        if (loggingOptions.LogThinking)
            Log.Information("Model thinking: {Think}", thought);

        _thinkBuffer.Clear();
    }

    private async Task RunSlashCommandAsync(string message)
    {
        var parts = message[1..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            Console.WriteLine();
            ConsoleTheme.WriteLineColored("  Available commands:", ConsoleTheme.BrightCyan);
            foreach (var cmd in registry.All)
            {
                ConsoleTheme.WriteColored($"    /{cmd.Name,-12}", ConsoleTheme.BrightCyan);
                ConsoleTheme.WriteLineColored(cmd.Description, ConsoleTheme.Gray);
            }

            var enabledSkills = context.SkillRegistry.Enabled;
            if (enabledSkills.Count > 0)
            {
                ConsoleTheme.WriteLineColored("  Skills:", ConsoleTheme.BrightCyan);
                foreach (var skillInfo in enabledSkills)
                {
                    ConsoleTheme.WriteColored($"    /{skillInfo.Name,-12}", ConsoleTheme.BrightCyan);
                    ConsoleTheme.WriteLineColored(skillInfo.Description, ConsoleTheme.Gray);
                }
            }

            Console.WriteLine();
            return;
        }

        var command = registry.Find(parts[0]);
        if (command is not null)
        {
            Console.WriteLine();
            await command.Handler(parts[1..], context);
            Console.WriteLine();
            return;
        }

        // Not a built-in command - fall back to treating it as "/<skill-name> [extra instructions]",
        // this harness's equivalent of Claude Code's own slash-invokable skills. A built-in command
        // always wins on a name clash (checked first, above), so a skill can never shadow one.
        var skill = context.SkillRegistry.Find(parts[0]);
        if (skill is not null)
        {
            await RunSkillAsSlashCommandAsync(skill.Name, string.Join(' ', parts[1..]));
            return;
        }

        ConsoleTheme.WriteError($"Unknown command '/{parts[0]}'");
        Console.Write("  ");
        ConsoleTheme.WriteColored("Available: ", ConsoleTheme.Gray);
        ConsoleTheme.WriteLineColored(
            string.Join(", ", registry.All.Select(c => "/" + c.Name)
                .Concat(context.SkillRegistry.All.Select(s => "/" + s.Name))),
            ConsoleTheme.Cyan);
        Console.WriteLine();
    }

    /// <summary>
    /// Loads <paramref name="skillName"/>'s instructions and sends them as the user's turn for this
    /// message - the same content <c>use_skill</c> would return to the model, just triggered
    /// directly by the user typing "/&lt;skill-name&gt;" instead of the model deciding to call it.
    /// <paramref name="extra"/> (everything typed after the skill name) is appended as additional,
    /// specific instructions; empty is fine - the skill's own instructions stand alone.
    /// </summary>
    private async Task RunSkillAsSlashCommandAsync(string skillName, string extra)
    {
        if (!context.SkillRegistry.IsEnabled(skillName))
        {
            Console.WriteLine();
            ConsoleTheme.WriteError($"Skill '{skillName}' is disabled. Enable it with '/skills on {skillName}'.");
            Console.WriteLine();
            return;
        }

        var content = SkillStore.LoadContent(skillName);
        if (content is null)
        {
            Console.WriteLine();
            ConsoleTheme.WriteError($"Skill '{skillName}' has no content.");
            Console.WriteLine();
            return;
        }

        ConsoleTheme.WriteToolCallHeader($"skill: {skillName}");

        var skillMessage = string.IsNullOrWhiteSpace(extra)
            ? $"Follow the \"{skillName}\" skill below for this turn:\n\n{content}"
            : $"Follow the \"{skillName}\" skill below for this turn:\n\n{content}\n\n---\n\nAdditional instructions from the user: {extra}";

        await SendMessageAsync(skillMessage);
    }
}
