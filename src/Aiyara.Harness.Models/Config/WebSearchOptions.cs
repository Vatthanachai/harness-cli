namespace Aiyara.Harness.Models.Config;

/// <summary>
/// Settings for the <c>web_search</c>/<c>web_fetch</c> tools, stored in <c>websearch.json</c>.
/// Search is backed by a self-hosted <a href="https://github.com/searxng/searxng">SearXNG</a>
/// instance - no API key needed, unlike a hosted search API; fetching a URL needs no key either.
/// </summary>
public sealed class WebSearchOptions
{
    /// <summary>
    /// Whether <c>web_search</c> and <c>web_fetch</c> are registered for the session.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Base URL of the SearXNG instance <c>web_search</c> queries, e.g. "http://localhost:8080".
    /// SearXNG must have "json" listed under search.formats in its settings.yml, or every query
    /// will 403.
    /// </summary>
    public string BaseUrl { get; init; } = "http://localhost:8080";

    /// <summary>
    /// Number of results <c>web_search</c> returns per query.
    /// </summary>
    public int MaxResults { get; init; } = 5;
}
