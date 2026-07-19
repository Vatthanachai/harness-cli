using System.Text.Json;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

using OllamaSharp.Models.Chat;

namespace Aiyara.Harness.Tools.Mcp;

/// <summary>
/// Exposes a single tool from a connected MCP server as an ordinary <see cref="BaseTool"/>, so it
/// flows through <c>ToolRegistry</c>, <c>/tools</c>, and either chat engine (Ollama or LM Studio)
/// exactly like a built-in tool - none of them know or care that the call is actually proxied to
/// an external MCP server process.
/// </summary>
public sealed class McpTool : BaseTool
{
    private readonly McpClient _client;
    private readonly string _remoteName;

    /// <summary>
    /// <paramref name="serverName"/> is prefixed onto the exposed tool name so tools from
    /// different MCP servers (or a built-in tool of the same name) never collide.
    /// </summary>
    public McpTool(string serverName, McpClientTool tool, McpClient client)
    {
        _client = client;
        _remoteName = tool.Name;

        Type = "function";
        Function = new Function
        {
            Name = $"{serverName}_{tool.Name}",
            Description = tool.Description ?? "",
            Parameters = ConvertSchema(tool.JsonSchema)
        };
    }

    protected override object? Execute(IDictionary<string, object?>? args)
    {
        var arguments = args?.ToDictionary(kv => kv.Key, kv => kv.Value) ?? new Dictionary<string, object?>();
        var result = _client.CallToolAsync(_remoteName, arguments).GetAwaiter().GetResult();

        var text = string.Join("\n", result.Content.OfType<TextContentBlock>().Select(t => t.Text));
        return result.IsError == true ? $"Error: {text}" : text;
    }

    /// <summary>
    /// Maps the top-level <c>properties</c>/<c>required</c> of an MCP tool's JSON Schema into
    /// OllamaSharp's flat <see cref="Parameters"/>/<see cref="Property"/> shape - the same
    /// Type/Description/Enum-only fidelity every hand-written tool in this repo already has.
    /// Deeply nested schemas (nested objects, arrays-of-objects, oneOf, ...) lose detail here;
    /// that's an existing limitation of OllamaSharp's tool-definition model, not something new.
    /// </summary>
    private static Parameters ConvertSchema(JsonElement schema)
    {
        var properties = new Dictionary<string, Property>();

        if (schema.TryGetProperty("properties", out var propsEl) && propsEl.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in propsEl.EnumerateObject())
            {
                var type = prop.Value.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String
                    ? t.GetString()!
                    : "string";

                var description = prop.Value.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String
                    ? d.GetString()
                    : null;

                List<string>? enumValues = null;
                if (prop.Value.TryGetProperty("enum", out var e) && e.ValueKind == JsonValueKind.Array)
                    enumValues = e.EnumerateArray().Select(v => v.ToString()).ToList();

                properties[prop.Name] = new Property { Type = type, Description = description, Enum = enumValues };
            }
        }

        var required = new List<string>();
        if (schema.TryGetProperty("required", out var reqEl) && reqEl.ValueKind == JsonValueKind.Array)
            required = reqEl.EnumerateArray().Select(v => v.GetString()!).ToList();

        return new Parameters { Type = "object", Properties = properties, Required = required };
    }
}
