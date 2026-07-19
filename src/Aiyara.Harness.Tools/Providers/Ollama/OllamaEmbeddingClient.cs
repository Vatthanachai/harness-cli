using OllamaSharp;
using OllamaSharp.Models;

namespace Aiyara.Harness.Tools.Providers.Ollama;

/// <summary>
/// <see cref="IEmbeddingClient"/> backed by <c>IOllamaApiClient.EmbedAsync</c>.
/// </summary>
public sealed class OllamaEmbeddingClient(IOllamaApiClient client) : IEmbeddingClient
{
    public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, string model, CancellationToken ct = default)
    {
        var response = await client.EmbedAsync(new EmbedRequest { Model = model, Input = texts.ToList() }, ct);
        return response.Embeddings;
    }
}
