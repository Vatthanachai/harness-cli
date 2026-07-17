# t1-harness

A .NET console chat client for a local [Ollama](https://ollama.com) server, with streaming
responses, tool-calling, and in-chat slash commands for managing config and models.

## Projects

| Project | Purpose |
|---|---|
| `src/Harness.Cli` | Console entry point, chat REPL, slash commands, persona/system prompt. |
| `src/Harness.Models` | Config option types and the JSON-backed user config store (`harness config` CLI). |
| `src/Harness.Tools` | Tools exposed to the model (date/time, weather stub) via `[OllamaTool]`. |

## Requirements

- .NET SDK `10.0.301` (see `global.json`)
- An [Ollama](https://ollama.com) server reachable from the machine running the CLI (defaults to `http://localhost:11434`)

## Running

```bash
dotnet run --project src/Harness.Cli
```

Type a message and press Enter to chat. Type `exit` or send an empty line to quit.

## Configuration

Settings are stored as JSON files under `%USERPROFILE%\.aiyara\`, seeded with defaults the first
time each is read:

| File | Options |
|---|---|
| `ollama.json` | `BaseUrl` — Ollama server URL. `AccessToken` — optional bearer token sent as `Authorization: Bearer <token>`. |
| `models.json` | `Default` — model used for chat completions, e.g. `gemma4:e4b`. |
| `mcp.json` | `Servers` — MCP server definitions (name, command, args, env). Reserved for upcoming MCP support; not yet wired into the chat loop. |
| `rag.json` | `Enabled`, `DocumentsPath`, `VectorStorePath`, `EmbeddingModel`, `ChunkSize`, `ChunkOverlap`, `TopK`. Reserved for upcoming retrieval-augmented generation support; not yet wired into the chat loop. |

Edit them from the command line without starting a chat session:

```bash
harness config path                          # print the config directory location
harness config show [category]               # print current config (all, or one category)
harness config set <category> <key> <value>  # change a single value
harness config edit <category>                # open a category's JSON file in your default editor
```

`category` is one of `ollama`, `models`, `mcp`, `rag`.

### In-chat slash commands

While chatting, type `/` to see the list of available commands as you type (autocomplete
updates live, Tab to complete). Two commands are built in:

- `/config` — same as the `harness config` CLI above, without leaving the chat (e.g. `/config set ollama AccessToken <token>`).
- `/model` — list models available on the Ollama server, or switch to one:
  - `/model` lists local models, marking the active one.
  - `/model <name>` switches to `<name>` if it's already pulled; otherwise pulls it from Ollama first (streaming progress), then switches. If Ollama can't be reached or doesn't have the model, you'll get a clear error instead.

Switching models via `/model` also persists the choice to `models.json` as the new default.

## Tools

The model can call these during a conversation (registered in `src/Harness.Cli/Program.cs`):

- `get_current_datetime` (`DateTimeTool`) — current date/time.
- `GetCurrentDate`, `GetCurrentTime`, `GetWeather` (`CommonTools`) — currently defined but excluded from the `Harness.Tools` build (`CommonTools.cs` is compiled out in `Harness.Tools.csproj`); wire it back in and register the tool to enable them.

## Persona

The assistant's persona and system prompt live in `src/Harness.Cli/Persona.cs`. It's baked into
the app rather than user-editable config, by design.

## Logging

Logging is configured via Serilog in `appsettings.json`: console output plus a daily rolling file
under `logs/harness-<date>.log` next to the executable.
