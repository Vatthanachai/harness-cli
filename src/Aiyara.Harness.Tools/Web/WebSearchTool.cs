using System.Text.Json;

using OllamaSharp.Models.Chat;

using Serilog;

namespace Aiyara.Harness.Tools.Web;

/// <summary>
/// Lets the model search the public web via a self-hosted SearXNG instance. Opt-in, like
/// <c>search_documents</c> - only registered when <c>websearch.json</c>'s <c>Enabled</c> is true
/// (see <c>Program.cs</c>). Pair with <see cref="WebFetchTool"/> to read a result's full content.
/// </summary>
public sealed class WebSearchTool : BaseTool
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _httpClient;
    private readonly int _maxResults;

    public WebSearchTool(HttpClient httpClient, int maxResults)
    {
        _httpClient = httpClient;
        _maxResults = maxResults;

        Type = "function";
        Function = new Function
        {
            Name = "web_search",
            Description = "Searches the public web (via a local SearXNG instance) and returns the top " +
                          "results' title, URL and snippet. Use web_fetch on a result's URL to read its " +
                          "full content.",
            Parameters = new Parameters
            {
                Properties = new Dictionary<string, Property>
                {
                    { "query", new Property { Type = "string", Description = "What to search for." } }
                },
                Required = new List<string> { "query" }
            }
        };
    }

    protected override object? Execute(IDictionary<string, object?>? args)
    {
        var query = args?.TryGetValue("query", out var q) == true ? q?.ToString() : null;
        if (string.IsNullOrWhiteSpace(query))
            return "Error: 'query' is required.";

        return SearchAsync(query, ToolCancellation.Current).GetAwaiter().GetResult();
    }

    private async Task<string> SearchAsync(string query, CancellationToken ct)
    {
        var requestUri = $"search?q={Uri.EscapeDataString(query)}&format=json";
        using var response = await _httpClient.GetAsync(requestUri, ct);

        if (!response.IsSuccessStatusCode)
        {
            // The most common cause by far: a fresh SearXNG instance only serves the "html" format
            // until "json" is added to search.formats in its settings.yml (then the container
            // restarted) - flagging that here saves a trip to the SearXNG logs to find out why every
            // query 403s.
            Log.Warning("web_search: SearXNG returned {StatusCode} {ReasonPhrase} for query '{Query}'",
                (int)response.StatusCode, response.ReasonPhrase, query);
            return $"Error: SearXNG returned {(int)response.StatusCode} {response.ReasonPhrase}. If this " +
                   "is a fresh instance, make sure 'json' is listed under search.formats in its settings.yml.";
        }

        var json = await response.Content.ReadAsStringAsync();
        var parsed = JsonSerializer.Deserialize<SearxResponse>(json, JsonOptions);
        var results = parsed?.Results;

        if (results is null || results.Count == 0)
            return "No results found.";

        return string.Join("\n\n", results.Take(_maxResults).Select((r, i) =>
            $"{i + 1}. {r.Title}\n{r.Url}\n{r.Content}"));
    }

    private sealed class SearxResponse
    {
        public List<SearxResult>? Results { get; set; }
    }

    private sealed class SearxResult
    {
        public string? Title { get; set; }
        public string? Url { get; set; }
        public string? Content { get; set; }
    }
}
