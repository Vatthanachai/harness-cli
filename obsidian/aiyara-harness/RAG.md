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
  without one blocking the other. Ranks search results by cosine similarity computed in C# over
  every row in the collection - no ANN index.
- `SqliteVec` - `SqliteVecVectorStore`: same tables as `Sqlite`, but embeddings live in a
  per-collection `vec_<collection>` table via the [sqlite-vec](https://github.com/asg017/sqlite-vec)
  extension (`vec0`, `distance_metric=cosine`), so `SearchAsync` runs a native `MATCH ... AND k = ?`
  KNN query instead of looping in C#. Verified this ranks identically to `SqliteVectorStore`'s
  brute-force cosine on the same data - it's still exact search (vec0 has no ANN index), just a much
  smaller constant factor from native/SIMD execution. Loading the native extension can fail on an
  unsupported platform; `Program.cs` catches that specifically and falls back to `SqliteVectorStore`
  with a warning logged, so choosing `SqliteVec` never turns into "RAG doesn't work here."

Existing `rag.json` files (and indexes already on disk) keep working unchanged since `Backend`
defaults to `Json`.

## Indexing (`RagIndexBuilder.BuildAsync`)

1. Resolves `DocumentsPath`/`VectorStorePath` through `Workspace.ResolvePath` - same workspace
   confinement every other file-touching feature already respects. A freshly-seeded `rag.json`
   defaults both to their own subfolder under the user config directory - `DocumentsPath` to
   `<UserConfigPaths.Directory>/rag/documents`, `VectorStorePath` to
   `<UserConfigPaths.Directory>/rag/vectorstore` (`RagOptions`) - kept separate so indexing never
   walks the store's own files as source documents. An older `rag.json` with `VectorStorePath: ""`
   still falls back to `.aiyara/rag` under the workspace root instead (`JsonVectorStore`/
   `SqliteVectorStore`/`SqliteVecVectorStore`.`FromOptionsAsync`), for back-compat.
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

## `Sqlite` vs `SqliteVec` benchmark

Synthetic benchmark (768-dim embeddings, matching `nomic-embed-text`; 10 chunks/doc; 30 random
queries at `topK=8` per data point; single run):

| Chunks | Backend | Insert (ms total) | Avg search (ms/query) | Search speedup |
|---|---|---|---|---|
| 500 | Sqlite | 84.6 | 5.85 | - |
| 500 | SqliteVec | 235.0 | 2.55 | 2.30x |
| 2,000 | Sqlite | 209.0 | 15.29 | - |
| 2,000 | SqliteVec | 1,733.5 | 5.19 | 2.95x |
| 8,000 | Sqlite | 783.3 | 55.15 | - |
| 8,000 | SqliteVec | 6,767.0 | 17.46 | 3.16x |
| 20,000 | Sqlite | 1,652.2 | 119.59 | - |
| 20,000 | SqliteVec | 15,792.8 | 45.63 | 2.62x |

Also cross-validated for correctness (not just speed): ranked identically to `SqliteVectorStore`'s
brute-force cosine on a 30-doc random dataset - same top-5 order, scores matching within 1e-3.

Takeaways:
- **Search is 2.3x-3.2x faster with `SqliteVec`, growing with dataset size** - both are still O(n)
  (vec0 has no ANN index), but native/SIMD execution has a much smaller per-row constant than the
  C# loop. This is the operation a user actually waits on per query, so it's the number that matters
  most for picking a backend at scale.
- **Insert is 2.8x-9.6x *slower* with `SqliteVec`** (15.8s vs 1.65s at 20k chunks). In practice this
  is dwarfed by the embedding API call that precedes every upsert (tens to hundreds of ms of
  network/inference latency per file) - but a full/initial reindex of a large corpus really will
  take noticeably longer to *persist* on `SqliteVec` than on `Sqlite`, independent of embedding time.
  Chased this in two rounds:
  1. Suspected `SqliteConnection`-open + `LoadVector()` overhead repeated on every call, so both
     stores were switched to a pooled/reused-connection design (`SqliteConnectionPool`). **Mostly
     wrong**: insert time only dropped ~3-4% (1733ms -> 1665ms at 2k chunks). `Microsoft.Data.Sqlite`
     already pools the underlying native handle per connection string by default, so the old per-call
     `new SqliteConnection()` was apparently already cheap. Kept the change anyway (strictly less
     wasteful, verified safe under 25 concurrent callers - no serialization regression, WAL still
     lets concurrent readers/writers through).
  2. Suspected the fsync-per-commit cost of WAL's default `synchronous=FULL` on every
     one-transaction-per-document upsert, so both stores now set `PRAGMA synchronous = NORMAL`
     (safe in WAL mode against an app crash; only a power-loss/OS-crash risk, acceptable for an
     index that's cheap to rebuild). **This one landed for `Sqlite` but not `SqliteVec`**:
     `Sqlite`'s insert time roughly halved at 8k/20k chunks (783ms -> 368ms; 1652ms -> 807ms), while
     `SqliteVec`'s barely moved (6767ms -> 6081ms; 15793ms -> 15440ms, ~1.1x). That widens the
     relative `SqliteVec`/`Sqlite` insert gap (now ~7x-19x instead of ~3x-10x) even though `SqliteVec`
     got slightly faster in absolute terms - `Sqlite` just improved more. This points at `vec0`'s own
     per-row insert work (JSON-parsing/validating/repacking the vector into its internal storage
     format) as the real remaining cost, not anything transaction- or connection-level - which would
     need batching multiple rows into fewer `INSERT`s (or fewer, larger vec0 writes) to address, a
     real change to `RagIndexBuilder`'s one-document-at-a-time upsert flow. Not chased further since
     nothing currently depends on faster `SqliteVec` indexing.
- `Backend` still defaults to `Json` for backward compatibility - `SqliteVec` is worth recommending
  once a workspace's document count grows large enough that search latency is noticeable, not as a
  blanket default.

## Automated tests

`tests/Aiyara.Harness.Tools.Tests/Rag/` (`dotnet test`) - the repo's first test project, added
specifically because RAG's `IVectorStore` implementations fail *silently* when subtly wrong (a stale
cache or a ranking bug returns slightly-off results, it doesn't throw). `VectorStoreContractTests`
is an abstract base run against all three implementations (round-trip, replace-on-upsert, remove,
list, search ranking, config-mismatch wipe); `VectorStoreRankingCrossValidationTests` cross-checks
`SqliteVec`'s native KNN against `Sqlite`'s brute-force cosine on the same random dataset; and
`JsonVectorStoreTests` covers the `PathFor` collection-name validation. 36 tests total.

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
  "DocumentsPath": "C:\\Users\\alice\\.aiyara\\rag\\documents",
  "VectorStorePath": "C:\\Users\\alice\\.aiyara\\rag\\vectorstore",
  "Backend": "SqliteVec",
  "EmbeddingModel": "text-embedding-nomic-embed-text-v1.5",
  "ChunkSize": 512,
  "ChunkOverlap": 50,
  "TopK": 4
}
```

`EmbeddingModel` has to match whatever's actually loaded/pullable on the active provider - e.g.
`nomic-embed-text` for Ollama vs. `text-embedding-nomic-embed-text-v1.5` for LM Studio in the
example above, even though both are the same underlying model. `DocumentsPath`/`VectorStorePath`
above are what a freshly-seeded `rag.json` now ships with by default (own subfolder each, under the
user config directory) - point `DocumentsPath` at wherever the real source documents live instead.
`Backend` is `Json` if omitted - set it to `Sqlite` to switch; switching after documents are already
indexed doesn't migrate data between the two, it starts the new backend's store empty and reindexes
from scratch.

## Related

[[Index]] · [[Architecture]] · [[Providers]] · [[MCP]] · [[Tools]]
