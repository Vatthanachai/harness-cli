If `websearch.json`'s `Enabled` is true, `Program.cs` registers two opt-in tools - `web_search` and
`web_fetch` - the same "just another `BaseTool`" pattern as [[RAG]] and [[MCP]] tools: no always-on
prompt injection, the model calls them on demand.

## `web_search`

`src/Aiyara.Harness.Tools/Web/WebSearchTool.cs` - queries a self-hosted
[SearXNG](https://github.com/searxng/searxng) instance (`GET search?q=...&format=json` against
`websearch.json`'s `BaseUrl`, e.g. `http://localhost:8080`) with `MaxResults` capping how many of
the returned results get formatted. No API key needed - unlike a hosted search API, this only
requires a reachable local (or self-hosted) instance. Returns each result's title, URL and
`content` snippet as plain text. Blocks synchronously on the HTTP call - same sync-over-async point
`SearchDocumentsTool`/`McpTool` already go through, since `IInvokableTool.InvokeMethod` is
synchronous.

**Gotcha:** a fresh SearXNG instance only serves the `html` format until `json` is added to
`search.formats` in its `settings.yml` (container restart required) - every query 403s until then.
`WebSearchTool` surfaces that in its error message rather than leaving it to be diagnosed from the
SearXNG logs. If `json` is already enabled and it still 403s with a generic "you don't have
permission" body (not the `search.formats` wording), check SearXNG's **limiter**/bot-detection
(`server.limiter` in `settings.yml`, or `limiter.toml`) - by default it blocks non-HTML formats as
an anti-scraping measure, which a purely local/private instance usually wants turned off.

## `web_fetch`

`src/Aiyara.Harness.Tools/Web/WebFetchTool.cs` - fetches a given `http(s)` URL on its own
(no-auth) `HttpClient` and, for an HTML response, strips `<script>`/`<style>` blocks and tags with
a couple of regexes (no HTML parser dependency) rather than a full DOM reconstruction, then
`WebUtility.HtmlDecode`s entities and collapses whitespace. Truncated to 8,000 characters so one
fetch can't blow out the context window. Meant to follow up a `web_search` result the model wants
to read in full. Provider-agnostic - unaffected by the Brave -> SearXNG switch below.

## Why no new plumbing

Both tools are ordinary `BaseTool`s added to the same flat `List<object>` in `Program.cs` as
everything else - they show up in [[Tools]] (`/tools`) and work through either `IChatEngine`
implementation with zero changes anywhere else.

## `websearch.json` example

```json
{
  "Enabled": true,
  "BaseUrl": "http://localhost:8080",
  "MaxResults": 5
}
```

If `BaseUrl` isn't a valid URL, `Program.cs` logs a warning and leaves both tools unavailable for
the session rather than failing startup - same resilience policy as a bad RAG config or an
unreachable MCP server. `BaseUrl` (like `ModelsOptions.Provider`) is only read once at startup -
change it, then restart the harness, not just re-run `harness config set`.

## Troubleshooting behind a Caddy/TLS reverse proxy

A common local SearXNG setup fronts it with [Caddy](https://caddyserver.com) for automatic HTTPS
(a `docker-compose` stack exposing `8080` plain HTTP and `8443` HTTPS is typical). Three distinct
failures were hit and diagnosed getting exactly that setup working here, each with a different
symptom:

1. **`403 Forbidden`, body "you don't have permission..."** - see the `web_search` gotcha above:
   either `search.formats` doesn't list `json`, or SearXNG's limiter is blocking non-HTML formats.
2. **"connection actively refused (`localhost:443`)"** - Caddy's `http -> https` redirect on the
   plain port dropped the port entirely (`Location: https://localhost/...`, no `:8443`), and
   `HttpClient`'s default `AllowAutoRedirect` followed it straight into the default HTTPS port,
   where nothing listens. Also reproduced by pointing `BaseUrl` at the *old* default after changing
   it without restarting the harness (config is read once at startup - see above). Fixed on the
   Caddy/SearXNG side (the redirect now correctly includes `:8443`); if it recurs, point `BaseUrl`
   straight at the HTTPS port rather than relying on the HTTP port's redirect.
3. **.NET: "The SSL connection could not be established"** (curl reproduces it too, as
   `SEC_E_UNTRUSTED_ROOT` without `-k`) - Caddy's automatic HTTPS uses its own self-signed local CA
   ("Caddy Local Authority ... Root", regenerated per stack) that isn't in the OS trust store, so
   every HTTPS request fails TLS validation before it ever reaches `WebSearchTool`'s code. Fixed by
   trusting that CA at the OS level, not in the harness:
   ```bash
   # copy the root CA out of the Caddy container (adjust the container name)
   docker cp <caddy-container>:/data/caddy/pki/authorities/local/root.crt root.crt
   # import into the current user's trusted root store - unlike PowerShell's
   # Import-Certificate, this doesn't pop a confirmation dialog that fails in a
   # non-interactive session
   certutil -user -addstore -f "Root" root.crt
   ```
   No harness code change needed or wanted - .NET's default `HttpClientHandler` doesn't check
   certificate revocation (unlike curl's `schannel` backend, which does by default and needs
   `--ssl-revoke-best-effort` to match), so once the root is trusted the chain validates cleanly
   for `HttpClient` even where a strict `curl` still complains about revocation.

## Why SearXNG over a hosted search API

Originally backed by the Brave Search API (needs a subscription token, even on its free tier).
Switched to SearXNG - a self-hosted meta-search engine that aggregates real web results from
Google/Bing/DuckDuckGo/etc - so `web_search` needs no API key or account at all, just a reachable
instance (`docker compose up` locally, e.g. at `http://localhost:8080`). Trade-off: you own running
the instance, versus a hosted API someone else keeps up - the JSON response shape (`results[].title`
/`url`/`content`) is different enough between the two that swapping providers meant rewriting
`WebSearchTool`'s HTTP call and DTOs, not just an endpoint/header change.

## Related

[[Index]] · [[Tools]] · [[RAG]] · [[MCP]] · [[OCR]] · [[Multi-Agent]]
