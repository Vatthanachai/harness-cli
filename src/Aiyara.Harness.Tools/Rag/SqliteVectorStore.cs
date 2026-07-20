using Aiyara.Harness.Models.Config;

using Microsoft.Data.Sqlite;

namespace Aiyara.Harness.Tools.Rag;

/// <summary>
/// <see cref="IVectorStore"/> backed by a single SQLite database file shared across collections -
/// unlike <see cref="JsonVectorStore"/>, an upsert/remove only touches the rows it changes rather
/// than rewriting everything, and WAL mode lets multiple readers/writers (e.g. several agents) use
/// the same store concurrently. Search still ranks by cosine similarity computed in C# over every
/// row in the collection - there's no ANN index here, so this scales in row count the same way
/// <see cref="JsonVectorStore"/> scales in file size, just without the whole-file rewrite cost on
/// every write. Swap in a store built on a real ANN index (e.g. sqlite-vec, or an external vector
/// db) behind the same interface if brute-force search ever stops being fast enough.
/// </summary>
public sealed class SqliteVectorStore : IVectorStore
{
    private readonly string _connectionString;
    private readonly string _embeddingModel;
    private readonly int _chunkSize;
    private readonly int _chunkOverlap;
    private readonly SemaphoreSlim _validationLock = new(1, 1);
    private readonly HashSet<string> _validatedCollections = new();

