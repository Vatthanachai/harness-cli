using Aiyara.Harness.Models.Config;
using Aiyara.Harness.Tools.Rag;

namespace Aiyara.Harness.Tools.Tests.Rag;

/// <summary>
/// Behavior every <see cref="IVectorStore"/> implementation must satisfy identically, regardless of
/// backing store - run against each concrete implementation by subclassing and implementing
/// <see cref="CreateStoreAsync"/>. Each test gets a fresh, isolated <see cref="StoreDir"/> via
/// <see cref="IAsyncLifetime"/>, so tests never see another test's data.
/// </summary>
public abstract class VectorStoreContractTests : IAsyncLifetime
{
    private static readonly DateTime LastWrite = new(2026, 7, 20, 12, 0, 0, DateTimeKind.Utc);

    protected string StoreDir { get; private set; } = "";

    protected abstract Task<IVectorStore> CreateStoreAsync(RagOptions options);

    protected RagOptions MakeOptions(string embeddingModel = "test-model", int chunkSize = 100, int chunkOverlap = 10) =>
        new()
        {
            Enabled = true,
            DocumentsPath = ".",
            VectorStorePath = StoreDir,
            EmbeddingModel = embeddingModel,
            ChunkSize = chunkSize,
            ChunkOverlap = chunkOverlap,
            TopK = 4
        };

