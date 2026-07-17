namespace Aiyara.Harness.Cli.Commands;

/// <summary>
/// An in-chat "/name" command: its description is shown in the live "/" autocomplete list, and its
/// handler runs with the remaining arguments when the user submits the command.
/// </summary>
public sealed record SlashCommand(string Name, string Description, Func<string[], SlashCommandContext, Task> Handler);
