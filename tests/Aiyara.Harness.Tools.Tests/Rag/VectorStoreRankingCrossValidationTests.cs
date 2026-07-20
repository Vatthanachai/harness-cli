using Aiyara.Harness.Models.Config;
using Aiyara.Harness.Tools.Rag;

namespace Aiyara.Harness.Tools.Tests.Rag;

/// <summary>
/// <see cref="SqliteVecVectorStore"/>'s native KNN (vec0, <c>distance_metric=cosine</c>) should rank
/// identically to <see cref="SqliteVectorStore"/>'s brute-force C# cosine loop - they're two
/// implementations of the same contract and disagreement would mean one of them is wrong.
/// </summary>
public sealed class VectorStoreRankingCrossValidationTests : IAsyncLifetime
{
    private string _storeDir = "";

    public Task InitializeAsync()
    {
        _storeDir = Path.Combine(AppContext.BaseDirectory, "vectorstore-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_storeDir);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        try { Directory.Delete(_storeDir, recursive: true); }
        catch (IOException) { /* a pooled SQLite connection may still hold a handle */ }
        return Task.CompletedTask;
    }

    [Fact]
    public async Task SqliteVec_KNN_ranking_matches_Sqlite_brute_force_cosine()
    {
        const int dim = 16;
        const int docCount = 30;
        var rng = new Random(42);
        var lastWrite = DateTime.UtcNow;

        var bruteForceOptions = MakeOptions("brute");
        var vecOptions = MakeOptions("vec");
        var bruteForce = await SqliteVectorStore.FromOptionsAsync(bruteForceOptions);
        var vec = await SqliteVecVectorStore.FromOptionsAsync(vecOptions);

        for (var i = 0; i < docCount; i++)
        {
            var doc = new RagDocumentEntry
            {
                RelativePath = $"doc{i}.txt",
                LastWriteUtc = lastWrite,
                Chunks = [new RagChunkEntry { Text = $"chunk {i}", Embedding = RandomEmbedding(rng, dim) }]
            };
            await bruteForce.UpsertDocumentAsync("cross", doc);
            await vec.UpsertDocumentAsync("cross", doc);
        }

        var query = RandomEmbedding(rng, dim);
        var bruteResults = await bruteForce.SearchAsync("cross", query, 5);
        var vecResults = await vec.SearchAsync("cross", query, 5);

        Assert.Equal(bruteResults.Select(r => r.Path), vecResults.Select(r => r.Path));
        foreach (var (brute, vecResult) in bruteResults.Zip(vecResults))
            Assert.True(
                Math.Abs(brute.Score - vecResult.Score) < 1e-3f,
                $"{brute.Path}: brute-force score {brute.Score} vs sqlite-vec score {vecResult.Score}");
    }

    private RagOptions MakeOptions(string embeddingModel) => new()
    {
        Enabled = true,
        DocumentsPath = ".",
        VectorStorePath = _storeDir,
        EmbeddingModel = embeddingModel,
        ChunkSize = 100,
        ChunkOverlap = 10,
        TopK = 5
    };

    private static float[] RandomEmbedding(Random rng, int dimension)
    {
        var vector = new float[dimension];
        for (var i = 0; i < dimension; i++) vector[i] = (float)(rng.NextDouble() * 2 - 1);
        return vector;
    }
}
