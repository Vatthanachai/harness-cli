using System.Text;

using Aiyara.Harness.Tools.Providers;

using OllamaSharp.Models.Chat;
using OllamaSharp.Tools;

using Serilog;

namespace Aiyara.Harness.Tools;

/// <summary>
/// Delegates a bounded, self-contained task to an isolated sub-agent - its own
/// <see cref="IChatEngine"/>, own message history, own tool subset - runs it to completion, and
/// returns its final report as a single tool result. Unlike <see cref="HandoffTool"/> (which only
/// swaps the persona block of the *same* running conversation), this spins up a genuinely separate
/// agent via <see cref="IChatEngineFactory"/>.
/// </summary>
public sealed class DispatchAgentTool : BaseTool
{
    /// <summary>
    /// Tool names never handed to a sub-agent, regardless of <see cref="AgentTypes"/> - not because
    /// they're irrelevant to a sub-task, but because the specific tool *instances* in
    /// <c>availableTools</c> are bound to the primary conversation's shared state: <c>handoff</c>
    /// holds a reference to the primary <see cref="IChatEngine"/> and would mutate its system
    /// message; <c>write_tasks</c>/<c>update_task</c> hold the one shared task board. Handing either
    /// to a sub-agent would let it silently corrupt the primary conversation. <c>dispatch_agent</c>
    /// itself is excluded so nesting is structurally capped at depth 1, no counter needed.
    /// </summary>
    private static readonly string[] AlwaysExcluded = ["dispatch_agent", "handoff", "write_tasks", "update_task"];

    /// <summary>
    /// Read-only tools the <c>explore</c> agent type gets, if they happen to be registered this
    /// session - anything not in this list (write tools, <c>run_command</c>, MCP tools) is withheld,
    /// including MCP tools wholesale since there's no generic way to know which of those are safe.
    /// </summary>
    private static readonly string[] ExploreAllowlist =
        ["open_file", "list_files", "open_image", "get_current_datetime", "list_skills",
         "search_documents", "web_search", "web_fetch", "ocr_image"];

    private static readonly (string Name, string Description, string Focus)[] AgentTypes =
    [
        ("explore",
            "Read-only investigation - locate code, answer 'where is X' / 'how does Y work' questions. " +
            "Cannot write files or run commands.",
            "You are a read-only investigative sub-agent. Explore the workspace (open_file, list_files, and " +
            "any search/web tools available) to answer the task below, then report your findings concisely. " +
            "You cannot write files or run shell commands - if the task requires a change, report what should " +
            "change and where, rather than attempting it."),
        ("general",
            "Full tool access for a self-contained task that doesn't need a more specific type - can read, " +
            "write, and run commands.",
            "You are a general-purpose sub-agent handling a delegated, self-contained task. Use whatever " +
            "tools you need to complete it, then report back a concise summary of what you did and the " +
            "result.")
    ];

    private readonly IChatEngineFactory _factory;
    private readonly string _baseSystemPrompt;
    private readonly IChatEngine _primaryEngine;
    private readonly IReadOnlyList<object> _availableTools;

    /// <summary>Fires with each streamed reasoning/thinking token from the sub-agent, if any.</summary>
    public event EventHandler<string>? OnSubAgentThink;

    /// <summary>Fires once per tool call the sub-agent asks for, before it is invoked.</summary>
    public event EventHandler<Message.ToolCall>? OnSubAgentToolCall;

    /// <summary>Fires once per sub-agent tool call after it has been invoked, with its result.</summary>
    public event EventHandler<ToolResult>? OnSubAgentToolResult;

    public DispatchAgentTool(
        IChatEngineFactory factory,
        string baseSystemPrompt,
        IChatEngine primaryEngine,
        IReadOnlyList<object> availableTools)
    {
        _factory = factory;
        _baseSystemPrompt = baseSystemPrompt;
        _primaryEngine = primaryEngine;
        _availableTools = availableTools;

        Type = "function";
        Function = new Function
        {
            Name = "dispatch_agent",
            Description =
                "Delegates a bounded, self-contained task to an isolated sub-agent - its own system prompt, " +
                "message history and tool subset, with no visibility into this conversation beyond the " +
                "'prompt' given to it. Runs to completion and returns its final report as this call's " +
                "result; use it to keep a long investigation or side task out of the main conversation's " +
                "context instead of doing it inline. Available agent_type values: " +
                string.Join("; ", AgentTypes.Select(a => $"'{a.Name}' - {a.Description}")) + ".",
            Parameters = new Parameters
            {
                Properties = new Dictionary<string, Property>
                {
                    {
                        "agent_type",
                        new Property { Type = "string", Description = "One of: " + string.Join(", ", AgentTypes.Select(a => a.Name)) }
                    },
                    {
                        "prompt",
                        new Property
                        {
                            Type = "string",
                            Description = "The task/instructions for the sub-agent - it sees only this, not " +
                                          "the conversation so far, so include everything it needs to know."
                        }
                    },
                    {
                        "description",
                        new Property
                        {
                            Type = "string",
                            Description = "Optional short (3-5 word) label for this dispatch, for display/logging only."
                        }
                    }
                },
                Required = new List<string> { "agent_type", "prompt" }
            }
        };
    }

