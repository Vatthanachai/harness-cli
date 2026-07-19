using Aiyara.Harness.Tools.Providers;

namespace Aiyara.Harness.Cli.Commands;

/// <summary>
/// "/model" — lists models known to the active provider (Ollama or LM Studio), or switches to the
/// one named in the argument, pulling it first if the provider supports that and doesn't have it
/// yet.
/// </summary>
public static class ModelSlashCommand
{
    public static SlashCommand Create() => new(
        "model",
        "List available models, or switch to one (pulls it if missing and supported)",
        HandleAsync);

    private static async Task HandleAsync(string[] args, SlashCommandContext context)
    {
        if (!await context.ModelCatalog.IsAvailableAsync())
        {
            ConsoleTheme.WriteError($"Can't reach {context.ModelCatalog.ProviderName} at {context.ModelCatalog.Uri}. Is it running?");
            return;
        }

        var localModels = (await context.ModelCatalog.ListModelsAsync()).ToList();

        if (args.Length == 0)
        {
            ConsoleTheme.WriteLineColored("  Available models:", ConsoleTheme.Orange);
            foreach (var model in localModels)
            {
                var suffix = model.SupportsTools ? "" : " (no tool_use)";
                var isCurrent = string.Equals(model.Name, context.Chat.Model, StringComparison.OrdinalIgnoreCase);
                if (isCurrent)
                {
                    ConsoleTheme.WriteColored("    ● ", ConsoleTheme.BrightGreen);
                    ConsoleTheme.WriteLineColored(model.Name + suffix, ConsoleTheme.Bold + ConsoleTheme.White);
                }
                else
                {
                    ConsoleTheme.WriteColored("    ○ ", ConsoleTheme.Gray);
                    ConsoleTheme.WriteLineColored(model.Name + suffix, ConsoleTheme.Gray);
                }
            }
            return;
        }

        var requested = args[0];
        var existing = localModels.FirstOrDefault(m => string.Equals(m.Name, requested, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            context.SwitchModel(existing.Name);
            ConsoleTheme.WriteColored("  ✓ ", ConsoleTheme.BrightGreen);
            ConsoleTheme.WriteLineColored($"Switched to {existing.Name}", ConsoleTheme.White);
            if (!existing.SupportsTools)
                ConsoleTheme.WriteLineColored($"  Warning: '{existing.Name}' doesn't advertise tool_use support - tool calls may not work.", ConsoleTheme.Yellow);
            return;
        }

        ConsoleTheme.WriteLineColored($"  Pulling '{requested}' from {context.ModelCatalog.ProviderName}...", ConsoleTheme.Yellow);
        try
        {
            await foreach (var progress in context.ModelCatalog.PullModelAsync(requested))
            {
                Console.Write($"\r  {ConsoleTheme.Yellow}{progress.Status} {progress.Percent:0}%   {ConsoleTheme.Reset}");
            }
            Console.WriteLine();
        }
        catch (NotSupportedException ex)
        {
            ConsoleTheme.WriteError(ex.Message);
            return;
        }
        catch (Exception ex)
        {
            ConsoleTheme.WriteError($"{context.ModelCatalog.ProviderName} couldn't pull '{requested}': {ex.Message}");
            return;
        }

        context.SwitchModel(requested);
        ConsoleTheme.WriteColored("  ✓ ", ConsoleTheme.BrightGreen);
        ConsoleTheme.WriteLineColored($"Switched to {requested}", ConsoleTheme.White);
    }
}
