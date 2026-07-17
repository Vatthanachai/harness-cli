using Aiyara.Harness.Models.Config;

using OllamaSharp.Models.Chat;

namespace Aiyara.Harness.Tools;

/// <summary>
/// Lets the model load a skill's full instructions by name - this harness's equivalent of Claude
/// Code's own Skill invocation. Read-only, so no confirmation prompt, matching <c>open_file</c>.
/// </summary>
public class UseSkillTool : BaseTool
{
    private readonly SkillRegistry _registry;

    public UseSkillTool(SkillRegistry registry)
    {
        _registry = registry;

        Type = "function";
        Function = new Function
        {
            Name = "use_skill",
            Description = "Loads a skill's full instructions by name so you can follow them for the current " +
                          "task. See the skill catalog in the system prompt, or call list_skills for the " +
                          "current on-disk set (the system prompt catalog is only a startup snapshot).",
            Parameters = new Parameters
            {
                Properties = new Dictionary<string, Property>
                {
                    { "name", new Property { Type = "string", Description = "Name of the skill to load." } }
                },
                Required = new List<string> { "name" }
            }
        };
    }

    protected override object? Execute(IDictionary<string, object?>? args)
    {
        var name = args?.TryGetValue("name", out var n) == true ? n?.ToString() : null;

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Skill name cannot be empty.", nameof(name));
        }

        if (!_registry.IsEnabled(name))
        {
            return SkillStore.Exists(name)
                ? $"Error: skill '{name}' is disabled. The user can re-enable it with '/skills on {name}'."
                : $"Error: no skill named '{name}' exists. Call list_skills to see what's available.";
        }

        var content = SkillStore.LoadContent(name);
        return content ?? $"Error: skill '{name}' has no content.";
    }
}
