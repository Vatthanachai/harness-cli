# FILES.md

Auto-loaded into the system prompt at startup (see `Program.cs`). Describes this project's layout
so the model doesn't need to re-explore the tree every session.

## Solution layout

- `aiyara-harness.slnx` — solution file, references all three projects below.
- `global.json` — pins the .NET SDK version.
- `Directory.Build.props` — MSBuild properties shared by every project (`TargetFramework`,
  `ImplicitUsings`, `Nullable`). Auto-imported by every `.csproj` under this directory - add a new
  project-wide setting here, not to each `.csproj` individually. A project-specific override (e.g.
  `GenerateDocumentationFile`, currently only Cli/Tools) still belongs in that project's own file.
- `Directory.Packages.props` — Central Package Management: every NuGet package version used
  anywhere in the solution, in one place (`ManagePackageVersionsCentrally=true`). Individual
  `.csproj` files reference packages by name only, no `Version` attribute - add new packages'
  versions here, then a bare `<PackageReference Include="..." />` in the project that needs it.
- `src/Aiyara.Harness.Cli/` — console entry point and chat loop.
- `src/Aiyara.Harness.Models/` — config option types, JSON-backed user config store, workspace/trust/consent.
- `src/Aiyara.Harness.Tools/` — tools exposed to the model.
- `tests/` — currently empty; no test project exists yet.

## `src/Aiyara.Harness.Cli/`

- `Program.cs` — startup: workspace trust check, offers to generate `AIYARA.md` if missing (runs
  one chat turn via `ChatSession.RunInitTurnAsync`), loads `AIYARA.md`/`FILES.md`/`TOOLS.md`/
  `COMMANDS.md`/`MEMORY.md` into the system prompt, builds the chat engine (and an
  `IEmbeddingClient`) for whichever provider `models.json`'s `Provider` names (`Ollama` or
  `LMStudio` - see `src/Aiyara.Harness.Tools/Providers/`), connects to any MCP servers in
  `mcp.json` and appends their tools (see `src/Aiyara.Harness.Tools/Mcp/`), builds/refreshes the
  RAG index and adds `search_documents` if `rag.json`'s `Enabled` is true (see
  `src/Aiyara.Harness.Tools/Rag/`), registers the built-in tools, starts the chat session. Builds
  the Serilog file sink's path here rather than in `appsettings.json` -
  `%USERPROFILE%\.aiyara\logs\harness-.log`, via `UserConfigPaths.Directory`, needs to resolve at
  runtime to the current user's profile, not a path relative to wherever the process happens to be
  launched from (which is what a relative path in the JSON config would do). `appsettings.json`
  still owns the console sink and log-level settings.
- `ChatSession.cs` — the interactive chat REPL loop; also exposes `RunInitTurnAsync` for the
  one-off `AIYARA.md` generation turn at startup. `RunSlashCommandAsync` resolves `/<name>` against
  built-in commands first, then falls back to treating `<name>` as a skill (`/<skill-name> [extra
  instructions]`) - loads its content via `SkillStore.LoadContent` and sends it as the user's turn
  through the normal `SendMessageAsync` path, this harness's user-triggered equivalent of the
  model calling `use_skill` itself. A built-in command always wins on a name clash.
- `Persona.cs` — the assistant's persona/system prompt (baked in, not user-editable).
- `ToolRegistry.cs` — tracks which registered tools are enabled/disabled for the running session.
- `TerminalUI.cs`, `ConsoleTheme.cs`, `NativeConsole.cs` — console rendering.
- `SlashInputReader.cs` — the input box. Wraps long lines across up to 6 rows instead of
  scrolling horizontally, and the buffer can hold real line breaks - typed via Shift/Alt+Enter, or
  inserted automatically when a multi-line paste is detected (a paste arrives as a burst of
  synthetic keystrokes including one Enter per line break, distinguished from a real Enter
  keypress by `Console.KeyAvailable` being true right after). Top/bottom border only (plain
  horizontal lines, no side borders or corners) - `ClearRow` never touches a row's very last
  column, so anything drawn there (e.g. a box corner) would never get cleared again for the rest
  of the session; keep any future border/line drawing at most `width - 1` columns wide.
  `BuildSuggestions` (used for both the live "/" dropdown and Tab-completion) merges built-in
  commands with enabled skill names, commands winning any name clash - mirrors the dispatch
  priority `ChatSession.RunSlashCommandAsync` actually uses, so what autocompletes is always what
  would actually run.