    protected override object? Execute(IDictionary<string, object?>? args)
    {
        var agentType = args?.TryGetValue("agent_type", out var t) == true ? t?.ToString() : null;
        var prompt = args?.TryGetValue("prompt", out var p) == true ? p?.ToString() : null;

        if (string.IsNullOrWhiteSpace(prompt))
        {
            return "Error: 'prompt' is required.";
        }

        var match = AgentTypes.FirstOrDefault(x => string.Equals(x.Name, agentType, StringComparison.OrdinalIgnoreCase));
        if (match.Name is null)
        {
            return $"Error: unknown agent_type '{agentType}'. Available: {string.Join(", ", AgentTypes.Select(x => x.Name))}.";
        }

        return RunAsync(match, prompt, ToolCancellation.Current).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Builds an isolated sub-agent for <paramref name="type"/>, runs <paramref name="prompt"/> as
    /// its only user turn to completion, and returns the accumulated final text - same sync-over-async
    /// pattern <see cref="Web.WebSearchTool"/>/<c>SearchDocumentsTool</c> already use in their own
    /// <c>Execute</c>. Uses <see cref="_primaryEngine"/>'s *current* model (read live, not captured at
    /// construction) so a <c>/model</c> switch before dispatching applies to sub-agents too.
    /// <paramref name="ct"/> is the primary turn's own cancellation (see <see cref="ToolCancellation"/>)
    /// - cancelling it aborts the sub-agent's turn exactly like it would the primary conversation's.
    /// </summary>
    private async Task<string> RunAsync((string Name, string Description, string Focus) type, string prompt, CancellationToken ct)
    {
        var systemPrompt = $"{_baseSystemPrompt}\n\n### Sub-agent role ###\n{type.Focus}";
        var engine = await _factory.CreateAsync(_primaryEngine.Model, systemPrompt, ct);
        var subAgentTools = ToolsFor(type.Name);

        engine.OnThink += (_, thought) => OnSubAgentThink?.Invoke(this, thought);

        engine.OnToolCall += (_, call) =>
        {
            OnSubAgentToolCall?.Invoke(this, call);
            Log.Information("[sub-agent:{AgentType}] wants to call: {ToolName}", type.Name, call.Function?.Name);
        };

        engine.OnToolResult += (_, result) =>
        {
            OnSubAgentToolResult?.Invoke(this, result);
            Log.Information("[sub-agent:{AgentType}] tool returned: {ToolResult}", type.Name, result.Result);
        };

        Log.Information("Dispatching {AgentType} sub-agent: {Prompt}", type.Name, Truncate(prompt, 200));

        var text = new StringBuilder();
        await foreach (var token in engine.SendAsync(prompt, subAgentTools, ct))
        {
            text.Append(token);
        }

        var final = text.ToString().Trim();
        Log.Information("{AgentType} sub-agent finished: {Length} char(s)", type.Name, final.Length);

        return string.IsNullOrEmpty(final) ? "(sub-agent returned no text)" : final;
    }

    private static string Truncate(string text, int maxLength) =>
        text.Length > maxLength ? text[..maxLength] + "..." : text;

    /// <summary>
    /// Filters <see cref="_availableTools"/> down to what <paramref name="agentTypeName"/> gets:
    /// <see cref="AlwaysExcluded"/> removed for every type, then <c>explore</c> further narrowed to
    /// <see cref="ExploreAllowlist"/>. Public (not just used internally by <see cref="RunAsync"/>)
    /// so it doubles as introspection - what tool subset a given agent type would actually get - and
    /// so it's testable without going through a live model.
    /// </summary>
    /// <param name="agentTypeName"></param>
    /// <returns></returns>
    public List<object> ToolsFor(string agentTypeName)
    {
        var excluded = new HashSet<string>(AlwaysExcluded, StringComparer.OrdinalIgnoreCase);
        var candidates = _availableTools.Where(tool => !excluded.Contains(NameOf(tool)));

        if (!string.Equals(agentTypeName, "explore", StringComparison.OrdinalIgnoreCase))
        {
            return candidates.ToList();
        }

        var allowed = new HashSet<string>(ExploreAllowlist, StringComparer.OrdinalIgnoreCase);
        return candidates.Where(tool => allowed.Contains(NameOf(tool))).ToList();
    }

    private static string NameOf(object tool) => ((Tool)tool).Function?.Name ?? tool.GetType().Name;
}
