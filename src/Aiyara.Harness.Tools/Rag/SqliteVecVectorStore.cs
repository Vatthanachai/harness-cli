using System.Text.Json;

using Aiyara.Harness.Models.Config;

using Microsoft.Data.Sqlite;

namespace Aiyara.Harness.Tools.Rag;

/// <summary>
/// <see cref="IVectorStore"/> backed by SQLite plus the <c>sqlite-vec</c> extension (vec0 virtual
/// tables) - same relational <c>documents</c>/<c>chunks</c>/<c>collections</c> tables as
/// <see cref="SqliteVectorStore"/>, but embeddings live in a per-collection vec0 table
/// (<c>vec_&lt;collection&gt;</c>) so nearest-neighbor search runs as a native, SIMD-accelerated
/// <c>MATCH</c> query instead of a C# loop over every row. Still exact KNN, not approximate (vec0
/// has no ANN index) - this scales in row count the same way <see cref="SqliteVectorStore"/> does,
/// just with a much smaller constant factor on search.
/// </summary>
/// <remarks>
/// Construction throws if the native <c>sqlite-vec</c> extension can't load on this platform - a
/// caller that wants to degrade gracefully should catch that and fall back to
/// <see cref="SqliteVectorStore"/> instead; this class intentionally doesn't know about that
/// fallback itself.
/// </remarks>
public sealed class SqliteVecVectorStore : IVectorStore
{
    private readonly string _connectionString;
    private readonly string _embeddingModel;
    private readonly int _chunkSize;
    private readonly int _chunkOverlap;
    private readonly SemaphoreSlim _validationLock = new(1, 1);
    private readonly HashSet<string> _validatedCollections = new();
    private readonly HashSet<string> _ensuredVecTables = new();

