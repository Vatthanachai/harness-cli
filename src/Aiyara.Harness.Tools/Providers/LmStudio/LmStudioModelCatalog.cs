using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Aiyara.Harness.Tools.Providers.LmStudio;

/// <summary>
/// <see cref="IModelCatalog"/> backed by LM Studio's own richer <c>/api/v0/models</c> endpoint
/// (confirmed live: unlike the plain OpenAI-compatible <c>/v1/models</c>, it also reports each
/// model's loaded state and whether it advertises <c>tool_use</c> capability - this harness
/// always sends tool definitions, so that capability matters for the <c>/model</c> listing).
/// </summary>
public sealed class LmStudioModelCatalog(HttpClient http) : IModelCatalog
{
    public Uri Uri => http.BaseAddress!;

    public string ProviderName => "LM Studio";

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await http.GetAsync("api/v0/models", ct);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<ModelSummary>> ListModelsAsync(CancellationToken ct = default)
    {
        using var response = await http.GetAsync("api/v0/models", ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var result = new List<ModelSummary>();
        if (!doc.RootElement.TryGetProperty("data", out var data)) return result;

        foreach (var model in data.EnumerateArray())
        {
            var name = model.GetProperty("id").GetString() ?? "";

            var supportsTools = model.TryGetProperty("capabilities", out var capabilities) &&
                                 capabilities.EnumerateArray().Any(c => c.GetString() == "tool_use");

            bool? isLoaded = model.TryGetProperty("state", out var state)
                ? state.GetString() == "loaded"
                : null;

            result.Add(new ModelSummary(name, supportsTools, isLoaded));
        }

        return result;
    }

    /// <summary>
    /// LM Studio exposes no capability signal for reasoning/"thinking" support (see
    /// <see cref="IModelCatalog.SupportsThinkingAsync"/>), and there's nothing to fail here either
    /// way, so this always reports true.
    /// </summary>
    public Task<bool> SupportsThinkingAsync(string model, CancellationToken ct = default) => Task.FromResult(true);

    /// <summary>
    /// LM Studio has no API-driven registry to pull from - models are downloaded out-of-band via
    /// the <c>lms</c> CLI or the LM Studio app.
    /// </summary>
    public async IAsyncEnumerable<ModelPullProgress> PullModelAsync(
        string modelName,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        throw new NotSupportedException(
            $"LM Studio has no remote model registry to pull from. Download '{modelName}' first " +
            $"(e.g. 'lms get {modelName}' or via the LM Studio app), then run /model {modelName} again.");
#pragma warning disable CS0162 // unreachable - required so this satisfies IAsyncEnumerable<T>
        yield break;
#pragma warning restore CS0162
    }
}
