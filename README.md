# Aiyara Harness

A .NET console chat client for a local [Ollama](https://ollama.com) or [LM Studio](https://lmstudio.ai)
server — streaming responses, tool-calling (including tools from any configured
[MCP](https://modelcontextprotocol.io) server, and optional retrieval-augmented document search),
in-chat slash commands, a workspace-scoped trust/consent model, and a Claude Code-inspired Skill
system. Confines its file/shell tools to a single workspace folder per run, the same way Claude
Code confines itself to the folder it's launched in.

## Projects

| Project | Purpose |
|---|---|
| `src/Aiyara.Harness.Cli` | Console entry point, chat REPL, slash commands, persona/system prompt, console rendering. |
| `src/Aiyara.Harness.Models` | Config option types, the JSON-backed user config store, workspace/trust/consent, skills, tasks. |
| `src/Aiyara.Harness.Tools` | Tools exposed to the model (files, images, skills, tasks, shell commands, statusline, ...). |
| `tests/` | Currently empty — no test project exists yet. |

## Requirements

- .NET SDK `10.0.301` (see `global.json`)
- Either an [Ollama](https://ollama.com) server reachable from the machine running the CLI (defaults to `http://localhost:11434`), or an [LM Studio](https://lmstudio.ai) server (`lms server start`, defaults to `http://localhost:1234`) — see [Choosing a provider](#choosing-a-provider)

## Running

```bash
dotnet run --project src/Aiyara.Harness.Cli [workspace-path]
```

`workspace-path` is optional — omit it to use the current directory. The first time you point the
harness at a given folder, you'll be asked to confirm you trust it (arrow-key Yes/No prompt); that
decision is remembered in `trust.json` so you won't be asked again for the same folder. If the
folder has no `AIYARA.md` yet, you'll also be offered to have the model generate one from the
project's contents.

Type a message and press Enter to chat (Shift/Alt+Enter, or a multi-line paste, inserts a line
break instead of sending). Type `/` to see available slash commands, or `exit` to quit.

## Workspace & trust

Every file- and shell-touching tool resolves paths through the current workspace root; a path that
would land outside it triggers an approval prompt (or is silently allowed if you approved that
exact path in a past run — see `trust.json` below). `/workspace` shows the current root and which
project docs (`AIYARA.md`, `FILES.md`, etc.) are currently loaded from it.

## Configuration

Settings are stored as JSON files under `%USERPROFILE%\.aiyara\`, seeded with defaults the first
time each is read:

| File | Options |
|---|---|
| `ollama.json` | `BaseUrl` — Ollama server URL. `AccessToken` — optional bearer token sent as `Authorization: Bearer <token>`. |
| `lmstudio.json` | `BaseUrl` — LM Studio server URL. `AccessToken` — optional bearer token sent as `Authorization: Bearer <token>`. |
| `models.json` | `Default` — model used for chat completions, e.g. `qwen3:8b`. `Provider` — `Ollama` or `LMStudio`; which server the harness talks to. Read once at startup — restart the harness after changing it. |
| `statusline.json` | `Command` — shell command run each turn to build the status line under the input box (PowerShell on Windows, `/bin/sh` elsewhere; receives `{Model, ToolsEnabled, ToolsTotal, Cwd}` as JSON on stdin). Empty = the built-in `model: ... - tools x/y` text. |
| `trust.json` | `TrustedWorkspaces`, `AllowedExternalPaths` — folders and files you've already approved; managed automatically by the trust/consent prompts, not usually hand-edited. |
| `logging.json` | `ShowThinking` — whether the model's thinking/reasoning is displayed live in the terminal. `LogThinking` — whether it's also written to the log (console + rolling file). Two independent toggles; **both off by default.** |
| `mcp.json` | `Servers` — [MCP](https://modelcontextprotocol.io) server definitions (`Name`, `Command`, `Args`, `Env`), each launched over stdio at startup. Every tool the server reports is exposed to the model as `<Name>_<tool>` (e.g. a server named `github` exposing `search_issues` becomes `github_search_issues`), alongside the built-in tools — `/tools` lists and toggles them the same way. A server that fails to launch or complete the MCP handshake is skipped with a warning logged, rather than stopping the harness from starting. |
| `rag.json` | `Enabled` — turns retrieval-augmented generation on. `DocumentsPath` — folder (relative to the workspace root, unless absolute) to index. `VectorStorePath` — where the index is persisted; defaults to `.aiyara/rag` under the workspace root if left empty. `Backend` — `Json` (default; one JSON file, simplest) or `Sqlite` (one shared `vectors.db`, only rewrites changed rows, safe for concurrent readers/writers). `EmbeddingModel` — embedding model name on whichever provider is active (e.g. `nomic-embed-text` on Ollama, `text-embedding-nomic-embed-text-v1.5` on LM Studio). `ChunkSize`/`ChunkOverlap` — characters per chunk / shared between consecutive chunks. `TopK` — chunks returned per query. When enabled, the harness (re)indexes `DocumentsPath` at startup — incrementally, only embedding files that are new or changed since the index was last built — and exposes a `search_documents` tool the model calls on demand (see [Tools](#tools)); a bad path or embedding failure is logged as a warning and just leaves `search_documents` unavailable, rather than stopping the harness from starting. |

Edit them from the command line without starting a chat session:

```bash
harness config path                          # print the config directory location
harness config show [category]               # print current config (all, or one category)
harness config set <category> <key> <value>  # change a single value
harness config edit <category>                # open a category's JSON file in your default editor
```

`category` is one of `ollama`, `lmstudio`, `models`, `mcp`, `rag`, `statusline`, `trust`, `logging`.

### Choosing a provider

```bash
harness config set models Provider LMStudio   # or: Ollama
```

then restart the harness — `Provider` is only read at startup. LM Studio's "just-in-time model
loading" means requesting a model that's downloaded but not currently loaded starts it
automatically; there's no separate load step needed. LM Studio has no API for pulling a model it
doesn't already have (unlike Ollama's registry pull) — download it first with `lms get <model>`
or the LM Studio app, then `/model <name>` will find it.

### In-chat slash commands

While chatting, type `/` to see the list of available commands as you type (autocomplete updates
live, Tab to complete):

- `/config` — same as the `harness config` CLI above, without leaving the chat (e.g. `/config set ollama AccessToken <token>`).
- `/model` — list models available on the active provider (Ollama or LM Studio), or switch to one:
  - `/model` lists local models, marking the active one; on LM Studio, models that don't advertise `tool_use` capability are flagged `(no tool_use)` since this harness always sends tool definitions.
  - `/model <name>` switches to `<name>` if it's already available; on Ollama, a missing model is pulled first (streaming progress); LM Studio has no pull API, so a missing model instead errors with a message suggesting `lms get <name>`. If the server can't be reached, you'll get a clear error instead.
  - Switching also persists the choice to `models.json` as the new default (without touching `Provider`).
- `/tools` — list tools and their enabled state, or `/tools on|off <name>` to toggle one for the rest of the session.
- `/skills` — list skills and their enabled state, or `/skills on|off <name>` to toggle one (see [Skills](#skills) below).
- `/clear` — reset the conversation back to just the system message and re-show the welcome screen. Doesn't touch tool/skill enable state or the task list.
- `/workspace` — show the current workspace root and which project docs are loaded from it.

## Tools

The model can call these during a conversation (registered in `src/Aiyara.Harness.Cli/Program.cs`):

- `get_current_datetime` — current date/time.
- `open_file`, `write_file`, `open_image`, `save_image` — read/write text and image files.
- `list_files` — recursively list the workspace's file tree (read-only, no confirmation).
- `run_command` — run an external shell command (e.g. `dotnet build`); always confirmed.
- `set_statusline_command` — set the custom statusline command in `statusline.json`.
- `write_aiyara_document`, `write_files_document`, `write_tools_document`, `write_commands_document`, `write_memory_document` — write `AIYARA.md` / `FILES.md` / `TOOLS.md` / `COMMANDS.md` / `MEMORY.md` directly (see [Project docs](#project-docs)).
- `write_tasks`, `update_task` — create/update the in-session task list.
- `write_skill`, `use_skill`, `list_skills`, `delete_skill` — manage skills (see [Skills](#skills)).
- `handoff` — switch the model's own working mode to a built-in specialist persona (`planner`, `reviewer`, `debugger`, or back to `general`).

`CommonTools.cs` (`GetCurrentDate`, `GetCurrentTime`, `GetWeather`) is excluded from the build and not currently wired in.

Plus, dynamically:
- One tool per capability reported by each connected [MCP](#configuration) server (`mcp.json`), named `<server>_<tool>`.
- `search_documents` — searches the RAG index built from `rag.json`'s `DocumentsPath`, if `Enabled: true`.

## Skills

This harness's equivalent of Claude Code's own Skill system: a named, reusable Markdown playbook
the model can load on demand via `use_skill` instead of re-deriving an approach every session.
Each lives at `.aiyara/skills/<name>/SKILL.md` with a small frontmatter block (`name`,
`description`) followed by the instructions.

Two scopes:

- **Workspace** (default) — under this project's own `.aiyara/`, meant to be committed and shared via the repo.
- **User** — under `%USERPROFILE%\.aiyara\skills\`, personal, available from every project on the machine.

A workspace skill shadows a same-named user skill everywhere a plain name is used (`use_skill`,
`list_skills`, `delete_skill` without an explicit scope) — the project-specific copy always wins
over the personal one. `write_skill`/`delete_skill` take an optional `scope: "workspace" | "user"`
parameter to target one explicitly.

## Project docs

`AIYARA.md`, `FILES.md`, `TOOLS.md`, `COMMANDS.md`, `MEMORY.md` live at the workspace root and are
auto-loaded into the system prompt at startup, so the model doesn't need to re-explore the project
every session. `AIYARA.md` is this harness's equivalent of Claude Code's `CLAUDE.md`. The model
keeps them current via the `write_*_document` tools as the project (or its own working
conventions) changes.

## Persona

The assistant's persona and system prompt live in `src/Aiyara.Harness.Cli/Persona.cs`. It's baked
into the app rather than user-editable config, by design.

## Logging

Logging is configured via Serilog: console output from `appsettings.json`, plus a daily rolling
file under `%USERPROFILE%\.aiyara\logs\harness-<date>.log` (set in code in `Program.cs`, since the
path needs to resolve to the current user's profile at runtime rather than a path relative to
wherever the harness happens to be launched from) - one shared log location across every
workspace, alongside the rest of the user-level config. Whether the model's thinking/reasoning is
shown live in the terminal, and whether it's also written to the log, are two independent toggles
in `logging.json` (`ShowThinking`, `LogThinking`) - both off by default - see
[Configuration](#configuration).