    private SqliteVecVectorStore(string dbPath, string embeddingModel, int chunkSize, int chunkOverlap)
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
        _embeddingModel = embeddingModel;
        _chunkSize = chunkSize;
        _chunkOverlap = chunkOverlap;
    }

    /// <summary>
    /// Builds the store for <paramref name="options"/>'s <c>VectorStorePath</c> (defaulting to
    /// <c>.aiyara/rag</c>), creating the directory and the database schema if either don't exist
    /// yet. Throws if the <c>sqlite-vec</c> native extension can't be loaded on this machine.
    /// </summary>
    public static async Task<SqliteVecVectorStore> FromOptionsAsync(RagOptions options, CancellationToken ct = default)
    {
        var storeDir = Workspace.ResolvePath(
            string.IsNullOrWhiteSpace(options.VectorStorePath) ? ".aiyara/rag" : options.VectorStorePath);
        Directory.CreateDirectory(storeDir);
        var dbPath = Path.Combine(storeDir, "vectors-vec.db");

        var store = new SqliteVecVectorStore(dbPath, options.EmbeddingModel, options.ChunkSize, options.ChunkOverlap);

        await using var connection = await store.OpenConnectionAsync(ct);
        await store.InitializeSchemaAsync(connection, ct);
        return store;
    }

    public async Task<RagDocumentEntry?> GetDocumentAsync(string collection, string relativePath, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct);
        await EnsureCollectionValidAsync(collection, connection, ct);

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

        var chunkRows = new List<(long ChunkId, string Text)>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT chunk_id, text FROM chunks
                WHERE collection = $collection AND relative_path = $path
                ORDER BY chunk_index;
                """;
            command.Parameters.AddWithValue("$collection", collection);
            command.Parameters.AddWithValue("$path", relativePath);

            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) chunkRows.Add((reader.GetInt64(0), reader.GetString(1)));
        }

        var embeddings = await GetEmbeddingsAsync(connection, collection, chunkRows.Select(c => c.ChunkId).ToList(), ct);
        var chunks = chunkRows.Select(c => new RagChunkEntry { Text = c.Text, Embedding = embeddings[c.ChunkId] }).ToList();

        return new RagDocumentEntry { RelativePath = relativePath, LastWriteUtc = lastWriteUtc, Chunks = chunks };
    }

    public async Task UpsertDocumentAsync(string collection, RagDocumentEntry document, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct);
        await EnsureCollectionValidAsync(collection, connection, ct);

        if (document.Chunks.Count > 0)
            await EnsureVecTableAsync(collection, document.Chunks[0].Embedding.Length, connection, ct);

        await using var transaction = connection.BeginTransaction();

        var oldChunkIds = await GetChunkIdsAsync(connection, transaction, collection, document.RelativePath, ct);
        await DeleteChunksAsync(connection, transaction, collection, document.RelativePath, oldChunkIds, ct);

        await using (var upsertDocument = connection.CreateCommand())
        {
            upsertDocument.Transaction = transaction;
            upsertDocument.CommandText = """
                INSERT INTO documents (collection, relative_path, last_write_utc)
                VALUES ($collection, $path, $lastWriteUtc)
                ON CONFLICT(collection, relative_path) DO UPDATE SET last_write_utc = excluded.last_write_utc;
                """;
            upsertDocument.Parameters.AddWithValue("$collection", collection);
            upsertDocument.Parameters.AddWithValue("$path", document.RelativePath);
            upsertDocument.Parameters.AddWithValue("$lastWriteUtc", document.LastWriteUtc.ToString("O"));
            await upsertDocument.ExecuteNonQueryAsync(ct);
        }

        for (var i = 0; i < document.Chunks.Count; i++)
        {
            long chunkId;
            await using (var insertChunk = connection.CreateCommand())
            {
                insertChunk.Transaction = transaction;
                insertChunk.CommandText = """
                    INSERT INTO chunks (collection, relative_path, chunk_index, text)
                    VALUES ($collection, $path, $index, $text)
                    RETURNING chunk_id;
                    """;
                insertChunk.Parameters.AddWithValue("$collection", collection);
                insertChunk.Parameters.AddWithValue("$path", document.RelativePath);
                insertChunk.Parameters.AddWithValue("$index", i);
                insertChunk.Parameters.AddWithValue("$text", document.Chunks[i].Text);
                chunkId = (long)(await insertChunk.ExecuteScalarAsync(ct))!;
            }

            await using var insertVector = connection.CreateCommand();
            insertVector.Transaction = transaction;
            insertVector.CommandText = $"INSERT INTO {VecTableName(collection)} (rowid, embedding) VALUES ($rowid, $embedding);";
            insertVector.Parameters.AddWithValue("$rowid", chunkId);
            insertVector.Parameters.AddWithValue("$embedding", JsonSerializer.Serialize(document.Chunks[i].Embedding));
            await insertVector.ExecuteNonQueryAsync(ct);
        }

        await transaction.CommitAsync(ct);
    }

    public async Task RemoveDocumentAsync(string collection, string relativePath, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct);
        await EnsureCollectionValidAsync(collection, connection, ct);

        await using var transaction = connection.BeginTransaction();

        var chunkIds = await GetChunkIdsAsync(connection, transaction, collection, relativePath, ct);
        await DeleteChunksAsync(connection, transaction, collection, relativePath, chunkIds, ct);

        await using (var deleteDocument = connection.CreateCommand())
        {
            deleteDocument.Transaction = transaction;
            deleteDocument.CommandText = "DELETE FROM documents WHERE collection = $collection AND relative_path = $path;";
            deleteDocument.Parameters.AddWithValue("$collection", collection);
            deleteDocument.Parameters.AddWithValue("$path", relativePath);
            await deleteDocument.ExecuteNonQueryAsync(ct);
        }

        await transaction.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<string>> ListDocumentPathsAsync(string collection, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct);
        await EnsureCollectionValidAsync(collection, connection, ct);

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
        await using var connection = await OpenConnectionAsync(ct);
        await EnsureCollectionValidAsync(collection, connection, ct);

        // Nothing has ever been upserted into this collection, so its vec0 table doesn't exist yet
        // - querying it would just throw "no such table" for what is really just an empty result.
        if (!_ensuredVecTables.Contains(collection) && !await VecTableExistsAsync(connection, collection, ct))
            return [];

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT c.relative_path, c.text, v.distance
            FROM {VecTableName(collection)} v
            JOIN chunks c ON c.chunk_id = v.rowid
            WHERE v.embedding MATCH $query AND k = $k
            ORDER BY v.distance;
            """;
        command.Parameters.AddWithValue("$query", JsonSerializer.Serialize(query));
        command.Parameters.AddWithValue("$k", topK);

        var results = new List<(string Path, string Text, float Score)>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            // vec0's cosine "distance" is 1 - cosine_similarity, so flip it back to the similarity
            // score every other IVectorStore implementation returns (higher = more relevant).
            results.Add((reader.GetString(0), reader.GetString(1), 1f - (float)reader.GetDouble(2)));

        return results;
    }

    private async Task<Dictionary<long, float[]>> GetEmbeddingsAsync(
        SqliteConnection connection, string collection, IReadOnlyList<long> chunkIds, CancellationToken ct)
    {
        var result = new Dictionary<long, float[]>();
        if (chunkIds.Count == 0) return result;

        await using var command = connection.CreateCommand();
        var placeholders = string.Join(",", chunkIds.Select((_, i) => $"$id{i}"));
        command.CommandText = $"SELECT rowid, embedding FROM {VecTableName(collection)} WHERE rowid IN ({placeholders});";
        for (var i = 0; i < chunkIds.Count; i++)
            command.Parameters.AddWithValue($"$id{i}", chunkIds[i]);

        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            result[reader.GetInt64(0)] = ToVector((byte[])reader[1]);

        return result;
    }

    private static async Task<List<long>> GetChunkIdsAsync(
        SqliteConnection connection, SqliteTransaction transaction, string collection, string relativePath, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT chunk_id FROM chunks WHERE collection = $collection AND relative_path = $path;";
        command.Parameters.AddWithValue("$collection", collection);
        command.Parameters.AddWithValue("$path", relativePath);

        var ids = new List<long>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) ids.Add(reader.GetInt64(0));
        return ids;
    }

    private async Task DeleteChunksAsync(
        SqliteConnection connection, SqliteTransaction transaction, string collection, string relativePath,
        IReadOnlyList<long> chunkIds, CancellationToken ct)
    {
        if (chunkIds.Count > 0)
        {
            await using var deleteVectors = connection.CreateCommand();
            deleteVectors.Transaction = transaction;
            var placeholders = string.Join(",", chunkIds.Select((_, i) => $"$id{i}"));
            deleteVectors.CommandText = $"DELETE FROM {VecTableName(collection)} WHERE rowid IN ({placeholders});";
            for (var i = 0; i < chunkIds.Count; i++)
                deleteVectors.Parameters.AddWithValue($"$id{i}", chunkIds[i]);
            await deleteVectors.ExecuteNonQueryAsync(ct);
        }

        await using var deleteChunks = connection.CreateCommand();
        deleteChunks.Transaction = transaction;
        deleteChunks.CommandText = "DELETE FROM chunks WHERE collection = $collection AND relative_path = $path;";
        deleteChunks.Parameters.AddWithValue("$collection", collection);
        deleteChunks.Parameters.AddWithValue("$path", relativePath);
        await deleteChunks.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Creates <c>vec_&lt;collection&gt;</c> (<c>IF NOT EXISTS</c>, so this is cheap to call
    /// unconditionally) with a fixed <paramref name="dimension"/> - every embedding upserted into
    /// this collection has to match it, since vec0 columns have a fixed vector width. That's
    /// already guaranteed by <see cref="EnsureCollectionValidAsync"/> wiping the collection whenever
    /// <c>EmbeddingModel</c> changes, so a dimension mismatch here would mean a caller bug, not a
    /// config change - left to surface as a raw vec0 error rather than pre-checked.
    /// </summary>
    private async Task EnsureVecTableAsync(string collection, int dimension, SqliteConnection connection, CancellationToken ct)
    {
        if (_ensuredVecTables.Contains(collection)) return;

        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE VIRTUAL TABLE IF NOT EXISTS {VecTableName(collection)} USING vec0(embedding float[{dimension}] distance_metric=cosine);";
        await command.ExecuteNonQueryAsync(ct);

        _ensuredVecTables.Add(collection);
    }

    private static async Task<bool> VecTableExistsAsync(SqliteConnection connection, string collection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name;";
        command.Parameters.AddWithValue("$name", VecTableName(collection));
        return await command.ExecuteScalarAsync(ct) is not null;
    }

    /// <summary>
    /// Makes sure <paramref name="collection"/>'s stored config (embedding model/chunk size/overlap)
    /// matches what this store was constructed with, wiping its rows (including its vec0 table, since
    /// a new embedding model likely means a different dimension) if not - same reasoning as
    /// <see cref="SqliteVectorStore"/>'s discard-on-mismatch. Cached per collection so this only
    /// costs a query once per store instance, not once per call.
    /// </summary>
    private async Task EnsureCollectionValidAsync(string collection, SqliteConnection connection, CancellationToken ct)
    {
        if (_validatedCollections.Contains(collection)) return;

        await _validationLock.WaitAsync(ct);
        try
        {
            if (_validatedCollections.Contains(collection)) return;

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
                await using (var dropVecTable = connection.CreateCommand())
                {
                    dropVecTable.Transaction = transaction;
                    dropVecTable.CommandText = $"DROP TABLE IF EXISTS {VecTableName(collection)};";
                    await dropVecTable.ExecuteNonQueryAsync(ct);
                }

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

                _ensuredVecTables.Remove(collection);
            }

            await transaction.CommitAsync(ct);
            _validatedCollections.Add(collection);
        }
        finally
        {
            _validationLock.Release();
        }
    }

    private async Task InitializeSchemaAsync(SqliteConnection connection, CancellationToken ct)
    {
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
                chunk_id INTEGER PRIMARY KEY AUTOINCREMENT,
                collection TEXT NOT NULL,
                relative_path TEXT NOT NULL,
                chunk_index INTEGER NOT NULL,
                text TEXT NOT NULL,
                UNIQUE (collection, relative_path, chunk_index)
            );
            """;
        await command.ExecuteNonQueryAsync(ct);
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken ct)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        connection.LoadVector();

        // WAL lets concurrent readers (e.g. several agents searching) proceed alongside a writer
        // instead of blocking; busy_timeout retries a lock conflict instead of failing it outright.
        await using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode = WAL; PRAGMA busy_timeout = 5000;";
        await pragma.ExecuteNonQueryAsync(ct);

        return connection;
    }

    /// <summary>
    /// <paramref name="collection"/> is interpolated directly into DDL/table-name positions that
    /// SQLite can't parameterize - restricted to alphanumeric/underscore so that's safe.
    /// </summary>
    private static string VecTableName(string collection)
    {
        if (collection.Length == 0 || !collection.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
            throw new ArgumentException(
                $"Collection name '{collection}' must be alphanumeric/underscore only.", nameof(collection));

        return $"vec_{collection}";
    }

    private static float[] ToVector(byte[] bytes)
    {
        var vector = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, vector, 0, bytes.Length);
        return vector;
    }
}
