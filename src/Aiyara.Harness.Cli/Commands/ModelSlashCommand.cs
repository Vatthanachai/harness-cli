using OllamaSharp.Models;

namespace Aiyara.Harness.Cli.Commands;

/// <summary>
/// "/model" — lists locally available Ollama models, or switches to the one named in the
/// argument, pulling it first if Ollama doesn't have it yet.
/// </summary>
public static class ModelSlashCommand
{
    public static SlashCommand Create() => new(
        "model",
        "List available models, or switch to one (pulls it if missing)",
        HandleAsync);

    private static async Task HandleAsync(string[] args, SlashCommandContext context)
    {
        if (!await context.Ollama.IsRunningAsync())
        {
            ConsoleTheme.WriteError($"Can't reach Ollama at {context.Ollama.Uri}. Is it running?");
            return;
        }

        var localModels = (await context.Ollama.ListLocalModelsAsync()).ToList();

        if (args.Length == 0)
        {
            ConsoleTheme.WriteLineColored("  Available models:", ConsoleTheme.Orange);
            foreach (var model in localModels)
            {
                var isCurrent = string.Equals(model.Name, context.Chat.Model, StringComparison.OrdinalIgnoreCase);
                if (isCurrent)
                {
                    ConsoleTheme.WriteColored("    ● ", ConsoleTheme.BrightGreen);
                    ConsoleTheme.WriteLineColored(model.Name, ConsoleTheme.Bold + ConsoleTheme.White);
                }
                else
                {
                    ConsoleTheme.WriteColored("    ○ ", ConsoleTheme.Gray);
                    ConsoleTheme.WriteLineColored(model.Name, ConsoleTheme.Gray);
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
            return;
        }

        ConsoleTheme.WriteLineColored($"  Pulling '{requested}' from Ollama...", ConsoleTheme.Yellow);
        try
        {
            await foreach (var progress in context.Ollama.PullModelAsync(new PullModelRequest { Model = requested }))
            {
                if (progress is null) continue;
                Console.Write($"\r  {ConsoleTheme.Yellow}{progress.Status} {progress.Percent:0}%   {ConsoleTheme.Reset}");
            }
            Console.WriteLine();
        }
        catch (Exception ex)
        {
            ConsoleTheme.WriteError($"Ollama couldn't pull '{requested}': {ex.Message}");
            return;
        }

        context.SwitchModel(requested);
        ConsoleTheme.WriteColored("  ✓ ", ConsoleTheme.BrightGreen);
        ConsoleTheme.WriteLineColored($"Switched to {requested}", ConsoleTheme.White);
    }
}
