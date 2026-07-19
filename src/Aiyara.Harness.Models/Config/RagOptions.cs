namespace Aiyara.Harness.Models.Config;

/// <summary>
/// Retrieval-augmented generation settings, stored in <c>rag.json</c>.
/// </summary>
public sealed class RagOptions
{
    /// <summary>
    /// Whether retrieval-augmented generation is active.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Folder containing the source documents to index.
    /// </summary>
    public string DocumentsPath { get; init; } = "";

    /// <summary>
    /// Folder where the vector store is persisted.
    /// </summary>
    public string VectorStorePath { get; init; } = "";

    /// <summary>
    /// Name of the embedding model (on whichever provider is active) used to index and query documents.
    /// </summary>
    public string EmbeddingModel { get; init; } = "nomic-embed-text";

    /// <summary>
    /// Maximum number of characters per document chunk.
    /// </summary>
    public int ChunkSize { get; init; } = 512;

    /// <summary>
    /// Number of characters shared between consecutive chunks.
    /// </summary>
    public int ChunkOverlap { get; init; } = 50;

    /// <summary>
    /// Number of chunks retrieved per query.
    /// </summary>
    public int TopK { get; init; } = 4;
}
