`dispatch_agent` lets the model delegate a bounded, self-contained task to a genuinely isolated
sub-agent - its own `IChatEngine`, own message history, own tool subset - and get back its final
report as a single tool result. Always registered (no config gate, unlike [[Web Search]]/[[OCR]] -
it needs no external service or credential, just the same provider the primary conversation already
talks to).

## Why this is different from `handoff`

The harness already had one "multi-agent-ish" tool before this: `handoff`
(`src/Aiyara.Harness.Tools/HandoffTool.cs`). It only swaps the *persona* block of the **same**
running system message in-place - same conversation, same message history, no isolation. It's a
lighter-weight thing entirely. `dispatch_agent` is a real sub-agent: a second, independent
`IChatEngine` with its own history that the primary conversation never sees except through the
final report text.

## `IChatEngineFactory` — the piece that made this possible

`Program.cs` used to build its one `IChatEngine` inline, once, in the
`if (models.Provider == Provider.Ollama) {...} else {...}` branch - nothing else could reuse that
construction. `src/Aiyara.Harness.Tools/Providers/IChatEngineFactory.cs` factors it out:
`Create(model, systemPrompt)` builds a brand new engine against whichever client `Program.cs`
already opened (`OllamaChatEngineFactory`/`LmStudioChatEngineFactory`, same per-provider folder
split as every other `Providers/` abstraction) - no second connection, no new config.

## `dispatch_agent` itself

`src/Aiyara.Harness.Tools/DispatchAgentTool.cs`. Two `agent_type` values, same hardcoded-catalog
shape `HandoffTool.Personas` already uses (name + model-facing description + focus text baked into
the sub-agent's system prompt):

- **`explore`** — read-only investigation. Tool subset is an *allowlist* (`open_file`, `list_files`,
  `open_image`, `get_current_datetime`, `list_skills`, plus `search_documents`/`web_search`/
  `web_fetch`/`ocr_image` if registered this session). No MCP tools - there's no generic way to know
  which of those are read-only, so all are withheld rather than guessing.
- **`general`** — everything currently registered, minus the exclusion set below. Same "any
  self-contained task" scope as this repo's own environment's `general-purpose` agent concept.

**Always excluded, for both types** — not a per-type relevance call, a correctness guardrail:
`dispatch_agent` (itself - nesting is capped at depth 1 structurally, no recursion counter needed),
`handoff`, `write_tasks`, `update_task`. The reason those three specifically: the tool *instances*
handed to `DispatchAgentTool` are the exact same objects the primary conversation uses -
`HandoffTool` holds a reference to the **primary** `IChatEngine` and mutates its system message;
`WriteTasksTool`/`UpdateTaskTool` hold the one shared `TaskBoard`. A sub-agent calling either would
silently corrupt the primary conversation's persona or task list, not just do something irrelevant
to its own task - so it's a hard exclusion regardless of `agent_type`.

**Registration order matters:** `Program.cs` adds `dispatch_agent` to the tool list *last*, after
the RAG/websearch/OCR conditional blocks, and hands it a `tools.ToList()` snapshot at that point -
so `ToolsFor` always reflects whichever optional tools actually ended up enabled this session,
without `DispatchAgentTool` needing to know about `rag.json`/`websearch.json`/`ocr.json` itself.

**Model selection:** a sub-agent always uses `primaryEngine.Model` read *live* at dispatch time, not
a value captured at startup - so switching model with `/model` before dispatching applies to
sub-agents too, no separate config knob.

**Execution:** synchronous, sync-over-async inside `Execute` (`await foreach` over `SendAsync`,
accumulated into the final report string) - same pattern `WebSearchTool`/`SearchDocumentsTool`
already use. It runs to completion before `dispatch_agent` returns; there's no way to check on it
partway through or cancel it once dispatched (the delegate/sub-agent shape was chosen explicitly
over a parallel-worker or background/async shape - concurrent agents would need real
thread-safety work across `IChatEngine`/`ToolRegistry`/console output that the current
single-threaded REPL doesn't have).

## Console visibility

A tool in `Aiyara.Harness.Tools` can't call `ConsoleTheme` directly - `Aiyara.Harness.Cli` depends
on `Tools`, not the other way around (same reason [[Skills System]]'s `SkillRegistry` lives in
`Models` instead of next to `ToolRegistry` in `Cli`). Instead `DispatchAgentTool` re-exposes its
sub-engine's `OnThink`/`OnToolCall`/`OnToolResult` as its own `OnSubAgentThink`/`OnSubAgentToolCall`/
`OnSubAgentToolResult` events, and `ChatSession.EnsureEventsWired` subscribes to every registered
`DispatchAgentTool` the same way it wires the primary conversation's own events, prefixed `agent: `
so a long-running delegate isn't a silent black box. Safe to share `_thinkBuffer`/`FlushThinking`
with the primary conversation: `Execute` runs synchronously inside the primary's own tool-call loop,
so a primary stream and a sub-agent stream are never in flight on the console at the same time.

## No new consent/trust plumbing needed

`Workspace.ResolvePath`, `TrustStore`, `ActionConsent` are all static and process-wide - a
sub-agent's file/write/`run_command` calls go through the exact same consent prompts the primary
agent already triggers. Nothing agent-specific to bypass or duplicate there.

## Related

[[Index]] · [[Tools]] · [[Architecture]] · [[Providers]] · [[Web Search]] · [[OCR]]
