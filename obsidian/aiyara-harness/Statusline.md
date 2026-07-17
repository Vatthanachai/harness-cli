The custom status line shown under the input box. Configured via `statusline.json`'s `Command`
(user-scope, see [[Architecture]]), set through `/config set statusline Command "..."` or the
model's `set_statusline_command` tool. Implemented in `StatuslineRunner.cs`.

The command receives `{Model, ToolsEnabled, ToolsTotal, Cwd}` as JSON on stdin, and its trimmed
first line of stdout becomes the status text. Empty command, failure, timeout, or non-zero exit all
fall back to the built-in `model: ... - tools x/y` text.

## Runs via PowerShell, not cmd.exe

`StatuslineRunner` launches `powershell.exe` on Windows (`/bin/sh` elsewhere) - deliberately not
`cmd.exe`. Reasons, found while debugging a real report of a broken statusline:

1. **Syntax mismatch**: users naturally write bash/zsh-style commands with `$(...)` command
   substitution (e.g. `$(git rev-parse --abbrev-ref HEAD)`). `cmd.exe` has no concept of `$(...)`
   at all - it just prints it back literally instead of evaluating it. PowerShell's `$(...)`
   subexpression operator is close enough to bash's that these commands work with no rewriting.
2. **Encoding**: a freshly spawned, redirected-output shell starts on the system's legacy ANSI
   codepage (874 on a Thai-locale machine, not UTF-8) - any non-ASCII text (Thai labels in the
   reported case) got decoded as UTF-8 on the .NET side and turned into `�` replacement
   characters. Fix: inject `[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false);`
   as the first statement before the user's command runs, plus matching
   `StandardInputEncoding`/`StandardOutputEncoding`/`StandardErrorEncoding = UTF8` on the .NET
   side. **A same-line `chcp 65001 &&` prefix does NOT fix this under `cmd.exe`** - it parses/
   tokenizes the whole `/c` argument under the old codepage before executing anything in it, so by
   the time `chcp` runs the damage is already done. This only surfaced because a repro was run
   line-by-line rather than assumed to work.

## WorkingDirectory bug (also found and fixed)

The spawned process never had `ProcessStartInfo.WorkingDirectory` set, so it silently inherited the
*harness's own* process working directory - not necessarily the workspace folder (`context.Cwd`).
Only matched by coincidence when the harness was launched from inside the workspace with no
explicit path argument. A `git`/`pwd`-based statusline command could read the wrong repo entirely
whenever the two differ. Fixed by setting `WorkingDirectory = context.Cwd` explicitly.

## Current working example

```powershell
$ctx = [Console]::In.ReadToEnd() | ConvertFrom-Json
$branch = git rev-parse --abbrev-ref HEAD 2>$null
$parts = @($ctx.Model, $env:COMPUTERNAME, $ctx.Cwd)
if ($branch) { $parts += $branch }
$parts -join ' | '
```

Produces `model | host | folder | branch` (branch segment omitted entirely outside a git repo,
rather than showing an empty field).

## Related

[[Index]] · [[Architecture]] · [[Tools]]
