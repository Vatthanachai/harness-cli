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
}
