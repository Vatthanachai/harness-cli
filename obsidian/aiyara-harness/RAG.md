If `rag.json`'s `Enabled` is true, the harness (re)indexes `DocumentsPath` at startup and exposes a
`search_documents` tool the model calls on demand - an opt-in tool call, same as [[MCP]] tools,
skills, and `handoff`, not context automatically injected into every turn. Embeddings go through
the same [[Providers]] split as chat (`IEmbeddingClient`), so indexing/search work on whichever
provider is active.

## Why this needed almost no new plumbing

Same reasoning as [[MCP]]: every tool the model can call is already just a `BaseTool` in one flat
`List<object>`, so `src/Aiyara.Harness.Tools/Rag/SearchDocumentsTool.cs` is just another one of
those - it shows up in [[Tools]] (`/tools`) and gets called through either `IChatEngine`
implementation with zero changes anywhere else.

## Storage: `IVectorStore`

Indexing and search go through an `IVectorStore` abstraction (`src/Aiyara.Harness.Tools/Rag/
IVectorStore.cs`) - mirrors the `IEmbeddingClient` split so `RagIndexBuilder`/`SearchDocumentsTool`
don't know or care which backing store is active. Everything is scoped by a `collection` string
(today there's exactly one, `RagIndexBuilder.Collection`) - a placeholder for per-agent collections
later.

`rag.json`'s `Backend` (`VectorStoreBackend`) picks the implementation, chosen in `Program.cs`:

- `Json` (default) - `JsonVectorStore`: one JSON file per collection
  (`<VectorStorePath>/<collection>.json`), loaded into memory on first access and rewritten whole on
  every upsert/remove. Simplest, no extra dependency, fine at small document counts.
- `Sqlite` - `SqliteVectorStore`: one shared `vectors.db` (`Microsoft.Data.Sqlite`), so an
  upsert/remove only touches the rows it changes instead of rewriting a whole file. Runs in WAL mode
  with a `busy_timeout`, so multiple readers/writers (e.g. several agents) can use the store at once
  without one blocking the other.

Both still rank search results by cosine similarity computed in C# over every row/entry in the
collection - no ANN index yet. A store built on one (sqlite-vec, or an external vector db) is the
planned next step once document counts outgrow that - swapping it in should be a new `IVectorStore`
implementation, not a rewrite of the indexing/search flow. Existing `rag.json` files (and indexes
already on disk) keep working unchanged since `Backend` defaults to `Json`.

## Indexing (`RagIndexBuilder.BuildAsync`)

1. Resolves `DocumentsPath`/`VectorStorePath` through `Workspace.ResolvePath` - same workspace
   confinement every other file-touching feature already respects. `VectorStorePath` defaults to
   `.aiyara/rag` under the workspace root when left empty.
2. The active store loads the persisted collection if present - but discards it entirely (full
   rebuild) if its `EmbeddingModel`/`ChunkSize`/`ChunkOverlap` no longer match `rag.json`, rather
   than risk mixing incompatible embedding spaces or chunk boundaries.
3. Walks `DocumentsPath` recursively (same noise-directory skip list as `ListFilesTool`, plus
   `.aiyara` itself; skips files over 2 MB). For each file: `store.GetDocumentAsync` returns the
   stored entry if its `LastWriteUtc` exactly matches what's already stored, reused untouched -
   **no embedding call**. Only a new or modified file gets chunked (`DocumentChunker` - plain
   character sliding window, `ChunkSize`/`ChunkOverlap`), embedded, and written back via
   `store.UpsertDocumentAsync`.
4. Drops entries for files that no longer exist via `store.ListDocumentPathsAsync`/
   `RemoveDocumentAsync`, and returns a `RagIndexStats` (file/chunk counts) for startup logging.

This means a restart with no document changes is fast (no re-embedding at all), and editing one
file only costs an embedding call for that file's chunks - confirmed live (see below).

## `search_documents`

Embeds the query (via the active provider's `IEmbeddingClient`), calls `IVectorStore.SearchAsync`
(cosine similarity ranking, on whichever backend `rag.json`'s `Backend` picked), returns the top
`TopK` as `[<relative path>]\n<chunk text>` blocks. Blocks synchronously on both calls - same
sync-over-async point `McpTool.Execute` already goes through, since `IInvokableTool.InvokeMethod`
is synchronous.

## Verified live, on both providers

Indexed the same 25-file/255-chunk document set with **both** `nomic-embed-text` (Ollama) and
`text-embedding-nomic-embed-text-v1.5` (LM Studio) and got correct, relevant search results on
each. Also confirmed: a restart with no changes re-used the whole index (no embedding calls, near-
instant startup); editing one file caused only that file to be re-embedded, and the new content
showed up in the next search; and pointing `DocumentsPath` at a folder that doesn't exist logs a
warning and just leaves `search_documents` unavailable, instead of crashing the harness.

## `rag.json` example

```json
{
  "Enabled": true,
  "DocumentsPath": ".",
  "VectorStorePath": "",
  "Backend": "Sqlite",
  "EmbeddingModel": "text-embedding-nomic-embed-text-v1.5",
  "ChunkSize": 512,
  "ChunkOverlap": 50,
  "TopK": 4
}
```

`EmbeddingModel` has to match whatever's actually loaded/pullable on the active provider - e.g.
`nomic-embed-text` for Ollama vs. `text-embedding-nomic-embed-text-v1.5` for LM Studio in the
example above, even though both are the same underlying model. `VectorStorePath: ""` uses the
`.aiyara/rag` default. `Backend` is `Json` if omitted - set it to `Sqlite` to switch; switching
after documents are already indexed doesn't migrate data between the two, it starts the new
backend's store empty and reindexes from scratch.

## Related

[[Index]] · [[Architecture]] · [[Providers]] · [[MCP]] · [[Tools]]
