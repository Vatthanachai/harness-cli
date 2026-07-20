using Aiyara.Harness.Models.Config;
using Aiyara.Harness.Tools.Rag;

namespace Aiyara.Harness.Tools.Tests.Rag;

public sealed class SqliteVecVectorStoreTests : VectorStoreContractTests
{
    protected override async Task<IVectorStore> CreateStoreAsync(RagOptions options) =>
        await SqliteVecVectorStore.FromOptionsAsync(options);
}
