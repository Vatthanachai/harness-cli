using Aiyara.Harness.Tools.Providers;

using OllamaSharp.Models.Chat;

namespace Aiyara.Harness.Tools;

/// <summary>
/// Lets the model hand itself off to a different specialist persona partway through the
/// conversation - a lighter-weight analogue of switching subagents, but within the same running
/// chat and message history rather than spawning an isolated one. Handoff only changes the system
/// message's specialization block (see <see cref="SpecializationMarker"/>); the shared identity,
/// working principles and project docs loaded at startup stay untouched, and nothing said so far
/// in the conversation is lost.
/// </summary>
public class HandoffTool : BaseTool
{
    /// <summary>
    /// Delimits the persona-specific block appended to the system message, so a later handoff can
    /// find and replace just that block instead of accumulating one block per handoff forever.
    /// </summary>
    private const string SpecializationMarker = "\n\n### Active specialist persona ###\n";

    private static readonly (string Name, string Description, string Focus)[] Personas =
    [
        ("general", "Default all-purpose mode - hand back to this to drop any specialization.", ""),
        ("planner",
            "Plans before implementing: breaks work into steps, surfaces trade-offs and open questions before touching code.",
            "You are acting as a planner. Break the task into concrete steps and identify the files/areas involved " +
            "before writing any code. Call out risky trade-offs or open questions explicitly, and check the plan " +
            "holds together before starting to implement it."),
        ("reviewer",
            "Reviews existing code critically for bugs, security issues, and deviations from this repo's conventions.",
            "You are acting as a code reviewer. Read the relevant files before commenting - don't review from memory. " +
            "Look specifically for correctness bugs, security issues, and inconsistencies with this repo's " +
            "established conventions. Cite file and line for anything you flag. If something's fine, say so briefly " +
            "instead of padding the review with restated code."),
        ("debugger",
            "Diagnoses failures systematically: builds a repro, ranks hypotheses, verifies the real cause before fixing.",
            "You are acting as a debugger. Don't guess at a fix. Build a way to reliably reproduce the problem first, " +
            "list a short ranked set of hypotheses for the root cause before testing any of them, verify which one " +
            "is actually true, then fix it and confirm the original symptom is actually gone.")
    ];

    private readonly IChatEngine _chat;

    public HandoffTool(IChatEngine chat)
    {
        _chat = chat;

        Type = "function";
        Function = new Function
        {
            Name = "handoff",
            Description = "Switches your own working mode to a different specialist persona, without losing " +
                          "anything said so far in this conversation. Available personas: " +
                          string.Join("; ", Personas.Select(p => $"'{p.Name}' - {p.Description}")) +
                          ". Use 'general' to hand back to the default mode.",
            Parameters = new Parameters
            {
                Properties = new Dictionary<string, Property>
                {
                    {
                        "persona",
                        new Property { Type = "string", Description = "One of: " + string.Join(", ", Personas.Select(p => p.Name)) }
                    },
                    {
                        "reason",
                        new Property { Type = "string", Description = "Short note on why you're handing off now and what the new persona should focus on." }
                    }
                },
                Required = new List<string> { "persona", "reason" }
            }
        };
    }

    protected override object? Execute(IDictionary<string, object?>? args)
    {
        var personaName = args?.TryGetValue("persona", out var p) == true ? p?.ToString() : null;
        var reason = args?.TryGetValue("reason", out var r) == true ? r?.ToString() : null;

        var match = Personas.FirstOrDefault(x => string.Equals(x.Name, personaName, StringComparison.OrdinalIgnoreCase));
        if (match.Name is null)
        {
            return $"Error: unknown persona '{personaName}'. Available: {string.Join(", ", Personas.Select(x => x.Name))}.";
        }

        var systemMessage = _chat.Messages.FirstOrDefault(m => m.Role == ChatRole.System);
        if (systemMessage is null)
        {
            return "Error: no system message found to hand off from.";
        }

        var basePrompt = systemMessage.Content ?? "";
        var markerIndex = basePrompt.IndexOf(SpecializationMarker, StringComparison.Ordinal);
        if (markerIndex >= 0) basePrompt = basePrompt[..markerIndex];

        systemMessage.Content = string.IsNullOrEmpty(match.Focus)
            ? basePrompt
            : basePrompt + SpecializationMarker + match.Focus;

        return string.IsNullOrWhiteSpace(reason)
            ? $"Handed off to '{match.Name}' persona."
            : $"Handed off to '{match.Name}' persona. {reason}";
    }
}
