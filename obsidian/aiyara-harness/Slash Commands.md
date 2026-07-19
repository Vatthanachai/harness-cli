Typed at the input prompt, starting with `/`. Live autocomplete as you type; Tab completes. Six
built-in commands below - plus every *enabled skill name* is also directly runnable as
`/<skill-name>` (checked only after these six, so none of them can be shadowed by a skill) - see
[[Skills System]] § Invoking directly.

## `/config`

Same as the `harness config` CLI, without leaving the chat.

**Syntax:** `/config <path|show|set|edit> ...`

| Subcommand | Parameters | What it does |
|---|---|---|
| `path` | none | Prints the config directory (`%USERPROFILE%\.aiyara\`). |
| `show` | `[category]` optional | Prints current config - all eight categories, or just one. |
| `set` | `<category> <key> <value>` all required | Changes a single value in that category's JSON file. |
| `edit` | `<category>` required | Opens the category's JSON file in your default editor. |

`category` is one of: `ollama`, `lmstudio`, `models`, `mcp`, `rag`, `statusline`, `trust`, `logging`.

**Examples:**
```
/config path
/config show
/config show statusline
/config set ollama AccessToken sk-abc123
/config set models Provider LMStudio
/config set models Default qwen3:8b
/config set logging ShowThinking true
/config set logging LogThinking true
/config edit statusline
```

## `/model`

Lists or switches the active model on whichever provider is configured (`models.json`'s
`Provider` - see [[Providers]]). Behavior differs a bit between the two:

**Syntax:** `/model [name]`

| Form | Ollama | LM Studio |
|---|---|---|
| `/model` (no args) | Lists local models, marking the active one. | Same, plus flags any model that doesn't advertise `tool_use` capability with `(no tool_use)`. |
| `/model <name>` | Switches to `<name>`; if not already pulled, pulls it first (streaming progress), then switches. | Switches to `<name>` if already downloaded; LM Studio has no pull API, so a missing model errors with a message suggesting `lms get <name>` instead. |

Either way, switching persists `<name>` to `models.json`'s `Default` without touching `Provider`.

**Examples:**
```
/model
/model qwen3:8b
```

## `/tools`

Lists tools, or toggles one on/off for the rest of the session.

**Syntax:** `/tools [on|off <name>]`

| Form | Parameters |
|---|---|
| `/tools` | none - lists every tool with its enabled state |
| `/tools on <name>` | `name` required |
| `/tools off <name>` | `name` required |

**Examples:**
```
/tools
/tools off run_command
/tools on run_command
```

## `/skills`

Lists skills (with scope), or toggles one on/off. See [[Skills System]] for what workspace vs user
scope means - this command's `on`/`off` doesn't take a `scope` argument, it just matches by name
against whichever skill that name currently resolves to.

**Syntax:** `/skills [on|off <name>]`

| Form | Parameters |
|---|---|
| `/skills` | none - lists every skill as `[workspace]`/`[user]`, name, description, enabled state |
| `/skills on <name>` | `name` required |
| `/skills off <name>` | `name` required |

**Examples:**
```
/skills
/skills off example-greeting
/skills on example-greeting
```

## `/clear`

**Syntax:** `/clear` - no parameters.

Resets the conversation back to just the system message and re-shows the welcome screen. Does
*not* touch tool/skill enable state or the task list - only the conversation itself.

## `/workspace`

**Syntax:** `/workspace` - no parameters.

Shows the current workspace root and, for each of `AIYARA.md`/`FILES.md`/`TOOLS.md`/
`COMMANDS.md`/`MEMORY.md`, whether it's loaded from the workspace root or missing.

## Related

[[Index]] · [[Architecture]] · [[Tools]] · [[Providers]] · [[MCP]]
