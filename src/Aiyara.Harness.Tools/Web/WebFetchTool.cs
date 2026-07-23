using System.Net;
using System.Text.RegularExpressions;

using OllamaSharp.Models.Chat;

using Serilog;

namespace Aiyara.Harness.Tools.Web;

/// <summary>
/// Fetches a URL and returns its visible text (HTML tags stripped), for reading a specific
/// <see cref="WebSearchTool"/> result in full. Opt-in alongside <c>web_search</c> - see <c>Program.cs</c>.
/// </summary>
public sealed class WebFetchTool : BaseTool
{
    private const int MaxContentLength = 8000;

    private static readonly Regex ScriptOrStyle =
        new(@"<(script|style)[^>]*>.*?</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex Tag = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    private readonly HttpClient _httpClient;

    public WebFetchTool(HttpClient httpClient)
    {
        _httpClient = httpClient;

        Type = "function";
        Function = new Function
        {
            Name = "web_fetch",
            Description = "Fetches a web page and returns its visible text content (HTML stripped), " +
                          $"truncated to {MaxContentLength} characters. Use it to read a URL found via web_search.",
            Parameters = new Parameters
            {
                Properties = new Dictionary<string, Property>
                {
                    { "url", new Property { Type = "string", Description = "The absolute http(s) URL to fetch." } }
                },
                Required = new List<string> { "url" }
            }
        };
    }

    protected override object? Execute(IDictionary<string, object?>? args)
    {
        var url = args?.TryGetValue("url", out var u) == true ? u?.ToString() : null;
        if (string.IsNullOrWhiteSpace(url))
            return "Error: 'url' is required.";

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return "Error: 'url' must be an absolute http:// or https:// URL.";

        return FetchAsync(uri, ToolCancellation.Current).GetAwaiter().GetResult();
    }

    private async Task<string> FetchAsync(Uri uri, CancellationToken ct)
    {
        using var response = await _httpClient.GetAsync(uri, ct);
        if (!response.IsSuccessStatusCode)
        {
            Log.Warning("web_fetch: {Uri} returned {StatusCode} {ReasonPhrase}", uri, (int)response.StatusCode, response.ReasonPhrase);
            return $"Error: request returned {(int)response.StatusCode} {response.ReasonPhrase}.";
        }

        var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
        var body = await response.Content.ReadAsStringAsync();

        var text = contentType.Contains("html", StringComparison.OrdinalIgnoreCase) ? ExtractText(body) : body;

        return text.Length > MaxContentLength ? text[..MaxContentLength] + "\n...[truncated]" : text;
    }

    /// <summary>
    /// Strips script/style blocks and tags with a couple of regexes rather than pulling in an HTML
    /// parser dependency - good enough for turning a page into readable text for the model, not a
    /// faithful DOM reconstruction.
    /// </summary>
    private static string ExtractText(string html)
    {
        var withoutScripts = ScriptOrStyle.Replace(html, "");
        var withoutTags = Tag.Replace(withoutScripts, " ");
        var decoded = WebUtility.HtmlDecode(withoutTags);
        return Whitespace.Replace(decoded, " ").Trim();
    }
}
