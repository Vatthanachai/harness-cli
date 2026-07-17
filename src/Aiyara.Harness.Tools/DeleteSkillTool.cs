using Aiyara.Harness.Models.Config;

using OllamaSharp.Models.Chat;

namespace Aiyara.Harness.Tools;

/// <summary>
/// Lets the model permanently delete a skill created via <see cref="WriteSkillTool"/>. Unlike
/// writes, this is confirmed under a key scoped to the exact skill name (not a broad "delete_skill"
/// key shared across all deletions) - deleting is irreversible, so approving one deletion should
/// never silently approve a different, unreviewed one, even within the same session.
/// </summary>
public class DeleteSkillTool : BaseTool
{
    public DeleteSkillTool()
    {
        Type = "function";
        Function = new Function
        {
            Name = "delete_skill",
            Description = "Permanently deletes a skill created via write_skill, including its whole folder.",
            Parameters = new Parameters
            {
                Properties = new Dictionary<string, Property>
                {
                    { "name", new Property { Type = "string", Description = "Name of the skill to delete." } }
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

        if (!SkillStore.Exists(name))
        {
            return $"Error: no skill named '{name}' exists.";
        }

        var approved = ActionConsent.Confirm($"delete_skill:{name}",
            "skill deletion",
            $"permanently delete skill '{name}'");

        if (!approved)
        {
            throw new UnauthorizedAccessException($"Deleting skill '{name}' was not approved.");
        }

        SkillStore.Delete(name);

        return $"Deleted skill '{name}'.";
    }
}