    public Task InitializeAsync()
    {
        StoreDir = Path.Combine(AppContext.BaseDirectory, "vectorstore-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(StoreDir);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        try { Directory.Delete(StoreDir, recursive: true); }
        catch (IOException) { /* a pooled SQLite connection may still hold a handle - not worth failing the test over */ }
        return Task.CompletedTask;
    }

    [Fact]
    public async Task GetDocumentAsync_returns_null_for_missing_path()
    {
        var store = await CreateStoreAsync(MakeOptions());

        Assert.Null(await store.GetDocumentAsync("index", "missing.txt"));
    }

    [Fact]
    public async Task UpsertDocumentAsync_then_GetDocumentAsync_round_trips()
    {
        var store = await CreateStoreAsync(MakeOptions());
        var doc = new RagDocumentEntry
        {
            RelativePath = "a/b.txt",
            LastWriteUtc = LastWrite,
            Chunks =
            [
                new RagChunkEntry { Text = "hello world", Embedding = [1f, 0f, 0f] },
                new RagChunkEntry { Text = "goodbye world", Embedding = [0f, 1f, 0f] }
            ]
        };

        await store.UpsertDocumentAsync("index", doc);
        var fetched = await store.GetDocumentAsync("index", "a/b.txt");

        Assert.NotNull(fetched);
        Assert.Equal(LastWrite, fetched!.LastWriteUtc);
        Assert.Equal(2, fetched.Chunks.Count);
        Assert.Equal("hello world", fetched.Chunks[0].Text);
        Assert.Equal("goodbye world", fetched.Chunks[1].Text);
        Assert.Equal([1f, 0f, 0f], fetched.Chunks[0].Embedding);
    }

    [Fact]
    public async Task ListDocumentPathsAsync_lists_upserted_documents()
    {
        var store = await CreateStoreAsync(MakeOptions());
        await store.UpsertDocumentAsync("index", SingleChunkDoc("a.txt", [1f, 0f]));
        await store.UpsertDocumentAsync("index", SingleChunkDoc("b.txt", [0f, 1f]));

        var paths = await store.ListDocumentPathsAsync("index");

        Assert.Equal(["a.txt", "b.txt"], paths.OrderBy(p => p));
    }

    [Fact]
    public async Task UpsertDocumentAsync_on_existing_path_replaces_chunks_not_appends()
    {
        var store = await CreateStoreAsync(MakeOptions());
        await store.UpsertDocumentAsync("index", new RagDocumentEntry
        {
            RelativePath = "a.txt",
            LastWriteUtc = LastWrite,
            Chunks =
            [
                new RagChunkEntry { Text = "old", Embedding = [1f, 0f] },
                new RagChunkEntry { Text = "old2", Embedding = [0f, 1f] }
            ]
        });

        // Same embedding width as above - a collection's vectors are always the same dimension in
        // practice, since they all come from one embedding model. SqliteVecVectorStore's vec0
        // column has a genuinely fixed width per collection, so mixing dimensions here would test
        // something that can't happen in real usage rather than the replace behavior this checks.
        var updatedWrite = LastWrite.AddMinutes(5);
        await store.UpsertDocumentAsync("index", new RagDocumentEntry
        {
            RelativePath = "a.txt",
            LastWriteUtc = updatedWrite,
            Chunks = [new RagChunkEntry { Text = "new", Embedding = [0f, 1f] }]
        });

        var fetched = await store.GetDocumentAsync("index", "a.txt");
        Assert.NotNull(fetched);
        Assert.Equal(updatedWrite, fetched!.LastWriteUtc);
        Assert.Single(fetched.Chunks);
        Assert.Equal("new", fetched.Chunks[0].Text);
        Assert.Single(await store.ListDocumentPathsAsync("index"));
    }

    [Fact]
    public async Task RemoveDocumentAsync_removes_the_document_and_its_chunks()
    {
        var store = await CreateStoreAsync(MakeOptions());
        await store.UpsertDocumentAsync("index", SingleChunkDoc("a.txt", [1f, 0f]));

        await store.RemoveDocumentAsync("index", "a.txt");

        Assert.Null(await store.GetDocumentAsync("index", "a.txt"));
        Assert.Empty(await store.ListDocumentPathsAsync("index"));
        Assert.Empty(await store.SearchAsync("index", [1f, 0f], 4));
    }

    [Fact]
    public async Task RemoveDocumentAsync_on_missing_path_is_a_no_op()
    {
        var store = await CreateStoreAsync(MakeOptions());

        await store.RemoveDocumentAsync("index", "never-existed.txt");

        Assert.Empty(await store.ListDocumentPathsAsync("index"));
    }

    [Fact]
    public async Task SearchAsync_ranks_exact_match_first()
    {
        var store = await CreateStoreAsync(MakeOptions());
        await store.UpsertDocumentAsync("index", new RagDocumentEntry
        {
            RelativePath = "a.txt",
            LastWriteUtc = LastWrite,
            Chunks =
            [
                new RagChunkEntry { Text = "match", Embedding = [1f, 0f, 0f] },
                new RagChunkEntry { Text = "orthogonal", Embedding = [0f, 1f, 0f] },
                new RagChunkEntry { Text = "opposite", Embedding = [-1f, 0f, 0f] }
            ]
        });

        var results = await store.SearchAsync("index", [1f, 0f, 0f], 3);

        Assert.Equal(3, results.Count);
        Assert.Equal("match", results[0].Text);
        Assert.True(results[0].Score > 0.99f, $"expected top score > 0.99, got {results[0].Score}");
        Assert.True(results[0].Score > results[1].Score);
        Assert.True(results[1].Score > results[2].Score);
    }

    [Fact]
    public async Task SearchAsync_on_empty_collection_returns_empty()
    {
        var store = await CreateStoreAsync(MakeOptions());

        Assert.Empty(await store.SearchAsync("index", [1f, 0f], 4));
    }

    [Fact]
    public async Task Collection_wiped_when_embedding_config_changes()
    {
        var store = await CreateStoreAsync(MakeOptions(chunkSize: 100));
        await store.UpsertDocumentAsync("index", SingleChunkDoc("a.txt", [1f, 0f]));

        var storeWithNewConfig = await CreateStoreAsync(MakeOptions(chunkSize: 999));

        Assert.Null(await storeWithNewConfig.GetDocumentAsync("index", "a.txt"));
        Assert.Empty(await storeWithNewConfig.ListDocumentPathsAsync("index"));
    }

    private static RagDocumentEntry SingleChunkDoc(string path, float[] embedding) => new()
    {
        RelativePath = path,
        LastWriteUtc = LastWrite,
        Chunks = [new RagChunkEntry { Text = $"chunk for {path}", Embedding = embedding }]
    };
}
