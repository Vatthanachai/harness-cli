using Aiyara.Harness.Tools.Providers;

using OllamaSharp.Models.Chat;

namespace Aiyara.Harness.Tools.Rag;

/// <summary>
/// Lets the model search the RAG index built from <c>rag.json</c>'s <c>DocumentsPath</c> - an
/// opt-in tool call, same as any other tool here, rather than context injected automatically into
/// every turn.
/// </summary>
public sealed class SearchDocumentsTool : BaseTool
{
    private readonly IVectorStore _store;
    private readonly string _collection;
    private readonly IEmbeddingClient _embeddings;
    private readonly string _model;
    private readonly int _topK;

    public SearchDocumentsTool(IVectorStore store, string collection, IEmbeddingClient embeddings, string model, int topK)
    {
        _store = store;
        _collection = collection;
        _embeddings = embeddings;
        _model = model;
        _topK = topK;

        Type = "function";
        Function = new Function
        {
            Name = "search_documents",
            Description = "Searches the project's indexed documents (rag.json's DocumentsPath) for passages " +
                          "relevant to a query, returning the most relevant chunks with their source file.",
            Parameters = new Parameters
            {
                Properties = new Dictionary<string, Property>
                {
                    { "query", new Property { Type = "string", Description = "What to search for." } }
                },
                Required = new List<string> { "query" }
            }
        };
    }

    protected override object? Execute(IDictionary<string, object?>? args)
    {
        var query = args?.TryGetValue("query", out var q) == true ? q?.ToString() : null;
        if (string.IsNullOrWhiteSpace(query))
            return "Error: 'query' is required.";

        var queryEmbedding = _embeddings.EmbedAsync([query], _model).GetAwaiter().GetResult()[0];
        var results = _store.SearchAsync(_collection, queryEmbedding, _topK).GetAwaiter().GetResult();

        if (results.Count == 0)
            return "No indexed documents matched.";

        return string.Join("\n\n---\n\n", results.Select(r => $"[{r.Path}]\n{r.Text}"));
    }
}
