A practical "how do I actually switch it" companion to [[RAG]] - that note covers the three
`IVectorStore` implementations' internals and the `Sqlite` vs `SqliteVec` benchmark; this one is
just the `rag.json` fields and `harness config` commands to pick and configure one.

## The three backends, in one line each

| `Backend` value | Storage | Pick it when |
|---|---|---|
| `Json` (default) | One JSON file per collection, rewritten whole on every change | Small document counts, zero extra setup - what a fresh `rag.json` ships with |
| `Sqlite` | One shared `vectors.db`, only touched rows rewritten, brute-force cosine search in C# | Bigger corpora, or multiple agents/processes reading the same store concurrently (WAL mode) |
| `SqliteVec` | Same as `Sqlite`, plus the [sqlite-vec](https://github.com/asg017/sqlite-vec) native extension for the KNN search itself | Search latency is noticeable on `Sqlite` already - 2.3x-3.2x faster search, at the cost of slower inserts (see [[RAG]]'s benchmark table). Falls back to `Sqlite` with a logged warning if the native extension can't load on this machine - never a hard failure. |

## The relevant `rag.json` fields

`Enabled`, `DocumentsPath`, `VectorStorePath`, `Backend`, `EmbeddingModel`, `ChunkSize`,
`ChunkOverlap`, `TopK` - all of `RagOptions`
(`src/Aiyara.Harness.Models/Config/RagOptions.cs`). For just picking/switching a vector store
backend, the two that matter are `Backend` (`VectorStoreBackend`: `Json` | `Sqlite` | `SqliteVec`,
case-sensitive - it's written to disk as a plain JSON string via `JsonStringEnumConverter`) and
`VectorStorePath` (where that backend's files live - own subfolder under the user config directory
by default, `<UserConfigPaths.Directory>/rag/vectorstore`).

## Commands

Same `harness config` CLI every category uses (see [[Index]] → README `## Configuration`), plus its
in-chat `/config` equivalent - both operate on the same `rag.json`.

```bash
# see the current rag.json in full
harness config show rag

# turn RAG on and point it at a folder of documents
harness config set rag Enabled true
harness config set rag DocumentsPath "C:\Users\alice\Documents\notes"

# pick a backend - exact enum spelling, case-sensitive
harness config set rag Backend Json
harness config set rag Backend Sqlite
harness config set rag Backend SqliteVec

# optionally move where that backend's files live (defaults under the user config dir already)
harness config set rag VectorStorePath "C:\Users\alice\.aiyara\rag\vectorstore"

# open rag.json directly in your default editor, for everything else (EmbeddingModel, ChunkSize, ...)
harness config edit rag
```

In-chat, without leaving the session, every one of those is `/config set rag <key> <value>` instead
of `harness config set rag <key> <value>` - e.g. `/config set rag Backend SqliteVec`.

## Restart required

`rag.json` is read once at startup - `Program.cs` builds the chosen `IVectorStore` and the
`search_documents` tool from whatever `Backend` says *then*, not on every turn (same read-once
rule as `models.json`'s `Provider` and `websearch.json`'s `BaseUrl`). Running `/config set rag
Backend SqliteVec` mid-session writes the file correctly, but has no visible effect until the
harness restarts.

## Switching backends doesn't migrate data

Each backend's data lives at its own path under `VectorStorePath` (`<collection>.json` for `Json`;
`vectors.db` for `Sqlite`/`SqliteVec`). Switching `Backend` doesn't convert or copy anything from
one to the other - the newly-selected backend starts with an empty collection and re-embeds every
document in `DocumentsPath` from scratch on the next startup. For a large corpus on a slow
embedding model, budget for that reindex time right after switching.

## Related

[[Index]] · [[RAG]] · [[Providers]] · [[Tools]]
