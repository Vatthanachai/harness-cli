# TOOLS.md

Auto-loaded into the system prompt at startup (see `Program.cs`). Project-specific conventions for
how the model should use its own tools here — not a description of what the tools do (their
`Description` fields already cover that).

## Before writing code

- Call `list_files` first if you haven't seen the current tree yet — don't guess at paths.
- Use `open_file` to read a file's actual content before editing it with `write_file`; don't
  overwrite a file you haven't read in this session.

## After editing `.cs` files

- Run `run_command` with `dotnet build aiyara-harness.slnx` and check for errors before considering
  the change done. This will always prompt for confirmation — that's expected, not a bug.

## `run_command` usage

- Prefer the narrowest command that answers the question (e.g. `dotnet build` over a full clean +
  restore + build) — every call needs a fresh user confirmation, so don't chain unrelated commands
  into one call hoping to save prompts.
- Never suggest destructive commands (`git reset --hard`, `git push --force`, `rm -rf`, dropping
  databases) through this tool without calling it out explicitly first.

## `set_statusline_command` usage

- The command runs via `powershell.exe` on Windows (`/bin/sh` elsewhere) - write PowerShell syntax
  (`$(...)` for command substitution, e.g. `$(git rev-parse --abbrev-ref HEAD)`), not `cmd.exe`
  batch syntax (`%cd%`, `for /f ...`). `cmd.exe` doesn't understand `$(...)` at all and just
  prints it back literally instead of evaluating it - a real bug hit in practice, see
  `StatuslineRunner.cs`.
- Non-ASCII output (Thai labels, etc.) is handled for you - don't add your own `chcp`/encoding
  workarounds to the command.

## Adding a NuGet package

- Package versions are centrally managed (`Directory.Packages.props`, `ManagePackageVersionsCentrally=true`)
  - prefer `run_command` with `dotnet add package <name>` in the project that needs it; the CLI is
  CPM-aware and updates both files correctly on its own.
- If editing `.csproj` by hand instead, add `<PackageVersion Include="..." Version="..." />` to
  `Directory.Packages.props` and a bare `<PackageReference Include="..." />` (no `Version`
  attribute) to the project's `.csproj`. A `Version` on a `PackageReference` now causes a build
  error (NU1008) because CPM is on.

## `web_search` / `web_fetch` usage

- Only available if `websearch.json`'s `Enabled` is true and its `BaseUrl` (a local SearXNG
  instance) is reachable - if these tools aren't in your list, don't ask the user to enable them
  mid-task; just say web search isn't configured.
- `web_search` returns title/URL/snippet only. Follow up with `web_fetch` on a specific result's
  URL when you need the full page content, not the snippet.
- `web_fetch` truncates to 8,000 characters - for a long page, prefer a narrower `web_search` query
  over relying on `web_fetch` to surface the right section.

## `ocr_image` usage

- Only available if `ocr.json`'s `Enabled` is true and its `TessDataPath` has a `.traineddata` file
  matching `Language` - if it isn't in your list, don't ask the user to enable it mid-task; just
  say OCR isn't configured.
- It's a deterministic transcription tool, not a description tool - use it to pull literal text out
  of a screenshot/scan/photo. For "what's in this image" questions about a non-text image, use
  `open_image` instead (only useful if the active model is vision-capable).
- Pass `language` per call only to override `ocr.json`'s default for that one image (e.g. a Thai
  document while the default is `eng`) - don't pass it just to restate the default.

## `write_skill` / `delete_skill` scope

- Default to `scope: "workspace"` (or omit it) for anything specific to this project - it lives in
  `.aiyara/skills/` at the repo root and is meant to be committed and shared with the team.
- Only use `scope: "user"` when the user is explicit that a skill is a personal, cross-project
  preference (e.g. "however I write commit messages, everywhere") - it's invisible to anyone else
  who clones this repo.
- If a workspace skill and a user skill share a name, the workspace one wins for `use_skill` and
  for `delete_skill` calls that don't specify `scope` - mention this if you're about to create a
  workspace skill that would shadow an existing user one (or vice versa), since the user's
  cross-project skill would silently stop applying here.

## Keeping `FILES.md` / `TOOLS.md` current

- If you add, rename, or remove a source file, or add a new tool, update `FILES.md` /
  `write_files_document` accordingly in the same change — don't let it drift.
- If you notice a tool being misused (e.g. `write_file` used instead of `write_files_document` for
  `FILES.md`), note the correct convention here via `write_tools_document`.
