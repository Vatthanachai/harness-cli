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

## Keeping `FILES.md` / `TOOLS.md` current

- If you add, rename, or remove a source file, or add a new tool, update `FILES.md` /
  `write_files_document` accordingly in the same change — don't let it drift.
- If you notice a tool being misused (e.g. `write_file` used instead of `write_files_document` for
  `FILES.md`), note the correct convention here via `write_tools_document`.