- `StatuslineRunner.cs` — runs the configured statusline command each turn, via `powershell.exe`
  on Windows (`/bin/sh` elsewhere). PowerShell, not `cmd.exe`, is deliberate: it supports
  `$(...)` command substitution, matching the bash/zsh-style syntax users tend to write for a
  dynamic statusline, and a `[Console]::OutputEncoding` UTF-8 override is injected ahead of the
  user's command so non-ASCII output (e.g. Thai labels) doesn't get mangled by the legacy ANSI
  codepage a freshly spawned redirected shell starts on. A same-line `chcp 65001 &&` prefix does
  *not* fix that encoding issue under `cmd.exe` - it parses/tokenizes the whole `/c` argument
  under the old codepage before executing anything in it.
- `Commands/` — the six built-in slash commands (`/config`, `/model`, `/tools`, `/skills`,
  `/clear`, `/workspace`) and their registry. `/clear` resets `chat.Messages` back to just the
  system message and re-shows the welcome screen via `SlashCommandContext.TerminalUI` - it doesn't
  touch tool/skill enable state or the task list. Every enabled skill is *also* invokable as
  `/<skill-name>` even though it isn't one of these six - see `ChatSession.cs`.

## `src/Aiyara.Harness.Models/Config/`

- `Workspace.cs` — the workspace root; every file tool resolves paths through
  `Workspace.ResolvePath`, which prompts for consent (and remembers the decision in `trust.json`)
  when a path escapes the workspace.
- `TrustStore.cs` / `TrustOptions.cs` — persisted trust decisions (`trust.json`): trusted workspace
  roots and approved external paths. Survives across runs.
- `ConsoleSelect.cs` — shared "This &lt;noun&gt; requires approval" arrow-key/typed option selector
  (Claude Code-style), backing both `ConsentPrompt` and `ActionConsent` so their look/interaction
  stay identical. Each option shows a one-line description below it, and an "Esc to cancel" hint
  is shown below the list in interactive sessions (Escape resolves to the last/declining option).
- `ConsentPrompt.cs` — Yes/No prompt (via `ConsoleSelect`) for one-off decisions that don't repeat
  identically within a run: workspace trust, external-path access, the `AIYARA.md` generation offer.
- `ActionConsent.cs` — three-way prompt (Yes / Yes-and-don't-ask-again-for-session / No, via
  `ConsoleSelect`) used by file-writing tools and `run_command`. "Allow for session" is in-memory
  only, not persisted; file tools key broadly (e.g. "write_file"), `run_command` keys narrowly by
  exact command + directory so approving one command doesn't silently approve a different one.
- `AiyaraDocument.cs` / `FilesDocument.cs` / `ToolsDocument.cs` / `CommandsDocument.cs` /
  `MemoryDocument.cs` — read `AIYARA.md` / `FILES.md` / `TOOLS.md` / `COMMANDS.md` / `MEMORY.md`
  from the workspace root. `AIYARA.md` is this harness's equivalent of Claude Code's `CLAUDE.md`.
