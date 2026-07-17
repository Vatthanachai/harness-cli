# FILES.md

Auto-loaded into the system prompt at startup (see `Program.cs`). Describes this project's layout
so the model doesn't need to re-explore the tree every session.

## Solution layout

- `aiyara-harness.slnx` — solution file, references all three projects below.
- `global.json` — pins the .NET SDK version.
- `src/Aiyara.Harness.Cli/` — console entry point and chat loop.
- `src/Aiyara.Harness.Models/` — config option types, JSON-backed user config store, workspace/trust/consent.
- `src/Aiyara.Harness.Tools/` — tools exposed to the model.
- `tests/` — currently empty; no test project exists yet.

## `src/Aiyara.Harness.Cli/`

- `Program.cs` — startup: workspace trust check, offers to generate `AIYARA.md` if missing (runs
  one chat turn via `ChatSession.RunInitTurnAsync`), loads `AIYARA.md`/`FILES.md`/`TOOLS.md`/
  `COMMANDS.md`/`MEMORY.md` into the system prompt, builds the Ollama client, registers tools,
  starts the chat session.
- `ChatSession.cs` — the interactive chat REPL loop; also exposes `RunInitTurnAsync` for the
  one-off `AIYARA.md` generation turn at startup.
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
- `StatuslineRunner.cs` — runs the configured statusline command each turn, via `powershell.exe`
  on Windows (`/bin/sh` elsewhere). PowerShell, not `cmd.exe`, is deliberate: it supports
  `$(...)` command substitution, matching the bash/zsh-style syntax users tend to write for a
  dynamic statusline, and a `[Console]::OutputEncoding` UTF-8 override is injected ahead of the
  user's command so non-ASCII output (e.g. Thai labels) doesn't get mangled by the legacy ANSI
  codepage a freshly spawned redirected shell starts on. A same-line `chcp 65001 &&` prefix does
  *not* fix that encoding issue under `cmd.exe` - it parses/tokenizes the whole `/c` argument
  under the old codepage before executing anything in it.
- `Commands/` — slash commands (`/config`, `/model`, `/tools`, `/skills`, `/clear`, `/workspace`)
  and their registry. `/clear` resets `chat.Messages` back to just the system message and re-shows
  the welcome screen via `SlashCommandContext.TerminalUI` - it doesn't touch tool/skill enable
  state or the task list.

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
- `SkillStore.cs` — reads/writes/deletes/lists skills under `.aiyara/skills/<name>/SKILL.md` in the
  workspace; this harness's equivalent of Claude Code's Skill system. Skill names are slugified
  (letters/digits/hyphens only), which also rules out path traversal via a crafted name.
- `SkillRegistry.cs` — tracks which skills are enabled for the running session, the skill-level
  counterpart to `ToolRegistry`. Lives here (not alongside `ToolRegistry` in Cli) because
  `UseSkillTool`/`ListSkillsTool` in Tools need to read it, and Tools cannot depend on Cli.
- `ModelsOptions.cs`, `OllamaConnectionOptions.cs`, `McpOptions.cs`, `RagOptions.cs`,
  `StatuslineOptions.cs` — option types stored as JSON config files.
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
  live `Chat`'s system message; conversation history is untouched.
- `WriteTasksTool.cs`, `UpdateTaskTool.cs` — create/update the shared `TaskBoard`.
- `WriteSkillTool.cs`, `DeleteSkillTool.cs`, `UseSkillTool.cs`, `ListSkillsTool.cs` — create/update,
  permanently delete, load, and list skills (see `SkillStore.cs`). `delete_skill` is confirmed per
  exact skill name (not a broad key), since deletion is irreversible.
- `DateTimeTool.cs` — current date/time.
- `CommonTools.cs` — excluded from the build (see `Aiyara.Harness.Tools.csproj`); not currently wired in.

## Conventions

- Config lives under `%USERPROFILE%\.aiyara\` as JSON, read via `UserConfigStore.Load`, which
  seeds defaults on first read.
- Anything that writes to disk resolves its path via `Workspace.ResolvePath` first.
- See `TOOLS.md` for how the model itself should use tools in this project.
