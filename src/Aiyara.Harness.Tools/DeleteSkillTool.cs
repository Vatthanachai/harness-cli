using Aiyara.Harness.Models.Config;

using OllamaSharp.Models.Chat;

namespace Aiyara.Harness.Tools;

/// <summary>
/// Lets the model permanently delete a skill created via <see cref="WriteSkillTool"/>. Unlike
/// writes, this is confirmed under a key scoped to the exact skill name and scope (not a broad
/// "delete_skill" key shared across all deletions) - deleting is irreversible, so approving one
/// deletion should never silently approve a different, unreviewed one, even within the same
/// session, and never a same-named skill in the *other* scope.
/// </summary>
public class DeleteSkillTool : BaseTool
{
    public DeleteSkillTool()
    {
        Type = "function";
        Function = new Function
        {
            Name = "delete_skill",
            Description = "Permanently deletes a skill created via write_skill, including its whole folder. If " +
                          "scope isn't given and a skill with that name exists in both scopes, the workspace one " +
                          "is deleted (it's the one that was actually taking effect).",
            Parameters = new Parameters
            {
                Properties = new Dictionary<string, Property>
                {
                    { "name", new Property { Type = "string", Description = "Name of the skill to delete." } },
                    {
                        "scope",
                        new Property
                        {
                            Type = "string",
                            Description = "'workspace' or 'user'. Omit to resolve automatically (workspace preferred)."
                        }
                    }
                },
                Required = new List<string> { "name" }
            }
        };
    }

    protected override object? Execute(IDictionary<string, object?>? args)
    {
        var name = args?.TryGetValue("name", out var n) == true ? n?.ToString() : null;
        var rawScope = args?.TryGetValue("scope", out var sc) == true ? sc?.ToString() : null;

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Skill name cannot be empty.", nameof(name));
        }

        var scope = ParseScope(rawScope) ?? SkillStore.ResolveScope(name);
        if (scope is null)
        {
            return $"Error: no skill named '{name}' exists.";
        }

        var approved = ActionConsent.Confirm($"delete_skill:{scope}:{name}",
            "skill deletion",
            $"permanently delete {ScopeLabel(scope.Value)} skill '{name}'");

        if (!approved)
        {
            throw new UnauthorizedAccessException($"Deleting skill '{name}' was not approved.");
        }

        SkillStore.Delete(name, scope);

        return $"Deleted {ScopeLabel(scope.Value)} skill '{name}'.";
    }

    private static SkillScope? ParseScope(string? scope) => scope?.Trim().ToLowerInvariant() switch
    {
        "user" => SkillScope.User,
        "workspace" => SkillScope.Workspace,
        _ => null
    };

    private static string ScopeLabel(SkillScope scope) => scope == SkillScope.User ? "user" : "workspace";
}
