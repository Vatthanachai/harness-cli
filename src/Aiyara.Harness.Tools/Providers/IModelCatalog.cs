namespace Aiyara.Harness.Tools.Providers;

/// <summary>
/// One model known to a provider's server: its name, whether it's known to support tool calling,
/// and whether it's currently loaded (providers that don't expose loaded-state, like Ollama's
/// listing endpoint, report <c>null</c>).
/// </summary>
public sealed record ModelSummary(string Name, bool SupportsTools, bool? IsLoaded);

/// <summary>
/// One progress update while pulling a model from a remote registry.
/// </summary>
public sealed record ModelPullProgress(string Status, double Percent);

/// <summary>
/// Lists and switches models on a provider's server, and (where supported) pulls a missing one.
/// Backs the <c>/model</c> slash command for both Ollama (<see cref="Ollama.OllamaModelCatalog"/>)
/// and LM Studio (<see cref="LmStudio.LmStudioModelCatalog"/>).
/// </summary>
public interface IModelCatalog
{
    /// <summary>
    /// The server's base address, shown in "can't reach" error messages.
    /// </summary>
    Uri Uri { get; }

    /// <summary>
    /// Display name of the provider, e.g. "Ollama" or "LM Studio", for error messages.
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Whether the server is reachable at all.
    /// </summary>
    Task<bool> IsAvailableAsync(CancellationToken ct = default);

    /// <summary>
    /// The models the server currently knows about (downloaded/local, not necessarily loaded).
    /// </summary>
    Task<IReadOnlyList<ModelSummary>> ListModelsAsync(CancellationToken ct = default);

    /// <summary>
    /// Pulls <paramref name="modelName"/> from a remote registry, streaming progress. Throws
    /// <see cref="NotSupportedException"/> for providers (LM Studio) with no such capability.
    /// </summary>
    IAsyncEnumerable<ModelPullProgress> PullModelAsync(string modelName, CancellationToken ct = default);
}
