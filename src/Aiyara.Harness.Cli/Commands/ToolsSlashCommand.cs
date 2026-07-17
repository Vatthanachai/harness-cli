namespace Aiyara.Harness.Cli.Commands;

/// <summary>
/// "/tools" — lists the harness's tools and whether each is enabled, or turns one on/off so the
/// model does or doesn't see it starting the next turn.
/// </summary>
public static class ToolsSlashCommand
{
    public static SlashCommand Create() => new(
        "tools",
        "List tools, or turn one on/off (/tools on|off <name>)",
        HandleAsync);

    private static Task HandleAsync(string[] args, SlashCommandContext context)
    {
        var registry = context.ToolRegistry;

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
            ConsoleTheme.WriteLineColored("  Usage: /tools [on|off <name>]", ConsoleTheme.Yellow);
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
            ConsoleTheme.WriteError($"Unknown tool '{name}'. Run /tools to see available names.");
        }

        return Task.CompletedTask;
    }

    private static void PrintList(ToolRegistry registry)
    {
        ConsoleTheme.WriteLineColored("  Tools:", ConsoleTheme.Orange);
        foreach (var (name, description, enabled) in registry.Describe())
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
            ConsoleTheme.WriteLineColored(description, ConsoleTheme.Gray);
        }
    }
}