    private SqliteVectorStore(string dbPath, string embeddingModel, int chunkSize, int chunkOverlap)
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
        _embeddingModel = embeddingModel;
        _chunkSize = chunkSize;
        _chunkOverlap = chunkOverlap;
    }

    /// <summary>
    /// Builds the store for <paramref name="options"/>'s <c>VectorStorePath</c> (defaulting to
    /// <c>.aiyara/rag</c>), creating the directory and the database schema if either don't exist yet.
    /// </summary>
    public static async Task<SqliteVectorStore> FromOptionsAsync(RagOptions options, CancellationToken ct = default)
    {
        var storeDir = Workspace.ResolvePath(
            string.IsNullOrWhiteSpace(options.VectorStorePath) ? ".aiyara/rag" : options.VectorStorePath);
        Directory.CreateDirectory(storeDir);
        var dbPath = Path.Combine(storeDir, "vectors.db");

        var store = new SqliteVectorStore(dbPath, options.EmbeddingModel, options.ChunkSize, options.ChunkOverlap);
        await store.InitializeSchemaAsync(ct);
        return store;
    }

    public async Task<RagDocumentEntry?> GetDocumentAsync(string collection, string relativePath, CancellationToken ct = default)
    {
        await EnsureCollectionValidAsync(collection, ct);

        await using var connection = await OpenConnectionAsync(ct);

        DateTime lastWriteUtc;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT last_write_utc FROM documents WHERE collection = $collection AND relative_path = $path;";
            command.Parameters.AddWithValue("$collection", collection);
            command.Parameters.AddWithValue("$path", relativePath);

            var result = await command.ExecuteScalarAsync(ct);
            if (result is not string lastWriteText) return null;

            // DateTime.Parse defaults a "Z"-suffixed ISO string to Kind=Local unless told
            // otherwise - RoundtripKind is what actually preserves the Utc it was written with.
            lastWriteUtc = DateTime.Parse(
                lastWriteText, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind);
        }

        var chunks = new List<RagChunkEntry>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT text, embedding FROM chunks
                WHERE collection = $collection AND relative_path = $path
                ORDER BY chunk_index;
                """;
            command.Parameters.AddWithValue("$collection", collection);
            command.Parameters.AddWithValue("$path", relativePath);

            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                chunks.Add(new RagChunkEntry
                {
                    Text = reader.GetString(0),
                    Embedding = ToVector((byte[])reader[1])
                });
            }
        }

        return new RagDocumentEntry { RelativePath = relativePath, LastWriteUtc = lastWriteUtc, Chunks = chunks };
    }

    public async Task UpsertDocumentAsync(string collection, RagDocumentEntry document, CancellationToken ct = default)
    {
        await EnsureCollectionValidAsync(collection, ct);

        await using var connection = await OpenConnectionAsync(ct);
        await using var transaction = connection.BeginTransaction();

        await DeleteDocumentAsync(connection, transaction, collection, document.RelativePath, ct);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO documents (collection, relative_path, last_write_utc)
                VALUES ($collection, $path, $lastWriteUtc);
                """;
            command.Parameters.AddWithValue("$collection", collection);
            command.Parameters.AddWithValue("$path", document.RelativePath);
            command.Parameters.AddWithValue("$lastWriteUtc", document.LastWriteUtc.ToString("O"));
            await command.ExecuteNonQueryAsync(ct);
        }

        for (var i = 0; i < document.Chunks.Count; i++)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO chunks (collection, relative_path, chunk_index, text, embedding)
                VALUES ($collection, $path, $index, $text, $embedding);
                """;
            command.Parameters.AddWithValue("$collection", collection);
            command.Parameters.AddWithValue("$path", document.RelativePath);
            command.Parameters.AddWithValue("$index", i);
            command.Parameters.AddWithValue("$text", document.Chunks[i].Text);
            command.Parameters.AddWithValue("$embedding", ToBytes(document.Chunks[i].Embedding));
            await command.ExecuteNonQueryAsync(ct);
        }

        await transaction.CommitAsync(ct);
    }

    public async Task RemoveDocumentAsync(string collection, string relativePath, CancellationToken ct = default)
    {
        await EnsureCollectionValidAsync(collection, ct);

        await using var connection = await OpenConnectionAsync(ct);
        await using var transaction = connection.BeginTransaction();
        await DeleteDocumentAsync(connection, transaction, collection, relativePath, ct);
        await transaction.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<string>> ListDocumentPathsAsync(string collection, CancellationToken ct = default)
    {
        await EnsureCollectionValidAsync(collection, ct);

        await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT relative_path FROM documents WHERE collection = $collection;";
        command.Parameters.AddWithValue("$collection", collection);

        var paths = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) paths.Add(reader.GetString(0));
        return paths;
    }

    public async Task<IReadOnlyList<(string Path, string Text, float Score)>> SearchAsync(
        string collection, float[] query, int topK, CancellationToken ct = default)
    {
        await EnsureCollectionValidAsync(collection, ct);

        await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT relative_path, text, embedding FROM chunks WHERE collection = $collection;";
        command.Parameters.AddWithValue("$collection", collection);

        var scored = new List<(string Path, string Text, float Score)>();
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                var embedding = ToVector((byte[])reader[2]);
                scored.Add((reader.GetString(0), reader.GetString(1), CosineSimilarity(query, embedding)));
            }
        }

        return scored.OrderByDescending(e => e.Score).Take(topK).ToList();
    }

    private static async Task DeleteDocumentAsync(
        SqliteConnection connection, SqliteTransaction transaction, string collection, string relativePath, CancellationToken ct)
    {
        await using var deleteChunks = connection.CreateCommand();
        deleteChunks.Transaction = transaction;
        deleteChunks.CommandText = "DELETE FROM chunks WHERE collection = $collection AND relative_path = $path;";
        deleteChunks.Parameters.AddWithValue("$collection", collection);
        deleteChunks.Parameters.AddWithValue("$path", relativePath);
        await deleteChunks.ExecuteNonQueryAsync(ct);

        await using var deleteDocument = connection.CreateCommand();
        deleteDocument.Transaction = transaction;
        deleteDocument.CommandText = "DELETE FROM documents WHERE collection = $collection AND relative_path = $path;";
        deleteDocument.Parameters.AddWithValue("$collection", collection);
        deleteDocument.Parameters.AddWithValue("$path", relativePath);
        await deleteDocument.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Makes sure <paramref name="collection"/>'s stored config (embedding model/chunk size/overlap)
    /// matches what this store was constructed with, wiping its rows if not - same reasoning as
    /// <see cref="JsonVectorStore"/>'s discard-on-mismatch, just scoped to one collection's rows
    /// instead of a whole file. Cached per collection so this only costs a query once per store
    /// instance, not once per call.
    /// </summary>
    private async Task EnsureCollectionValidAsync(string collection, CancellationToken ct)
    {
        if (_validatedCollections.Contains(collection)) return;

        await _validationLock.WaitAsync(ct);
        try
        {
            if (_validatedCollections.Contains(collection)) return;

            await using var connection = await OpenConnectionAsync(ct);
            await using var transaction = connection.BeginTransaction();

            (string EmbeddingModel, int ChunkSize, int ChunkOverlap)? existing = null;
            await using (var select = connection.CreateCommand())
            {
                select.Transaction = transaction;
                select.CommandText = "SELECT embedding_model, chunk_size, chunk_overlap FROM collections WHERE collection = $collection;";
                select.Parameters.AddWithValue("$collection", collection);
                await using var reader = await select.ExecuteReaderAsync(ct);
                if (await reader.ReadAsync(ct))
                    existing = (reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2));
            }

            var matches = existing is { } config &&
                          config.EmbeddingModel == _embeddingModel &&
                          config.ChunkSize == _chunkSize &&
                          config.ChunkOverlap == _chunkOverlap;

            if (!matches)
            {
                await using (var deleteChunks = connection.CreateCommand())
                {
                    deleteChunks.Transaction = transaction;
                    deleteChunks.CommandText = "DELETE FROM chunks WHERE collection = $collection;";
                    deleteChunks.Parameters.AddWithValue("$collection", collection);
                    await deleteChunks.ExecuteNonQueryAsync(ct);
                }

                await using (var deleteDocuments = connection.CreateCommand())
                {
                    deleteDocuments.Transaction = transaction;
                    deleteDocuments.CommandText = "DELETE FROM documents WHERE collection = $collection;";
                    deleteDocuments.Parameters.AddWithValue("$collection", collection);
                    await deleteDocuments.ExecuteNonQueryAsync(ct);
                }

                await using var upsertConfig = connection.CreateCommand();
                upsertConfig.Transaction = transaction;
                upsertConfig.CommandText = """
                    INSERT INTO collections (collection, embedding_model, chunk_size, chunk_overlap)
                    VALUES ($collection, $embeddingModel, $chunkSize, $chunkOverlap)
                    ON CONFLICT(collection) DO UPDATE SET
                        embedding_model = excluded.embedding_model,
                        chunk_size = excluded.chunk_size,
                        chunk_overlap = excluded.chunk_overlap;
                    """;
                upsertConfig.Parameters.AddWithValue("$collection", collection);
                upsertConfig.Parameters.AddWithValue("$embeddingModel", _embeddingModel);
                upsertConfig.Parameters.AddWithValue("$chunkSize", _chunkSize);
                upsertConfig.Parameters.AddWithValue("$chunkOverlap", _chunkOverlap);
                await upsertConfig.ExecuteNonQueryAsync(ct);
            }

            await transaction.CommitAsync(ct);
            _validatedCollections.Add(collection);
        }
        finally
        {
            _validationLock.Release();
        }
    }

    private async Task InitializeSchemaAsync(CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS collections (
                collection TEXT PRIMARY KEY,
                embedding_model TEXT NOT NULL,
                chunk_size INTEGER NOT NULL,
                chunk_overlap INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS documents (
                collection TEXT NOT NULL,
                relative_path TEXT NOT NULL,
                last_write_utc TEXT NOT NULL,
                PRIMARY KEY (collection, relative_path)
            );
            CREATE TABLE IF NOT EXISTS chunks (
                collection TEXT NOT NULL,
                relative_path TEXT NOT NULL,
                chunk_index INTEGER NOT NULL,
                text TEXT NOT NULL,
                embedding BLOB NOT NULL,
                PRIMARY KEY (collection, relative_path, chunk_index)
            );
            """;
        await command.ExecuteNonQueryAsync(ct);
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken ct)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        // WAL lets concurrent readers (e.g. several agents searching) proceed alongside a writer
        // instead of blocking; busy_timeout retries a lock conflict instead of failing it outright.
        await using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode = WAL; PRAGMA busy_timeout = 5000;";
        await pragma.ExecuteNonQueryAsync(ct);

        return connection;
    }

    private static byte[] ToBytes(float[] vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static float[] ToVector(byte[] bytes)
    {
        var vector = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, vector, 0, bytes.Length);
        return vector;
    }

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
