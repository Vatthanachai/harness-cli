using System.Text;

using Aiyara.Harness.Cli.Commands;
using Aiyara.Harness.Models.Config;

using OllamaSharp;

using Serilog;

namespace Aiyara.Harness.Cli;

/// <summary>
/// Drives the console read-eval-print loop with Claude Code-inspired output formatting.
/// </summary>
public sealed class ChatSession(Chat chat, ToolRegistry toolRegistry, SlashCommandRegistry registry, SlashCommandContext context, TerminalUI ui)
{
    private readonly StringBuilder _thinkBuffer = new();
    private readonly Queue<string> _pendingImageAttachments = new();
    private bool _eventsWired;

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
                // The task list needs to actually be readable, not chopped mid-line at 200 chars.
                var isTaskListUpdate = result.Tool?.Function?.Name is "write_tasks" or "update_task";
                var cap = isTaskListUpdate ? 2000 : 200;
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
    }

    private async Task SendMessageAsync(string message)
    {
        ConsoleTheme.WriteAssistantHeader();
        Console.Write("  ");

        var messageCountBeforeSend = chat.Messages.Count;
        try
        {
            await foreach (var token in chat.SendAsync(message, toolRegistry.Enabled))
            {
                FlushThinking();
                Console.Write(token);
            }
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

        FlushThinking();
        Console.WriteLine();
        Console.WriteLine();
        AttachPendingImages(messageCountBeforeSend);
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
