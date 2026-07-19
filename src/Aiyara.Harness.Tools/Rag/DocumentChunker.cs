namespace Aiyara.Harness.Tools.Rag;

/// <summary>
/// Splits a document's text into overlapping character-based chunks for embedding - no NLP
/// dependency, matching <see cref="Aiyara.Harness.Models.Config.RagOptions"/>'s
/// <c>ChunkSize</c>/<c>ChunkOverlap</c> being plain character counts.
/// </summary>
public static class DocumentChunker
{
    public static IEnumerable<string> Chunk(string text, int chunkSize, int chunkOverlap)
    {
        if (string.IsNullOrEmpty(text)) yield break;

        var step = Math.Max(1, chunkSize - chunkOverlap);

        for (var start = 0; start < text.Length; start += step)
        {
            var length = Math.Min(chunkSize, text.Length - start);
            yield return text.Substring(start, length);

            if (start + length >= text.Length) yield break;
        }
    }
}
