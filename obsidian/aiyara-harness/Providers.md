The harness talks to one of two local LLM servers, picked once at startup by `models.json`'s
`Provider` field (`Ollama` or `LMStudio`) - switching requires a restart, same as any other
startup-only config. Everything downstream (`ChatSession`, `HandoffTool`, `SlashCommandContext`,
`ModelSlashCommand`, `RagIndexBuilder` - see [[RAG]]) goes through three interfaces in
`src/Aiyara.Harness.Tools/Providers/` instead of talking to either server directly, so none of
that code needs to know which one is active.

## `IChatEngine`

Message history, streaming, tool-calling, thinking events - the whole chat surface.

| Implementation | What it actually does |
|---|---|
| `Ollama/OllamaChatEngine` | Thin forwarding wrapper around the real `OllamaSharp.Chat` - all behavior is OllamaSharp's. |
| `LmStudio/LmStudioChatEngine` | Hand-rolled client for LM Studio's OpenAI-compatible `/v1/chat/completions` (LM Studio has no equivalent of Ollama's native `/api/chat`). Owns the full streaming + agentic tool-call loop itself over a raw `HttpClient`/SSE. |

`OllamaSharp.Chat` has no virtual members, so composition (an interface + two implementations)
was the only option - it couldn't be subclassed to add LM Studio support underneath it.

LM Studio specifics, all confirmed against a real running LM Studio server rather than assumed
from docs:

- **Streaming tool calls**: `delta.tool_calls[]` arrives split across chunks, keyed by `index` -
  the first chunk for an index carries `id`/`name` (with empty `arguments`), every following chunk
  for that index carries only an `arguments` fragment. `LmStudio/LmStudioToolCallAccumulator`
  collects these and only parses the full `arguments` JSON once the stream ends.
- **Reasoning**: streams as `delta.reasoning_content`, structurally identical to `delta.content` -
  raised as `OnThink` the same way Ollama's thinking tokens are.
- **Images**: a tool-result message can't carry an image on LM Studio - a `role:"tool"` message
  with image content is rejected outright (`"Invalid 'messages' in payload"`). Confirmed instead
  that a `role:"user"` message with multipart `image_url` content *is* accepted, so
  `LmStudio/LmStudioJson` routes a tool result's image through a synthetic follow-up user message
  rather than attaching it to the tool message the way Ollama does.
- **`tool_call_id`**: sent per spec, but confirmed live that LM Studio doesn't actually enforce it
  (a missing or mismatched one still works) - included anyway since it's free and correct.

## `IModelCatalog`

Lists/switches models, and (where supported) pulls a missing one - backs [[Slash Commands]] §
`/model`.

| Implementation | Source | Notes |
|---|---|---|
| `Ollama/OllamaModelCatalog` | `IOllamaApiClient.ListLocalModelsAsync`/`PullModelAsync` | Reports every model as supporting tools (Ollama's listing endpoint doesn't expose capability info either way). |
| `LmStudio/LmStudioModelCatalog` | LM Studio's own `/api/v0/models` (richer than the plain OpenAI `/v1/models`) | Exposes per-model `state` (loaded/not-loaded) and `capabilities` (`tool_use`) - confirmed live that non-`tool_use` models exist and get correctly flagged. `PullModelAsync` throws `NotSupportedException` - LM Studio has no API to pull a model it doesn't already have; the error message suggests `lms get <model>` instead. |

## `IEmbeddingClient`

Text -> vectors, for [[RAG]] indexing/search - the only other capability besides chat this harness
needs from a provider.

| Implementation | Source |
|---|---|
| `Ollama/OllamaEmbeddingClient` | `IOllamaApiClient.EmbedAsync` |
| `LmStudio/LmStudioEmbeddingClient` | Raw `POST /v1/embeddings` (OpenAI-compatible) - confirmed live, standard `{"data":[{"embedding":[...],"index":0}, ...]}` shape. |

Confirmed live on both: Ollama's `nomic-embed-text` and LM Studio's
`text-embedding-nomic-embed-text-v1.5` both indexed and searched the same document set correctly.

## Config

`models.json`: `Default` (model name) + `Provider` (`Ollama`/`LMStudio`) - now a `record`, not a
plain class, so `SlashCommandContext.SwitchModel` can update `Default` via a `with`-expression
without silently resetting `Provider` back to its default on every `/model` switch. `lmstudio.json`
mirrors `ollama.json`'s shape (`BaseUrl`, `AccessToken`), default `http://localhost:1234`.

**`models.json`** (LM Studio active):
```json
{
  "Default": "google/gemma-4-e4b",
  "Provider": "LMStudio"
}
```

**`ollama.json`**:
```json
{
  "BaseUrl": "http://localhost:11434",
  "AccessToken": ""
}
```

**`lmstudio.json`**:
```json
{
  "BaseUrl": "http://localhost:1234",
  "AccessToken": ""
}
```

`AccessToken`, on either, is sent as `Authorization: Bearer <token>` when non-empty - leave it `""`
for a local server with no auth in front of it.

## Related

[[Index]] · [[Architecture]] · [[MCP]] · [[RAG]] · [[Slash Commands]] · [[Tools]]