- `UserConfigStore.cs` / `UserConfigPaths.cs` — JSON config file read/write under
  `%USERPROFILE%\.aiyara\`.
- `TaskBoard.cs` — in-memory task list (not persisted) the model updates via `write_tasks`/
  `update_task` to make multi-step work visible; this harness's equivalent of Claude Code's task list.
- `SkillStore.cs` — reads/writes/deletes/lists skills under `.aiyara/skills/<name>/SKILL.md`; this
  harness's equivalent of Claude Code's Skill system. Two scopes (`SkillScope`): `Workspace`
  (default, this project's own `.aiyara/`, meant to be committed and shared via the repo) and
  `User` (`%USERPROFILE%\.aiyara\skills\`, personal, available from every project). A workspace
  skill shadows a same-named user skill everywhere a plain name is used (listing, `use_skill`,
  `delete_skill` without an explicit scope) - `SkillStore.ResolveScope` is the single place that
  decides which one wins. Skill names are slugified (letters/digits/hyphens only), which also
  rules out path traversal via a crafted name.
- `SkillRegistry.cs` — tracks which skills are enabled for the running session, the skill-level
  counterpart to `ToolRegistry`. Lives here (not alongside `ToolRegistry` in Cli) because
  `UseSkillTool`/`ListSkillsTool` in Tools need to read it, and Tools cannot depend on Cli. `Find`
  looks up a skill by name regardless of enabled state - used by `ChatSession`/`SlashInputReader`
  to resolve `/<skill-name>` as a slash command.
- `ModelsOptions.cs`, `OllamaConnectionOptions.cs`, `LmStudioConnectionOptions.cs`, `McpOptions.cs`,
  `RagOptions.cs`, `StatuslineOptions.cs`, `LoggingOptions.cs` — option types stored as JSON config
  files. `ModelsOptions.Provider` (`Enums/Provider.cs`: `Ollama`/`LMStudio`) picks which server
  `Program.cs` builds a chat engine for at startup - read once, so switching it requires a
  restart; `ModelsOptions` is a `record` (not a plain class) so `SlashCommandContext.SwitchModel`
  can update `Default` via a `with`-expression without clobbering `Provider`.
  `LoggingOptions` (`logging.json`) holds two independent, both-off-by-default toggles
  `ChatSession.FlushThinking` checks fresh on every flush: `ShowThinking` (display the model's
  reasoning live in the terminal) and `LogThinking` (also write it to the log file). Neither
  implies the other.
- `ConfigCommand.cs` — implements the `harness config` CLI subcommand.

## `src/Aiyara.Harness.Tools/`

Each tool is a small class deriving from `BaseTool`, registered in `Program.cs`'s tool list.

- `BaseTool.cs` — base class; catches exceptions from `Execute` and turns them into an error string
  instead of crashing the chat session.
- `OpenFileTools.cs`, `WriteFileTool.cs`, `OpenImageTool.cs`, `SaveImageTool.cs` — read/write text
  and image files.
- `ListFilesTool.cs` — recursively lists the workspace's file tree (read-only, no confirmation).
- `AiyaraDocumentTool.cs`, `FilesDocumentTool.cs`, `ToolsDocumentTool.cs`, `CommandsDocumentTool.cs`,
  `MemoryDocumentTool.cs` — write `AIYARA.md` / `FILES.md` / `TOOLS.md` / `COMMANDS.md` /
  `MEMORY.md` directly.
- `SetStatuslineCommandTool.cs` — sets the statusline command in `statusline.json`.
- `RunCommandTool.cs` — runs an external shell command (e.g. `dotnet build`), always confirmed.
- `HandoffTool.cs` — switches the model's own working mode to a built-in specialist persona
  (`planner`, `reviewer`, `debugger`, or back to `general`) by swapping a marked-off block in the
  live chat engine's (`IChatEngine`, see `Providers/` below) system message; conversation history
  is untouched. Provider-agnostic - works the same against Ollama or LM Studio.
- `WriteTasksTool.cs`, `UpdateTaskTool.cs` — create/update the shared `TaskBoard`.
- `WriteSkillTool.cs`, `DeleteSkillTool.cs`, `UseSkillTool.cs`, `ListSkillsTool.cs` — create/update,
  permanently delete, load, and list skills (see `SkillStore.cs`). Both take an optional `scope`
  ("workspace"/"user"; `write_skill` defaults to "workspace" if omitted, `delete_skill` resolves
  via `SkillStore.ResolveScope` if omitted). `delete_skill` is confirmed per exact skill name *and*
  scope (not a broad key), since deletion is irreversible and a same-named skill can exist in both
  scopes at once.
- `DateTimeTool.cs` — current date/time.
- `CommonTools.cs` — excluded from the build (see `Aiyara.Harness.Tools.csproj`); not currently wired in.

## `src/Aiyara.Harness.Tools/Providers/`

Abstracts the chat backend behind `IChatEngine` (message history, streaming, tool-calling,
thinking events), `IModelCatalog` (list/switch/pull models), and `IEmbeddingClient` (text ->
vectors, for RAG), so `ChatSession`, `HandoffTool`, `SlashCommandContext`, `ModelSlashCommand` and
`RagIndexBuilder` don't know or care which provider is active - `Program.cs` picks the
implementations once at startup based on `ModelsOptions.Provider`.

- `IChatEngine.cs`, `IModelCatalog.cs`, `IEmbeddingClient.cs` — the three interfaces.
- `Ollama/OllamaChatEngine.cs`, `Ollama/OllamaModelCatalog.cs` — thin forwarding wrappers around
  the real `OllamaSharp.Chat` / `IOllamaApiClient` (all the actual behavior is OllamaSharp's).
- `LmStudio/LmStudioChatEngine.cs` — hand-rolled client for LM Studio's OpenAI-compatible
  `/v1/chat/completions` (LM Studio has no equivalent of Ollama's native `/api/chat`). Owns the
  full streaming + agentic tool-call loop itself over a raw `HttpClient`/SSE, so it presents the
  same `IChatEngine` surface `ChatSession` already expects from Ollama.
- `LmStudio/LmStudioToolCallAccumulator.cs` — accumulates streamed `tool_calls[]` deltas by their
  `index` into complete tool calls (LM Studio streams a call's `id`/`name` once, then only
  `arguments` fragments after).
- `LmStudio/LmStudioJson.cs` — request/response JSON plumbing: message/tool-schema translation
  (including routing a tool result's image through a synthetic follow-up `user` message, since LM
  Studio rejects images on a `tool`-role message) and SSE frame parsing.
- `LmStudio/LmStudioModelCatalog.cs` — uses LM Studio's own `/api/v0/models` (richer than the
  plain OpenAI `/v1/models`) for loaded-state and `tool_use` capability per model; `PullModelAsync`
  throws `NotSupportedException` since LM Studio has no API to pull a model it doesn't have.
- `Ollama/OllamaEmbeddingClient.cs` — wraps `IOllamaApiClient.EmbedAsync`.
- `LmStudio/LmStudioEmbeddingClient.cs` — raw `POST /v1/embeddings` (OpenAI-compatible), parses
  `data[].embedding` in `index` order.

## `src/Aiyara.Harness.Tools/Mcp/`

- `McpTool.cs` — wraps one MCP server's tool as an ordinary `BaseTool`: translates its JSON Schema
  into OllamaSharp's `Parameters`/`Property` shape (flat Type/Description/Enum only - same
  fidelity every hand-written tool here already has), and `Execute` proxies to
  `McpClient.CallToolAsync`, blocking on it since `IInvokableTool.InvokeMethod` is synchronous.
- `McpToolLoader.cs` — connects to every server listed in `mcp.json` over stdio (via the official
  `ModelContextProtocol.Core` client) at startup and exposes each tool as `<server>_<tool>`. A
  server that fails to connect is skipped with a warning logged, not fatal to the harness.

## `src/Aiyara.Harness.Tools/Rag/`

Wired in only if `rag.json`'s `Enabled` is true; exposes retrieval as an opt-in tool
(`search_documents`) the model calls on demand, the same pattern as MCP tools/skills/`handoff` -
not an always-on prompt-injection pipeline, so `ChatSession` needed no changes.

- `DocumentChunker.cs` — plain character-based sliding-window chunking (`ChunkSize`/`ChunkOverlap`
  from `rag.json`), no NLP dependency.
- `RagIndexFile.cs` — the on-disk JSON shape persisted at `<VectorStorePath>/index.json`
  (`RagIndexFile` > `RagDocumentEntry` > `RagChunkEntry`), keyed by each file's relative path and
  last-write time.
- `RagIndex.cs` — in-memory view over an already-built `RagIndexFile`'s documents; `Search` ranks
  chunks by cosine similarity to a query embedding.
- `RagIndexBuilder.cs` — `BuildAsync` resolves `DocumentsPath`/`VectorStorePath` through
  `Workspace.ResolvePath` (`VectorStorePath` defaults to `.aiyara/rag` under the workspace root
  when empty), discards and fully rebuilds the persisted index if `EmbeddingModel`/`ChunkSize`/
  `ChunkOverlap` no longer match the current config, and otherwise reuses a file's stored
  chunks/embeddings unchanged when its last-write time hasn't moved - only new/modified files cost
  an embedding call. Skips noise directories (same list as `ListFilesTool`, plus `.aiyara` itself)
  and files over 2 MB.
- `SearchDocumentsTool.cs` — `BaseTool` named `search_documents`; embeds the query via the active
  provider's `IEmbeddingClient`, calls `RagIndex.Search`, and returns the top `TopK` chunks as
  `[<relative path>]\n<chunk text>` blocks. Blocks synchronously on the embedding call - same
  sync-over-async point `McpTool.Execute` already goes through.

## Conventions

- Config lives under `%USERPROFILE%\.aiyara\` as JSON, read via `UserConfigStore.Load`, which
  seeds defaults on first read.
- Anything that writes to disk resolves its path via `Workspace.ResolvePath` first.
- See `TOOLS.md` for how the model itself should use tools in this project.
