using System.Text.Json;

using Aiyara.Harness.Models.Config;
using Aiyara.Harness.Tools.Providers;

namespace Aiyara.Harness.Tools.Rag;

/// <summary>
/// Builds (or incrementally refreshes) the RAG index for the configured <c>DocumentsPath</c>,
/// persisting it under <c>VectorStorePath</c>. A file whose content hasn't changed since it was
/// last indexed (exact <c>LastWriteUtc</c> match) is reused as-is, not re-embedded - the only
/// files that cost an embedding call are new or modified ones.
/// </summary>
public static class RagIndexBuilder
{
    // Skips pathologically large files rather than embedding gigabytes of, say, a checked-in
    // binary asset that happens to live under DocumentsPath.
    private const long MaxFileSizeBytes = 2 * 1024 * 1024;

    private static readonly HashSet<string> SkippedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", ".idea", ".aiyara", "bin", "obj", "node_modules", "dist", "build", "packages",
        "__pycache__", ".venv"
    };

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    public static async Task<RagIndex> BuildAsync(RagOptions options, IEmbeddingClient embeddings, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(options.DocumentsPath))
            throw new InvalidOperationException("rag.json's DocumentsPath is empty - set it before enabling RAG.");

        var documentsRoot = Workspace.ResolvePath(options.DocumentsPath);
        if (!Directory.Exists(documentsRoot))
            throw new DirectoryNotFoundException($"RAG DocumentsPath '{options.DocumentsPath}' does not exist.");

        var storeDir = Workspace.ResolvePath(
            string.IsNullOrWhiteSpace(options.VectorStorePath) ? ".aiyara/rag" : options.VectorStorePath);
        Directory.CreateDirectory(storeDir);
        var indexPath = Path.Combine(storeDir, "index.json");

        var existing = LoadExisting(indexPath, options);
        var existingByPath = existing.Documents.ToDictionary(d => d.RelativePath, StringComparer.OrdinalIgnoreCase);

        var updatedDocuments = new List<RagDocumentEntry>();

        foreach (var filePath in EnumerateDocumentFiles(documentsRoot))
        {
            ct.ThrowIfCancellationRequested();

            var relativePath = Path.GetRelativePath(documentsRoot, filePath).Replace('\\', '/');
            var lastWriteUtc = File.GetLastWriteTimeUtc(filePath);

            if (existingByPath.TryGetValue(relativePath, out var previous) && previous.LastWriteUtc == lastWriteUtc)
            {
                updatedDocuments.Add(previous);
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

            updatedDocuments.Add(new RagDocumentEntry
            {
                RelativePath = relativePath,
                LastWriteUtc = lastWriteUtc,
                Chunks = chunks.Zip(vectors, (chunkText, embedding) => new RagChunkEntry
                {
                    Text = chunkText,
                    Embedding = embedding
                }).ToList()
            });
        }

        var updatedFile = new RagIndexFile
        {
            EmbeddingModel = options.EmbeddingModel,
            ChunkSize = options.ChunkSize,
            ChunkOverlap = options.ChunkOverlap,
            Documents = updatedDocuments
        };

        await File.WriteAllTextAsync(indexPath, JsonSerializer.Serialize(updatedFile, SerializerOptions), ct);

        return new RagIndex(updatedDocuments);
    }

    private static RagIndexFile LoadExisting(string indexPath, RagOptions options)
    {
        if (!File.Exists(indexPath)) return new RagIndexFile();

        try
        {
            var loaded = JsonSerializer.Deserialize<RagIndexFile>(File.ReadAllText(indexPath));
            if (loaded is null) return new RagIndexFile();

            var matchesConfig = loaded.EmbeddingModel == options.EmbeddingModel &&
                                 loaded.ChunkSize == options.ChunkSize &&
                                 loaded.ChunkOverlap == options.ChunkOverlap;

            // A changed embedding model or chunk size makes the old vectors/boundaries
            // incompatible with new ones - safer to rebuild everything than silently mix them.
            return matchesConfig ? loaded : new RagIndexFile();
        }
        catch (JsonException)
        {
            return new RagIndexFile();
        }
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
