namespace Aiyara.Harness.Models.Config;

/// <summary>
/// Chat model selection, stored in <c>models.json</c>.
/// </summary>
public sealed class ModelsOptions
{
    /// <summary>
    /// Name of the model to use for chat completions, e.g. "gemma4:e4b".
    /// </summary>
    public string Default { get; init; } = "gemma4:e4b";
}
