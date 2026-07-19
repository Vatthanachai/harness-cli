using OllamaSharp;
using OllamaSharp.Models;

namespace Aiyara.Harness.Tools.Providers.Ollama;

/// <summary>
/// <see cref="IModelCatalog"/> backed by an <c>IOllamaApiClient</c>.
/// </summary>
public sealed class OllamaModelCatalog(IOllamaApiClient client) : IModelCatalog
{
    public Uri Uri => client.Uri;

    public string ProviderName => "Ollama";

    public Task<bool> IsAvailableAsync(CancellationToken ct = default) => client.IsRunningAsync(ct);

    public async Task<IReadOnlyList<ModelSummary>> ListModelsAsync(CancellationToken ct = default)
    {
        var models = await client.ListLocalModelsAsync(ct);

        // Ollama's list-models response exposes neither tool-calling capability nor loaded state,
        // so both are reported optimistically/unknown rather than guessed at.
        return models.Select(m => new ModelSummary(m.Name, SupportsTools: true, IsLoaded: null)).ToList();
    }

    public async IAsyncEnumerable<ModelPullProgress> PullModelAsync(
        string modelName,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var progress in client.PullModelAsync(new PullModelRequest { Model = modelName }, ct))
        {
            if (progress is null) continue;
            yield return new ModelPullProgress(progress.Status, progress.Percent);
        }
    }
}
