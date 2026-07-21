Aiyara Harness is a .NET console chat client for a local [Ollama](https://ollama.com) or [LM Studio](https://lmstudio.ai) server — streaming responses, tool-calling (including tools from any configured [MCP](https://modelcontextprotocol.io) server, and optional retrieval-augmented document search), in-chat slash commands, a workspace-scoped trust/consent model, and a Claude Code-inspired Skill system. It confines its file/shell tools to a single workspace folder per run, the same way Claude Code confines itself to the folder it's launched in.

Solution: `aiyara-harness.slnx`. Three projects: `Aiyara.Harness.Cli` (entry point, chat REPL, console rendering), `Aiyara.Harness.Models` (config types, workspace/trust/skills), `Aiyara.Harness.Tools` (tools exposed to the model, including the provider abstraction and MCP client).

Requires .NET SDK `10.0.301` and a reachable Ollama or LM Studio server (`http://localhost:11434` / `http://localhost:1234` by default respectively - see [[Providers]]).

## Notes in this vault

- [[Architecture]] — project layout, config file locations (user-level vs workspace-level)
- [[Providers]] — the `IChatEngine`/`IModelCatalog` abstraction behind Ollama vs LM Studio
- [[MCP]] — connecting to MCP servers and exposing their tools to the model
- [[RAG]] — indexing documents and the `search_documents` tool
- [[Web Search]] — the `web_search`/`web_fetch` tools, backed by a self-hosted SearXNG instance
- [[OCR]] — the `ocr_image` tool, backed by a local Tesseract engine
- [[Slash Commands]] — the six built-in `/` commands (plus any skill, directly runnable as `/<skill-name>`)
- [[Tools]] — the full tool catalog the model can call
- [[Skills System]] — the workspace/user scope split, the shadowing rule, and invoking a skill directly via `/<skill-name>`
- [[Statusline]] — the PowerShell-based custom status line, and the encoding bug it used to have
- [[Multi-line Input Box]] — how the input box wraps, and how paste/Shift+Enter insert real line breaks
