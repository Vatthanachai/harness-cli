This harness's equivalent of Claude Code's own Skill system - a named, reusable Markdown playbook
the model loads on demand via `use_skill` instead of re-deriving an approach every session. Lives
in `SkillStore.cs`/`SkillRegistry.cs` (`Aiyara.Harness.Models`) plus four tools (`write_skill`,
`use_skill`, `list_skills`, `delete_skill`) and the `/skills` slash command.

Each skill is `.aiyara/skills/<name>/SKILL.md`: a small frontmatter block (`name`, `description`)
followed by the instructions as Markdown - same shape as Claude Code's own skills.

## Two scopes (`SkillScope`)

- **Workspace** (default) - under *this project's own* `.aiyara/`, meant to be committed and
  shared via the repo.
- **User** - under `%USERPROFILE%\.aiyara\skills\`, personal, available from every project on the
  machine.

This mirrors the same user-vs-workspace split as the rest of the config system - see
[[Architecture]].

**A workspace skill shadows a same-named user skill** everywhere a plain name is used: `use_skill`,
`list_skills`, and `delete_skill` when no explicit `scope` is given. `SkillStore.ResolveScope` is
the single place that decides which one wins (workspace checked first, then user) - every other
name-only lookup goes through it, so the shadowing rule can't drift out of sync between call sites.

`write_skill`/`delete_skill` both take an optional `scope: "workspace" | "user"` parameter to
target one explicitly instead of relying on the default/resolution order.

## Why this design

Initially skills only lived at workspace scope. The reasoning for adding user scope: a skill is
"a repeatable approach worth reusing" - some of those are genuinely project-specific (belong in the
repo, shareable with teammates, same as `AIYARA.md`), but others are personal preferences that
don't belong to any one project (e.g. "how I like commit messages phrased") and should follow the
person across every repo they open the harness in, the same way Claude Code's own
`.claude/skills/` (project) vs `~/.claude/` (personal) split works.

## Verified behavior (from testing)

- `write_skill` with no `scope` → workspace, matches pre-existing default behavior.
- A workspace and a user skill can coexist under the same name; `use_skill`/`list_skills` resolve
  to the workspace one.
- `delete_skill` with no `scope` on a colliding name deletes only the workspace copy; the user copy
  survives and becomes the active one afterward.
- Disabling a skill via `/skills off <name>` (or the tool registry) blocks `use_skill` with a clear
  "re-enable with `/skills on <name>`" message, independent of scope.

## Tool parameters

| Tool | Parameters | Required |
|---|---|---|
| `write_skill` | `name`, `description`, `content`, `scope` | `name`, `description`, `content` (`scope` optional, defaults to `"workspace"`) |
| `use_skill` | `name` | `name` |
| `delete_skill` | `name`, `scope` | `name` (`scope` optional, resolved via `SkillStore.ResolveScope` if omitted) |
| `list_skills` | *(none)* | *(none)* |

## Usage examples

Creating a **workspace** skill (default - omit `scope` entirely, or pass `"workspace"`):

```json
write_skill({
  "name": "db-migration-check",
  "description": "Checklist to run before merging a schema migration.",
  "content": "1. Confirm the migration is backward-compatible...\n2. Run it against a copy of prod data...\n3. ..."
})
```

Creating a **user** skill (personal, follows you to every project):

```json
write_skill({
  "name": "git-commit-style",
  "description": "Personal preference for how to phrase git commit messages.",
  "content": "Use imperative mood (\"add\", not \"added\"), no trailing period...",
  "scope": "user"
})
```

Loading a skill's instructions to follow for the current task:

```json
use_skill({ "name": "db-migration-check" })
```

Checking what's currently on disk before deciding whether to create or reuse one:

```json
list_skills({})
```

Deleting a skill - explicit `scope` when you need to be sure which copy goes, e.g. clearing out a
user-scope one while a same-named workspace one stays:

```json
delete_skill({ "name": "example-greeting", "scope": "user" })
```

From the chat prompt directly (not a model tool call), toggling one off/on for the session instead
of deleting it - see [[Slash Commands]]:

```
/skills off db-migration-check
/skills on db-migration-check
```

## Invoking directly with `/<skill-name>`

You don't have to ask the model to use a skill in words - typing `/<skill-name>` runs it directly,
this harness's user-triggered equivalent of the model calling `use_skill` on its own. Implemented
in `ChatSession.RunSlashCommandAsync`: it checks the six built-in commands first, and only if none
match does it fall back to treating the typed name as a skill - so a skill can never shadow a
built-in command, only the reverse.

**Syntax:** `/<skill-name> [extra instructions]`

```
/git-commit-style
/git-commit-style also mention this closes issue #42
```

The skill's content is loaded and sent as your turn (same as if you'd typed a very long message);
any text after the name is appended as additional, specific instructions - useful for pointing a
generic skill at a specific target without rewriting it. A disabled skill gives a clear error
("enable it with `/skills on <name>`") instead of silently doing nothing, and an unknown name falls
through to the normal "Unknown command" error, which now lists skill names alongside the six
built-ins.

The live "/" suggestion dropdown and Tab-completion both know about this too - typing `/` shows
enabled skills mixed in with built-in commands (`SlashInputReader.BuildSuggestions`), using the
same command-wins-on-clash priority as actually running one.

## Related

[[Index]] · [[Architecture]] · [[Tools]]
