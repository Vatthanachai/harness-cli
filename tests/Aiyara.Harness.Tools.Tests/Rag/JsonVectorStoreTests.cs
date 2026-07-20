using Aiyara.Harness.Models.Config;
using Aiyara.Harness.Tools.Rag;

namespace Aiyara.Harness.Tools.Tests.Rag;

public sealed class JsonVectorStoreTests : VectorStoreContractTests
{
    protected override Task<IVectorStore> CreateStoreAsync(RagOptions options) =>
        Task.FromResult<IVectorStore>(JsonVectorStore.FromOptions(options));

    [Fact]
    public async Task Normal_and_underscore_collection_names_work()
    {
        var store = (JsonVectorStore)await CreateStoreAsync(MakeOptions());

        await store.UpsertDocumentAsync("index", MakeDoc("a.txt"));
        await store.UpsertDocumentAsync("agent_2", MakeDoc("b.txt"));

        Assert.True(File.Exists(Path.Combine(StoreDir, "index.json")));
        Assert.True(File.Exists(Path.Combine(StoreDir, "agent_2.json")));
    }

    [Theory]
    [InlineData("../secrets")]
    [InlineData("..\\secrets")]
    [InlineData("../../etc/passwd")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("")]
    [InlineData("a.json;drop")]
    public async Task Malicious_collection_names_are_rejected_before_touching_disk(string collection)
    {
        var store = (JsonVectorStore)await CreateStoreAsync(MakeOptions());

        await Assert.ThrowsAsync<ArgumentException>(() => store.GetDocumentAsync(collection, "x.txt"));

        var parentDir = Directory.GetParent(StoreDir)!.FullName;
        Assert.False(File.Exists(Path.Combine(parentDir, "secrets.json")));
    }

    private static RagDocumentEntry MakeDoc(string path) => new()
    {
        RelativePath = path,
        LastWriteUtc = DateTime.UtcNow,
        Chunks = [new RagChunkEntry { Text = "hi", Embedding = [1f, 0f] }]
    };
}
