using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Aiyara.Harness.Tools.Providers.LmStudio;

/// <summary>
/// <see cref="IEmbeddingClient"/> for LM Studio's OpenAI-compatible <c>/v1/embeddings</c> -
/// confirmed live against a running LM Studio server: standard OpenAI shape,
/// <c>{"data":[{"embedding":[...],"index":0}, ...]}</c>.
/// </summary>
public sealed class LmStudioEmbeddingClient(HttpClient http) : IEmbeddingClient
{
    public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, string model, CancellationToken ct = default)
    {
        var inputArray = new JsonArray();
        foreach (var text in texts) inputArray.Add(text);

        var body = new JsonObject { ["model"] = model, ["input"] = inputArray };

        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/embeddings")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };

        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var results = new float[texts.Count][];
        foreach (var entry in doc.RootElement.GetProperty("data").EnumerateArray())
        {
            var index = entry.GetProperty("index").GetInt32();
            results[index] = entry.GetProperty("embedding").EnumerateArray().Select(e => e.GetSingle()).ToArray();
        }

        return results;
    }
}
