using Aiyara.Harness.Models.Config;

namespace Aiyara.Harness.Cli.Commands;

/// <summary>
/// "/config" — views or edits the same JSON files as the <c>harness config</c> CLI command
/// (ollama url, access token, default model, ...) without leaving the chat session.
/// </summary>
public static class ConfigSlashCommand
{
    /// <summary>
    /// Builds the "/config" <see cref="SlashCommand"/>.
    /// </summary>
    public static SlashCommand Create() => new(
        "config",
        "View or change settings (ollama url, access token, ...)",
        (args, _) =>
        {
            ConfigCommand.Run(args.Length == 0 ? ["show"] : args);
            return Task.CompletedTask;
        });
}
