using Aiyara.Harness.Models.Config;
using Aiyara.Harness.Tools.Providers;

namespace Aiyara.Harness.Tools.Rag;

/// <summary>
/// Builds (or incrementally refreshes) the RAG index for the configured <c>DocumentsPath</c> into
/// an <see cref="IVectorStore"/>. A file whose content hasn't changed since it was last indexed
/// (exact <c>LastWriteUtc</c> match) is reused as-is, not re-embedded - the only files that cost an
/// embedding call are new or modified ones. Files that disappeared from <c>DocumentsPath</c> since
/// the last build are pruned from the store so they stop surfacing in search results.
/// </summary>
public static class RagIndexBuilder
{
    /// <summary>
    /// The single collection RAG currently indexes into - a placeholder for the day multiple
    /// collections (e.g. per agent) are needed, at which point this stops being a constant.
    /// </summary>
    public const string Collection = "index";

    // Skips pathologically large files rather than embedding gigabytes of, say, a checked-in
    // binary asset that happens to live under DocumentsPath.
    private const long MaxFileSizeBytes = 2 * 1024 * 1024;

    private static readonly HashSet<string> SkippedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", ".idea", ".aiyara", "bin", "obj", "node_modules", "dist", "build", "packages",
        "__pycache__", ".venv"
    };

    public static async Task<RagIndexStats> BuildAsync(
        RagOptions options, IEmbeddingClient embeddings, IVectorStore store, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(options.DocumentsPath))
            throw new InvalidOperationException("rag.json's DocumentsPath is empty - set it before enabling RAG.");

        var documentsRoot = Workspace.ResolvePath(options.DocumentsPath);
        if (!Directory.Exists(documentsRoot))
            throw new DirectoryNotFoundException($"RAG DocumentsPath '{options.DocumentsPath}' does not exist.");

        var currentPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fileCount = 0;
        var chunkCount = 0;

        foreach (var filePath in EnumerateDocumentFiles(documentsRoot))
        {
            ct.ThrowIfCancellationRequested();

            var relativePath = Path.GetRelativePath(documentsRoot, filePath).Replace('\\', '/');
            currentPaths.Add(relativePath);
            var lastWriteUtc = File.GetLastWriteTimeUtc(filePath);

            var previous = await store.GetDocumentAsync(Collection, relativePath, ct);
            if (previous is not null && previous.LastWriteUtc == lastWriteUtc)
            {
                fileCount++;
                chunkCount += previous.Chunks.Count;
                continue;
            }

            string text;
            try
            {
                text = await File.ReadAllTextAsync(filePath, ct);
            }
            catch (Exception)
            {
                continue; // binary/unreadable file - skip it, not fatal to the rest of the index
            }

            var chunks = DocumentChunker.Chunk(text, options.ChunkSize, options.ChunkOverlap).ToList();
            if (chunks.Count == 0) continue;

            var vectors = await embeddings.EmbedAsync(chunks, options.EmbeddingModel, ct);

            var document = new RagDocumentEntry
            {
                RelativePath = relativePath,
                LastWriteUtc = lastWriteUtc,
                Chunks = chunks.Zip(vectors, (chunkText, embedding) => new RagChunkEntry
                {
                    Text = chunkText,
                    Embedding = embedding
                }).ToList()
            };

            await store.UpsertDocumentAsync(Collection, document, ct);
            fileCount++;
            chunkCount += document.Chunks.Count;
        }

        var indexedPaths = await store.ListDocumentPathsAsync(Collection, ct);
        foreach (var stalePath in indexedPaths.Where(p => !currentPaths.Contains(p)))
            await store.RemoveDocumentAsync(Collection, stalePath, ct);

        return new RagIndexStats(fileCount, chunkCount);
    }

    private static IEnumerable<string> EnumerateDocumentFiles(string root)
    {
        foreach (var file in EnumerateFilesRecursive(root))
        {
            if (new FileInfo(file).Length <= MaxFileSizeBytes)
                yield return file;
        }
    }

    private static IEnumerable<string> EnumerateFilesRecursive(string directory)
    {
        var (files, subdirectories) = ListDirectory(directory);
        if (files is null) yield break;

        foreach (var file in files) yield return file;

        foreach (var subdirectory in subdirectories!)
            foreach (var file in EnumerateFilesRecursive(subdirectory))
                yield return file;
    }

    private static (List<string>? Files, List<string>? Subdirectories) ListDirectory(string directory)
    {
        try
        {
            var files = Directory.EnumerateFiles(directory).ToList();
            var subdirectories = Directory.EnumerateDirectories(directory)
                .Where(d => !SkippedDirectoryNames.Contains(Path.GetFileName(d)))
                .ToList();
            return (files, subdirectories);
        }
        catch (UnauthorizedAccessException)
        {
            return (null, null);
        }
    }
}

/// <summary>
/// Summary of a completed <see cref="RagIndexBuilder.BuildAsync"/> run, for status logging.
/// </summary>
public readonly record struct RagIndexStats(int FileCount, int ChunkCount);
