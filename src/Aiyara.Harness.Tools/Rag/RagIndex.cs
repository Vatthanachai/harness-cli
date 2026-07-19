namespace Aiyara.Harness.Tools.Rag;

/// <summary>
/// In-memory view over an already-built <see cref="RagIndexFile"/>'s documents, ranking chunks by
/// cosine similarity to a query embedding. Backs <see cref="SearchDocumentsTool"/>.
/// </summary>
public sealed class RagIndex(IReadOnlyList<RagDocumentEntry> documents)
{
    public int FileCount => documents.Count;

    public int ChunkCount => documents.Sum(d => d.Chunks.Count);

    public IReadOnlyList<(string Path, string Text, float Score)> Search(float[] query, int topK) =>
        documents
            .SelectMany(d => d.Chunks.Select(c => (Path: d.RelativePath, c.Text, c.Embedding)))
            .Select(e => (e.Path, e.Text, Score: CosineSimilarity(query, e.Embedding)))
            .OrderByDescending(e => e.Score)
            .Take(topK)
            .ToList();

    private static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length) return float.NegativeInfinity;

        double dot = 0, normA = 0, normB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        if (normA == 0 || normB == 0) return 0f;

        return (float)(dot / (Math.Sqrt(normA) * Math.Sqrt(normB)));
    }
}
