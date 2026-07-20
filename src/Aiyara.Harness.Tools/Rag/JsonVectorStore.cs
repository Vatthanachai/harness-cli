using System.Text.Json;

using Aiyara.Harness.Models.Config;

namespace Aiyara.Harness.Tools.Rag;

/// <summary>
/// <see cref="IVectorStore"/> backed by one JSON file per collection under a store directory - the
/// original (and still simplest) backing store: a collection is loaded into memory on first access
/// and rewritten to disk on every upsert/remove. Fine at the document counts RAG indexes at today;
/// a SQLite- or vector-db-backed <see cref="IVectorStore"/> is the natural next step once that
/// stops being true.
/// </summary>
public sealed class JsonVectorStore(string storeDir, string embeddingModel, int chunkSize, int chunkOverlap) : IVectorStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Dictionary<string, RagIndexFile> _cache = new();

    /// <summary>
    /// Builds the store for <paramref name="options"/>'s <c>VectorStorePath</c> (defaulting to
    /// <c>.aiyara/rag</c>), creating the directory if it doesn't exist yet.
    /// </summary>
    public static JsonVectorStore FromOptions(RagOptions options)
    {
        var resolvedStoreDir = Workspace.ResolvePath(
            string.IsNullOrWhiteSpace(options.VectorStorePath) ? ".aiyara/rag" : options.VectorStorePath);
        Directory.CreateDirectory(resolvedStoreDir);

        return new JsonVectorStore(resolvedStoreDir, options.EmbeddingModel, options.ChunkSize, options.ChunkOverlap);
    }

    public async Task<RagDocumentEntry?> GetDocumentAsync(string collection, string relativePath, CancellationToken ct = default)
    {
        var file = await LoadAsync(collection, ct);
        return file.Documents.FirstOrDefault(d => IsSamePath(d.RelativePath, relativePath));
    }

    public async Task UpsertDocumentAsync(string collection, RagDocumentEntry document, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var file = await LoadUnlockedAsync(collection, ct);
            file.Documents.RemoveAll(d => IsSamePath(d.RelativePath, document.RelativePath));
            file.Documents.Add(document);
            await SaveUnlockedAsync(collection, file, ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RemoveDocumentAsync(string collection, string relativePath, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var file = await LoadUnlockedAsync(collection, ct);
            if (file.Documents.RemoveAll(d => IsSamePath(d.RelativePath, relativePath)) > 0)
                await SaveUnlockedAsync(collection, file, ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<string>> ListDocumentPathsAsync(string collection, CancellationToken ct = default)
    {
        var file = await LoadAsync(collection, ct);
        return file.Documents.Select(d => d.RelativePath).ToList();
    }

    public async Task<IReadOnlyList<(string Path, string Text, float Score)>> SearchAsync(
        string collection, float[] query, int topK, CancellationToken ct = default)
    {
        var file = await LoadAsync(collection, ct);

        return file.Documents
            .SelectMany(d => d.Chunks.Select(c => (Path: d.RelativePath, c.Text, c.Embedding)))
            .Select(e => (e.Path, e.Text, Score: CosineSimilarity(query, e.Embedding)))
            .OrderByDescending(e => e.Score)
            .Take(topK)
            .ToList();
    }

    private async Task<RagIndexFile> LoadAsync(string collection, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            return await LoadUnlockedAsync(collection, ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<RagIndexFile> LoadUnlockedAsync(string collection, CancellationToken ct)
    {
        if (_cache.TryGetValue(collection, out var cached)) return cached;

        var file = await ReadFromDiskAsync(collection, ct);
        _cache[collection] = file;
        return file;
    }

    private async Task<RagIndexFile> ReadFromDiskAsync(string collection, CancellationToken ct)
    {
        var path = PathFor(collection);
        if (!File.Exists(path)) return NewFile();

        try
        {
            var loaded = JsonSerializer.Deserialize<RagIndexFile>(await File.ReadAllTextAsync(path, ct));
            if (loaded is null) return NewFile();

            // A changed embedding model or chunk size makes the old vectors/boundaries incompatible
            // with new ones - safer to discard them than silently mix embedding spaces.
            var matchesConfig = loaded.EmbeddingModel == embeddingModel &&
                                 loaded.ChunkSize == chunkSize &&
                                 loaded.ChunkOverlap == chunkOverlap;

            return matchesConfig ? loaded : NewFile();
        }
        catch (JsonException)
        {
            return NewFile();
        }
    }

    private async Task SaveUnlockedAsync(string collection, RagIndexFile file, CancellationToken ct)
    {
        _cache[collection] = file;
        await File.WriteAllTextAsync(PathFor(collection), JsonSerializer.Serialize(file, SerializerOptions), ct);
    }

    /// <summary>
    /// <paramref name="collection"/> is interpolated directly into a filename - restricted to
    /// alphanumeric/underscore so it can't be used to escape <c>storeDir</c> (e.g. <c>"../secrets"</c>)
    /// or collide with an unrelated file.
    /// </summary>
    private string PathFor(string collection)
    {
        if (collection.Length == 0 || !collection.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
            throw new ArgumentException(
                $"Collection name '{collection}' must be alphanumeric/underscore only.", nameof(collection));

        return Path.Combine(storeDir, $"{collection}.json");
    }

    private RagIndexFile NewFile() =>
        new() { EmbeddingModel = embeddingModel, ChunkSize = chunkSize, ChunkOverlap = chunkOverlap };

    private static bool IsSamePath(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

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
