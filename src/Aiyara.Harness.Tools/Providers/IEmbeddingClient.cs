namespace Aiyara.Harness.Tools.Providers;

/// <summary>
/// Embeds text into vectors for RAG indexing/search, on whichever provider is active - mirrors the
/// <see cref="IChatEngine"/>/<see cref="IModelCatalog"/> split so RAG code doesn't need to know
/// whether it's talking to Ollama or LM Studio either.
/// </summary>
public interface IEmbeddingClient
{
    /// <summary>
    /// Embeds <paramref name="texts"/> using <paramref name="model"/>, one vector per input text,
    /// in the same order.
    /// </summary>
    Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, string model, CancellationToken ct = default);
}
