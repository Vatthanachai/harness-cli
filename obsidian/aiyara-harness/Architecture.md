## Projects

| Project | Purpose |
|---|---|
| `src/Aiyara.Harness.Cli` | Console entry point, chat REPL, slash commands, persona/system prompt, console rendering. |
| `src/Aiyara.Harness.Models` | Config option types, JSON-backed user config store, workspace/trust/consent, skills, tasks. |
| `src/Aiyara.Harness.Tools` | Tools exposed to the model. |

`tests/Aiyara.Harness.Tools.Tests` — the repo's first test project (`dotnet test`), covering the
three `IVectorStore` implementations - see [[RAG]] § Automated tests.

## Two config tiers

This is the key design split in the whole project - everything either belongs to the **user**
(this machine, every project) or the **workspace** (this project only, meant to be committed).

- **User-level**: `%USERPROFILE%\.aiyara\*.json` — `ollama.json` / `lmstudio.json` (server URL,
  access token - only one is used, per `models.json`'s `Provider`), `models.json` (default model
  + `Provider: Ollama|LMStudio`, read once at startup - see [[Providers]]), `statusline.json`
  (custom status line command), `trust.json` (approved workspace folders / external paths),
  `logging.json` (`ShowThinking` / `LogThinking`, both off by default - see [[#Logging]]),
  `mcp.json` (MCP servers to connect to at startup - see [[MCP]]), `rag.json` (document indexing
  settings for the `search_documents` tool - see [[RAG]]), `websearch.json` (SearXNG `BaseUrl` +
  `MaxResults` for the `web_search`/`web_fetch` tools - see [[Web Search]]).
- **Workspace-level**: `AIYARA.md` / `FILES.md` / `TOOLS.md` / `COMMANDS.md` / `MEMORY.md` at the
  project root (auto-loaded into the system prompt at startup — this harness's equivalent of
  Claude Code's `CLAUDE.md`), plus `.aiyara/skills/` (see [[Skills System]]).

The same split shows up again inside skills specifically - see [[Skills System]].

## Logging

The Serilog log file also lives under the user tier - `%USERPROFILE%\.aiyara\logs\harness-<date>.log`,
one shared location across every workspace instead of scattering a `logs\` folder wherever the
harness happens to be launched from. That path can't be set in `appsettings.json` (a relative path
there resolves against the process's working directory, not the user's profile), so it's built in
`Program.cs` from `UserConfigPaths.Directory` and added to the `LoggerConfiguration` in code;
`appsettings.json` still owns the console sink and log levels.

`logging.json` has two independent toggles, both reloaded fresh on every `ChatSession.FlushThinking`
call (so a `/config set` takes effect immediately, no restart) and both **off by default**:

- `ShowThinking` - display the model's reasoning live in the terminal (the "✻ Thinking…" section).
- `LogThinking` - also write it to the log file above.

Turning one on doesn't turn on the other - e.g. `ShowThinking=true, LogThinking=false` (watch it
live, don't keep a permanent record) is a reasonable combination, not just the two defaults.

## Workspace & trust

Every file/shell tool resolves paths through the current workspace root (`Workspace.Root`). A path
that would land outside it triggers an approval prompt, unless it was already approved in a past
run (remembered in `trust.json`). First launch in a new folder also asks a one-time "do you trust
this folder" question before anything else runs.

## Console rendering

`TerminalUI.cs`, `ConsoleTheme.cs`, `NativeConsole.cs` handle output. `SlashInputReader.cs` handles
input - see [[Multi-line Input Box]] for the interesting parts of that one.

## Related

[[Index]] · [[Providers]] · [[MCP]] · [[RAG]] · [[Web Search]] · [[Slash Commands]] · [[Tools]] · [[Skills System]]
