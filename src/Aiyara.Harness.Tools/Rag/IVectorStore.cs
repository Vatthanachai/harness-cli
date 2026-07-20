namespace Aiyara.Harness.Tools.Rag;

/// <summary>
/// Persists and searches embedded document chunks for a named collection, on whichever backing
/// store is active - mirrors <see cref="Providers.IEmbeddingClient"/> so RAG code doesn't need to
/// know whether it's talking to a JSON file, SQLite, or an external vector database.
/// </summary>
public interface IVectorStore
{
    /// <summary>
    /// Returns the previously indexed entry for <paramref name="relativePath"/> in
    /// <paramref name="collection"/>, or <c>null</c> if it hasn't been indexed yet.
    /// </summary>
    Task<RagDocumentEntry?> GetDocumentAsync(string collection, string relativePath, CancellationToken ct = default);

    /// <summary>
    /// Inserts or replaces the indexed entry for <paramref name="document"/>'s path in
    /// <paramref name="collection"/>.
    /// </summary>
    Task UpsertDocumentAsync(string collection, RagDocumentEntry document, CancellationToken ct = default);

    /// <summary>
    /// Removes the indexed entry for <paramref name="relativePath"/> from <paramref name="collection"/>,
    /// if one exists - used to drop entries for source files that no longer exist.
    /// </summary>
    Task RemoveDocumentAsync(string collection, string relativePath, CancellationToken ct = default);

    /// <summary>
    /// Lists the relative paths of every document currently indexed in <paramref name="collection"/>,
    /// so a caller can tell which ones are no longer present at the source and should be removed.
    /// </summary>
    Task<IReadOnlyList<string>> ListDocumentPathsAsync(string collection, CancellationToken ct = default);

    /// <summary>
    /// Ranks chunks in <paramref name="collection"/> by similarity to <paramref name="query"/>,
    /// returning at most <paramref name="topK"/> results ordered by descending score.
    /// </summary>
    Task<IReadOnlyList<(string Path, string Text, float Score)>> SearchAsync(
        string collection, float[] query, int topK, CancellationToken ct = default);
}
