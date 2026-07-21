using Aiyara.Harness.Models.Enums;

namespace Aiyara.Harness.Models.Config;

/// <summary>
/// Chat model selection, stored in <c>models.json</c>.
/// </summary>
public sealed record ModelsOptions
{
    /// <summary>
    /// Name of the model to use for chat completions, e.g. "gemma4:e4b".
    /// </summary>
    public string Default { get; init; } = "gemma4:e4b";

    /// <summary>
    /// Which server to talk to for chat completions. Read once at startup - switching requires
    /// restarting the harness.
    /// </summary>
    public Provider Provider { get; init; } = Provider.Ollama;

    /// <summary>
    /// Whether the model is allowed to stream reasoning/"thinking" content. Only takes effect on a
    /// model that actually advertises thinking support - checked at startup via Ollama's
    /// <c>/api/show</c> capabilities (see <c>OllamaModelCatalog.SupportsThinkingAsync</c>); a model
    /// without it gets thinking force-disabled regardless of this flag, since Ollama rejects a
    /// "think" request from a model that can't honor it. LM Studio has no equivalent capability
    /// signal to check, so this flag is honored as-is there. Read once at startup (or once per
    /// <c>dispatch_agent</c> sub-agent), like <see cref="Provider"/> - switching requires a restart
    /// or a fresh sub-agent to take effect.
    /// </summary>
    public bool EnableThinking { get; init; } = true;
}
