using Aiyara.Harness.Models.Config;

namespace Aiyara.Harness.Cli.Commands;

/// <summary>
/// "/skills" — lists the workspace's skills and whether each is enabled, or turns one on/off so
/// <c>use_skill</c> does or doesn't honor it starting the next call. Mirrors <see cref="ToolsSlashCommand"/>.
/// </summary>
public static class SkillsSlashCommand
{
    public static SlashCommand Create() => new(
        "skills",
        "List skills, or turn one on/off (/skills on|off <name>)",
        HandleAsync);

    private static Task HandleAsync(string[] args, SlashCommandContext context)
    {
        var registry = context.SkillRegistry;

        if (args.Length == 0)
        {
            PrintList(registry);
            return Task.CompletedTask;
        }

        var action = args[0];
        if (args.Length < 2 ||
            (!action.Equals("on", StringComparison.OrdinalIgnoreCase) &&
             !action.Equals("off", StringComparison.OrdinalIgnoreCase)))
        {
            ConsoleTheme.WriteLineColored("  Usage: /skills [on|off <name>]", ConsoleTheme.Yellow);
            return Task.CompletedTask;
        }

        var enable = action.Equals("on", StringComparison.OrdinalIgnoreCase);
        var name = args[1];
        var ok = enable ? registry.Enable(name) : registry.Disable(name);

        if (ok)
        {
            ConsoleTheme.WriteColored("  ✓ ", ConsoleTheme.BrightGreen);
            ConsoleTheme.WriteLineColored($"{name} is now {(enable ? "enabled" : "disabled")}.", ConsoleTheme.White);
        }
        else
        {
            ConsoleTheme.WriteError($"Unknown skill '{name}'. Run /skills to see available names.");
        }

        return Task.CompletedTask;
    }

    private static void PrintList(SkillRegistry registry)
    {
        var skills = registry.Describe().ToList();
        if (skills.Count == 0)
        {
            ConsoleTheme.WriteLineColored("  No skills yet - ask the model to create one with write_skill.", ConsoleTheme.Gray);
            return;
        }

        ConsoleTheme.WriteLineColored("  Skills:", ConsoleTheme.Orange);
        foreach (var (name, description, enabled, scope) in skills)
        {
            if (enabled)
            {
                ConsoleTheme.WriteColored("    ✓ ", ConsoleTheme.BrightGreen);
                ConsoleTheme.WriteColored($"{name,-20}", ConsoleTheme.White);
            }
            else
            {
                ConsoleTheme.WriteColored("    ✗ ", ConsoleTheme.Red);
                ConsoleTheme.WriteColored($"{name,-20}", ConsoleTheme.Gray);
            }
            ConsoleTheme.WriteColored($"{(scope == SkillScope.User ? "[user]    " : "[workspace]")} ", ConsoleTheme.Gray);
            ConsoleTheme.WriteLineColored(description, ConsoleTheme.Gray);
        }
    }
}
