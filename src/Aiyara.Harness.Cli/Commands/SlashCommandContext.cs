using Aiyara.Harness.Models.Config;
using Aiyara.Harness.Tools.Providers;

namespace Aiyara.Harness.Cli.Commands;

/// <summary>
/// Shared state slash command handlers act on: the live model catalog and the active chat session.
/// </summary>
public sealed class SlashCommandContext(IModelCatalog modelCatalog, IChatEngine chat, ToolRegistry toolRegistry, SkillRegistry skillRegistry, TerminalUI terminalUI)
{
    /// <summary>
    /// Lists/switches models on the provider (Ollama or LM Studio) the running session talks to.
    /// </summary>
    public IModelCatalog ModelCatalog { get; } = modelCatalog;

    /// <summary>
    /// The active chat conversation.
    /// </summary>
    public IChatEngine Chat { get; } = chat;

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
        var current = UserConfigStore.Load("models.json", new ModelsOptions());
        UserConfigStore.Save("models.json", current with { Default = modelName });
    }
}
