namespace Aiyara.Harness.Tools.Rag;

/// <summary>
/// The on-disk shape persisted at <c>&lt;VectorStorePath&gt;/index.json</c>.
/// </summary>
public sealed class RagIndexFile
{
    /// <summary>
    /// The embedding model/chunking settings the index was built with - if any of these differ
    /// from the current <c>rag.json</c>, the whole file is discarded and rebuilt from scratch
    /// rather than mixing incompatible embedding spaces or chunk boundaries.
    /// </summary>
    public string EmbeddingModel { get; init; } = "";

    public int ChunkSize { get; init; }

    public int ChunkOverlap { get; init; }

    public List<RagDocumentEntry> Documents { get; init; } = [];
}

/// <summary>
/// One indexed file: its chunks/embeddings, and the write time they were embedded at, so an
/// unchanged file can be skipped on the next startup instead of re-embedded.
/// </summary>
public sealed class RagDocumentEntry
{
    public string RelativePath { get; init; } = "";

    public DateTime LastWriteUtc { get; init; }

    public List<RagChunkEntry> Chunks { get; init; } = [];
}

public sealed class RagChunkEntry
{
    public string Text { get; init; } = "";

    public float[] Embedding { get; init; } = [];
}
