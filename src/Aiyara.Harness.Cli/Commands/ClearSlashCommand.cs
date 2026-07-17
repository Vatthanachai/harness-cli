using Aiyara.Harness.Models.Config;

using OllamaSharp.Models.Chat;

namespace Aiyara.Harness.Cli.Commands;

/// <summary>
/// "/clear" — clears the conversation history (keeping the system prompt intact) and refreshes the
/// welcome screen, mirroring Claude Code's own /clear. Tool/skill enable state and the task list
/// are left untouched - this only resets the conversation, not the session's settings.
/// </summary>
public static class ClearSlashCommand
{
    public static SlashCommand Create() => new(
        "clear",
        "Clear conversation history and start fresh",
        HandleAsync);

    private static Task HandleAsync(string[] args, SlashCommandContext context)
    {
        var chat = context.Chat;
        var systemMessages = chat.Messages.Where(m => m.Role == ChatRole.System).ToList();

        chat.Messages.Clear();
        chat.Messages.AddRange(systemMessages);

        context.TerminalUI.Initialize(chat.Model, Workspace.Root);

        return Task.CompletedTask;
    }
}
