using Aiyara.Harness.Models.Config;

using OllamaSharp;

namespace Aiyara.Harness.Cli.Commands;

/// <summary>
/// Shared state slash command handlers act on: the live Ollama client and the active chat session.
/// </summary>
public sealed class SlashCommandContext(IOllamaApiClient ollama, Chat chat, ToolRegistry toolRegistry, SkillRegistry skillRegistry, TerminalUI terminalUI)
{
    /// <summary>
    /// The Ollama client the running session talks to.
    /// </summary>
    public IOllamaApiClient Ollama { get; } = ollama;

    /// <summary>
    /// The active chat conversation.
    /// </summary>
    public Chat Chat { get; } = chat;

    /// <summary>
    /// The tools available to the session and which of them are currently enabled.
    /// </summary>
    public ToolRegistry ToolRegistry { get; } = toolRegistry;

    /// <summary>
    /// The workspace's skills and which of them are currently enabled.
    /// </summary>
    public SkillRegistry SkillRegistry { get; } = skillRegistry;

    /// <summary>
    /// The console welcome screen - re-shown by "/clear" after resetting the conversation.
    /// </summary>
    public TerminalUI TerminalUI { get; } = terminalUI;

    /// <summary>
    /// Switches the active chat to <paramref name="modelName"/> and persists it as the default in
    /// <c>models.json</c> so future sessions start with it too.
    /// </summary>
    public void SwitchModel(string modelName)
    {
        Chat.Model = modelName;
        UserConfigStore.Save("models.json", new ModelsOptions { Default = modelName });
    }
}
