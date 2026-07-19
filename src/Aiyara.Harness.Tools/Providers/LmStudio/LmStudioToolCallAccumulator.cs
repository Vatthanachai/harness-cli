using System.Text;
using System.Text.Json;

using OllamaSharp.Models.Chat;

namespace Aiyara.Harness.Tools.Providers.LmStudio;

/// <summary>
/// Accumulates streamed <c>delta.tool_calls[]</c> fragments by their <c>index</c> into complete
/// <see cref="Message.ToolCall"/> instances. Confirmed live against LM Studio: the first chunk for
/// a given index carries <c>id</c>/<c>type</c>/<c>function.name</c> (with an empty
/// <c>function.arguments</c>), and every following chunk for that same index carries only a
/// fragment of <c>function.arguments</c> - the full arguments string is only valid once the
/// stream ends, so it's parsed in <see cref="Build"/>, not as each fragment arrives.
/// </summary>
internal sealed class LmStudioToolCallAccumulator
{
    private sealed class Builder
    {
        public string? Id;
        public string? Name;
        public readonly StringBuilder Arguments = new();
    }

    private readonly SortedDictionary<int, Builder> _byIndex = new();

    public bool HasAny => _byIndex.Count > 0;

    public void Apply(JsonElement toolCallDeltas)
    {
        foreach (var delta in toolCallDeltas.EnumerateArray())
        {
            var index = delta.TryGetProperty("index", out var indexEl) ? indexEl.GetInt32() : 0;
            if (!_byIndex.TryGetValue(index, out var builder))
                _byIndex[index] = builder = new Builder();

            if (delta.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                builder.Id = idEl.GetString();

            if (!delta.TryGetProperty("function", out var fn)) continue;

            if (fn.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
                builder.Name = nameEl.GetString();

            if (fn.TryGetProperty("arguments", out var argsEl) && argsEl.ValueKind == JsonValueKind.String)
                builder.Arguments.Append(argsEl.GetString());
        }
    }

    public List<Message.ToolCall> Build() =>
        _byIndex.Values.Select(builder => new Message.ToolCall
        {
            Id = builder.Id ?? Guid.NewGuid().ToString("N"),
            Function = new Message.Function
            {
                Name = builder.Name ?? "",
                Arguments = LmStudioJson.ParseArguments(builder.Arguments.ToString())
            }
        }).ToList();
}
