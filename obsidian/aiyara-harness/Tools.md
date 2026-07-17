Registered in `src/Aiyara.Harness.Cli/Program.cs`. Every tool derives from `BaseTool`, which catches
exceptions from `Execute` and turns them into an error string instead of crashing the session.

| Tool | Function name |
|---|---|
| Date/time | `get_current_datetime` |
| Files | `open_file`, `write_file`, `open_image`, `save_image`, `list_files` (read-only) |
| Shell | `run_command` (always confirmed) |
| Statusline | `set_statusline_command` — see [[Statusline]] |
| Project docs | `write_aiyara_document`, `write_files_document`, `write_tools_document`, `write_commands_document`, `write_memory_document` |
| Tasks | `write_tasks`, `update_task` |
| Skills | `write_skill`, `use_skill`, `list_skills`, `delete_skill` — see [[Skills System]] |
| Persona switch | `handoff` (`planner` / `reviewer` / `debugger` / `general`) |

`CommonTools.cs` (`GetCurrentDate`, `GetCurrentTime`, `GetWeather`) is excluded from the build and
not currently wired in.

## Consent model

- File-writing tools and `run_command` confirm via `ActionConsent` (Yes / Yes-for-session / No).
  "Allow for session" is in-memory only. File tools key broadly (e.g. `write_file`); `run_command`
  keys narrowly by exact command + directory.
- `delete_skill` confirms per exact skill name *and* scope (not a broad key) - deletion is
  irreversible, and a same-named skill can exist in both scopes at once (see [[Skills System]]).

## Related

[[Index]] · [[Architecture]] · [[Slash Commands]]
