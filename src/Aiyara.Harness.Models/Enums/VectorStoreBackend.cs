namespace Aiyara.Harness.Models.Enums;

/// <summary>
/// Which <c>IVectorStore</c> implementation backs RAG indexing/search.
/// </summary>
public enum VectorStoreBackend
{
    Json,
    Sqlite,
    SqliteVec
}
