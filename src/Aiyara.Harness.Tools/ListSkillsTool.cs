using Aiyara.Harness.Models.Config;

using OllamaSharp.Models.Chat;

namespace Aiyara.Harness.Tools;

/// <summary>
/// Lets the model check the current on-disk set of skills and their enabled state, without
/// restarting - the system prompt's skill catalog is only a snapshot from startup, so this is how
/// the model notices a skill it (or the user) just created, updated, or deleted this session.
/// Read-only, so no confirmation prompt.
/// </summary>
public class ListSkillsTool : BaseTool
{
    private readonly SkillRegistry _registry;

    public ListSkillsTool(SkillRegistry registry)
    {
        _registry = registry;

        Type = "function";
        Function = new Function
        {
            Name = "list_skills",
            Description = "Lists every skill currently on disk, with its description and whether it's enabled.",
            Parameters = new Parameters { Properties = new Dictionary<string, Property>(), Required = [] }
        };
    }

    protected override object? Execute(IDictionary<string, object?>? args)
    {
        var skills = _registry.Describe().ToList();

        return skills.Count == 0
            ? "(no skills yet - create one with write_skill)"
            : string.Join('\n', skills.Select(s => $"{(s.Enabled ? "[enabled] " : "[disabled]")} {s.Name} - {s.Description}"));
    }
}
