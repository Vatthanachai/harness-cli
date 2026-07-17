Aiyara Harness is a .NET console chat client for a local [Ollama](https://ollama.com) server — streaming responses, tool-calling, in-chat slash commands, a workspace-scoped trust/consent model, and a Claude Code-inspired Skill system. It confines its file/shell tools to a single workspace folder per run, the same way Claude Code confines itself to the folder it's launched in.

Solution: `aiyara-harness.slnx`. Three projects: `Aiyara.Harness.Cli` (entry point, chat REPL, console rendering), `Aiyara.Harness.Models` (config types, workspace/trust/skills), `Aiyara.Harness.Tools` (tools exposed to the model).

Requires .NET SDK `10.0.301` and a reachable Ollama server (`http://localhost:11434` by default).

## Notes in this vault

- [[Architecture]] — project layout, config file locations (user-level vs workspace-level)
- [[Slash Commands]] — the six built-in `/` commands (plus any skill, directly runnable as `/<skill-name>`)
- [[Tools]] — the full tool catalog the model can call
- [[Skills System]] — the workspace/user scope split, the shadowing rule, and invoking a skill directly via `/<skill-name>`
- [[Statusline]] — the PowerShell-based custom status line, and the encoding bug it used to have
- [[Multi-line Input Box]] — how the input box wraps, and how paste/Shift+Enter insert real line breaks
