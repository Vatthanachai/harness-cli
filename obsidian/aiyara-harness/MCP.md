Every server listed in `mcp.json` (`Servers`: `Name`, `Command`, `Args`, `Env`) is connected to at
startup, over stdio, via the official Microsoft-maintained `ModelContextProtocol.Core` client -
and each tool it reports gets exposed to the model as an ordinary tool, named `<Name>_<tool>` (a
server named `github` exposing `search_issues` becomes `github_search_issues`). Only **tools** are
wired in, not MCP resources or prompts - tools are the only MCP capability this harness's
tool-calling model has a slot for.

## Why this needed almost no new plumbing

Every tool the model can call is already just a `BaseTool` in one flat `List<object>` that
[[Tools]] (`ToolRegistry`, `/tools`) and [[Providers]] (both `IChatEngine` implementations) treat
opaquely by name/description. So `src/Aiyara.Harness.Tools/Mcp/McpTool.cs` just wraps a single MCP
tool as another `BaseTool`, and it shows up, toggles, and gets called exactly like a built-in tool
with zero changes anywhere else.

- `McpTool` translates the MCP tool's JSON Schema into OllamaSharp's flat
  `Parameters`/`Property` shape (`Type`/`Description`/`Enum` only - the same fidelity every
  hand-written tool in this repo already has; deeply nested schemas lose detail here, which is an
  existing limitation of OllamaSharp's tool model, not something new).
- `Execute` proxies to `McpClient.CallToolAsync`, blocking on it with `GetAwaiter().GetResult()` -
  `IInvokableTool.InvokeMethod` is synchronous, same sync-over-async point every tool call already
  goes through one way or another.
- `McpToolLoader.LoadAsync` connects to every configured server in a loop; a server that fails to
  launch or complete the MCP handshake (bad command, crashed process, timed-out handshake) is
  skipped with a warning logged, instead of taking the whole harness down - the same resilience
  policy already applied to a broken project doc or an invalid JSON config file.
- `Program.cs` disposes every connected `McpClient` (they're live subprocesses) in its shutdown
  `finally` block, alongside `Log.CloseAndFlush()`.

## Verified live

Connected to a real server (`npx -y @modelcontextprotocol/server-everything`), listed all of its
tools correctly prefixed in `/tools`, and had the model successfully call one (`everything_get-sum`)
through both Ollama and LM Studio - confirming MCP tools really are provider-agnostic. Also
confirmed a deliberately broken server command (bad `Command`) logs a warning and the harness
still starts normally.

## `mcp.json` example

The exact config used for the live verification above - one working stdio-launched server, one
deliberately broken to confirm graceful degradation:

```json
{
  "Servers": [
    {
      "Name": "everything",
      "Command": "npx",
      "Args": ["-y", "@modelcontextprotocol/server-everything"],
      "Env": {}
    },
    {
      "Name": "broken",
      "Command": "totally-not-a-binary",
      "Args": [],
      "Env": {}
    }
  ]
}
```

`Env` sets extra environment variables on the launched server process - e.g. an API key a real
MCP server (a GitHub or filesystem server, say) needs to authenticate:

```json
{
  "Name": "github",
  "Command": "npx",
  "Args": ["-y", "@modelcontextprotocol/server-github"],
  "Env": { "GITHUB_PERSONAL_ACCESS_TOKEN": "ghp_..." }
}
```

`everything`'s tools would show up in `/tools` as `everything_echo`, `everything_get-sum`, etc. -
see [[Tools]].

## Related

[[Index]] · [[Architecture]] · [[Providers]] · [[RAG]] · [[Tools]]
