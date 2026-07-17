using Aiyara.Harness.Models.Config;

using OllamaSharp.Models.Chat;

namespace Aiyara.Harness.Tools;

/// <summary>
/// Lets the model create or update a reusable skill - a named, packaged set of instructions it can
/// load later via <c>use_skill</c>, this harness's equivalent of Claude Code's own Skill system.
/// Stored at <c>.aiyara/skills/&lt;name&gt;/SKILL.md</c> via <see cref="SkillStore"/>, in either the
/// workspace scope (default - specific to this project, meant to be committed alongside it) or the
/// user scope (under <c>%USERPROFILE%\.aiyara\</c> - personal, available from every project).
/// Confirmed via <see cref="ActionConsent"/> under a broad "write_skill" key, same as the file-writing
/// tools, since writing a skill file carries the same risk as writing any other file.
/// </summary>
public class WriteSkillTool : BaseTool
{
    public WriteSkillTool()
    {
        Type = "function";
        Function = new Function
        {
            Name = "write_skill",
            Description = "Creates or updates a reusable skill at .aiyara/skills/<name>/SKILL.md, so it can be " +
                          "loaded later via use_skill. Create one once you've worked out a repeatable approach " +
                          "worth reusing across sessions - not for a one-off task. A 'workspace' skill (default) " +
                          "is specific to this project and shareable via its repo; a 'user' skill is personal and " +
                          "available from every project on this machine. A workspace skill takes priority over a " +
                          "user skill of the same name.",
            Parameters = new Parameters
            {
                Properties = new Dictionary<string, Property>
                {
                    {
                        "name",
                        new Property { Type = "string", Description = "Short kebab-case name, e.g. 'db-migration-check'." }
                    },
                    {
                        "description",
                        new Property { Type = "string", Description = "One-line summary of when to use this skill." }
                    },
                    {
                        "content",
                        new Property { Type = "string", Description = "The skill's full instructions, as Markdown." }
                    },
                    {
                        "scope",
                        new Property
                        {
                            Type = "string",
                            Description = "'workspace' (default, this project only) or 'user' (every project, personal)."
                        }
                    }
                },
                Required = new List<string> { "name", "description", "content" }
            }
        };
    }

    protected override object? Execute(IDictionary<string, object?>? args)
    {
        var name = args?.TryGetValue("name", out var n) == true ? n?.ToString() : null;
        var description = args?.TryGetValue("description", out var d) == true ? d?.ToString() : null;
        var content = args?.TryGetValue("content", out var c) == true ? c?.ToString() : null;
        var scope = ParseScope(args?.TryGetValue("scope", out var sc) == true ? sc?.ToString() : null);

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Skill name cannot be empty.", nameof(name));
        }

        var overwriting = SkillStore.Exists(name, scope);

        var approved = ActionConsent.Confirm("write_skill",
            "skill file",
            $"{(overwriting ? "update" : "create")} {ScopeLabel(scope)} skill '{name}'");

        if (!approved)
        {
            throw new UnauthorizedAccessException($"Writing skill '{name}' was not approved.");
        }

        SkillStore.Save(name, description ?? "", content ?? "", scope);

        return $"{(overwriting ? "Updated" : "Created")} {ScopeLabel(scope)} skill '{name}' at {SkillStore.SkillFilePath(name, scope)}.";
    }

    private static SkillScope ParseScope(string? scope) =>
        scope?.Trim().Equals("user", StringComparison.OrdinalIgnoreCase) == true ? SkillScope.User : SkillScope.Workspace;

    private static string ScopeLabel(SkillScope scope) => scope == SkillScope.User ? "user" : "workspace";
}
