using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

using OllamaSharp.Models.Chat;

namespace Aiyara.Harness.Tools.Providers.LmStudio;

/// <summary>
/// JSON plumbing for talking to LM Studio's OpenAI-compatible <c>/v1/chat/completions</c>: building
/// the request body (message/tool-schema translation, including the image-message workaround
/// described on <see cref="LmStudioChatEngine"/>) and reading the streamed SSE response. Kept
/// separate from <see cref="LmStudioChatEngine"/> so that class stays about the tool-call loop,
/// not JSON mechanics.
/// </summary>
internal static class LmStudioJson
{
    /// <summary>
    /// Used to parse a completed tool call's <c>arguments</c> JSON string into plain CLR values
    /// (not boxed <see cref="JsonElement"/>), matching what <c>BaseTool.Execute</c> implementations
    /// already expect from Ollama's own (pre-parsed) tool-call arguments.
    /// </summary>
    private static readonly JsonSerializerOptions ArgumentOptions = new()
    {
        Converters = { new ObjectToInferredTypesConverter() }
    };

    public static IDictionary<string, object?> ParseArguments(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, object?>();

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(json, ArgumentOptions)
                   ?? new Dictionary<string, object?>();
        }
        catch (JsonException)
        {
            // Malformed/truncated arguments - the tool call still fires, just with no arguments,
            // rather than silently dropping the model's tool call attempt entirely.
            return new Dictionary<string, object?>();
        }
    }

    public static HttpRequestMessage BuildChatCompletionRequest(
        string model,
        IReadOnlyList<Message> messages,
        IReadOnlyList<Tool> tools,
        float temperature,
        float topP,
        IReadOnlyDictionary<Message, string> toolCallIds)
    {
        var root = new JsonObject
        {
            ["model"] = model,
            ["messages"] = BuildMessages(messages, toolCallIds),
            ["stream"] = true,
            ["temperature"] = temperature,
            ["top_p"] = topP
        };

        if (tools.Count > 0)
            root["tools"] = BuildTools(tools);

        return new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions")
        {
            Content = new StringContent(root.ToJsonString(), Encoding.UTF8, "application/json")
        };
    }

    private static JsonArray BuildMessages(IReadOnlyList<Message> messages, IReadOnlyDictionary<Message, string> toolCallIds)
    {
        var array = new JsonArray();

        foreach (var message in messages)
        {
            var role = message.Role?.ToString() ?? "user";

            if (role == "tool")
            {
                array.Add(new JsonObject
                {
                    ["role"] = "tool",
                    ["content"] = message.Content ?? "",
                    ["tool_call_id"] = toolCallIds.TryGetValue(message, out var id) ? id : "",
                    ["name"] = message.ToolName
                });

                // LM Studio rejects a tool-role message that carries images (confirmed live), but
                // accepts a user-role message with multipart image_url content - so a tool result
                // that attached an image (see ChatSession.AttachPendingImages) rides along as a
                // synthetic follow-up user message instead.
                if (message.Images is { Length: > 0 })
                    array.Add(BuildImageMessage(message.Images, "(image attached)"));

                continue;
            }

            if (role == "assistant" && message.ToolCalls is { } calls && calls.Any())
            {
                array.Add(new JsonObject
                {
                    ["role"] = "assistant",
                    ["content"] = message.Content ?? "",
                    ["tool_calls"] = BuildToolCalls(calls)
                });
                continue;
            }

            if (message.Images is { Length: > 0 })
            {
                array.Add(BuildImageMessage(message.Images, message.Content));
                continue;
            }

            array.Add(new JsonObject { ["role"] = role, ["content"] = message.Content ?? "" });
        }

        return array;
    }

    private static JsonObject BuildImageMessage(string[] images, string? text)
    {
        var content = new JsonArray
        {
            new JsonObject { ["type"] = "text", ["text"] = string.IsNullOrEmpty(text) ? "(image attached)" : text }
        };

        foreach (var base64 in images)
        {
            content.Add(new JsonObject
            {
                ["type"] = "image_url",
                ["image_url"] = new JsonObject { ["url"] = $"data:{SniffImageMimeType(base64)};base64,{base64}" }
            });
        }

        return new JsonObject { ["role"] = "user", ["content"] = content };
    }

    /// <summary>
    /// Detects the image format from its raw byte signature rather than a file extension - by the
    /// time a tool result reaches here, only the base64 bytes are known (see <c>OpenImageTool</c>),
    /// not the originating file name.
    /// </summary>
    private static string SniffImageMimeType(string base64)
    {
        try
        {
            var prefixLength = Math.Min(base64.Length - base64.Length % 4, 16);
            if (prefixLength < 4) return "image/png";

            var bytes = Convert.FromBase64String(base64[..prefixLength]);

            if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
                return "image/png";
            if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
                return "image/jpeg";
            if (bytes.Length >= 4 && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x38)
                return "image/gif";
            if (bytes.Length >= 12 && bytes[0] == 'R' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == 'F'
                && bytes[8] == 'W' && bytes[9] == 'E' && bytes[10] == 'B' && bytes[11] == 'P')
                return "image/webp";
        }
        catch (FormatException)
        {
            // Not valid base64 (shouldn't happen - it came from Convert.ToBase64String) - fall
            // through to the default below.
        }

        return "image/png";
    }

    private static JsonArray BuildToolCalls(IEnumerable<Message.ToolCall> calls)
    {
        var array = new JsonArray();

        foreach (var call in calls)
        {
            array.Add(new JsonObject
            {
                ["id"] = call.Id,
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = call.Function?.Name,
                    ["arguments"] = JsonSerializer.Serialize(call.Function?.Arguments ?? new Dictionary<string, object?>())
                }
            });
        }

        return array;
    }

    private static JsonArray BuildTools(IReadOnlyList<Tool> tools)
    {
        var array = new JsonArray();

        foreach (var tool in tools)
        {
            var fn = tool.Function;
            var properties = new JsonObject();

            if (fn?.Parameters?.Properties is { } props)
            {
                foreach (var (name, prop) in props)
                {
                    var propObj = new JsonObject { ["type"] = prop.Type, ["description"] = prop.Description };

                    if (prop.Enum is { } enumValues && enumValues.Any())
                    {
                        var enumArray = new JsonArray();
                        foreach (var value in enumValues) enumArray.Add(JsonValue.Create(value));
                        propObj["enum"] = enumArray;
                    }

                    properties[name] = propObj;
                }
            }

            var requiredArray = new JsonArray();
            foreach (var required in fn?.Parameters?.Required ?? Enumerable.Empty<string>())
                requiredArray.Add(JsonValue.Create(required));

            array.Add(new JsonObject
            {
                ["type"] = tool.Type ?? "function",
                ["function"] = new JsonObject
                {
                    ["name"] = fn?.Name,
                    ["description"] = fn?.Description,
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = fn?.Parameters?.Type ?? "object",
                        ["properties"] = properties,
                        ["required"] = requiredArray
                    }
                }
            });
        }

        return array;
    }

    public static async IAsyncEnumerable<JsonElement> ReadSseDataFramesAsync(
        HttpResponseMessage response,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (string.IsNullOrEmpty(line) || !line.StartsWith("data: ", StringComparison.Ordinal))
                continue;

            var payload = line["data: ".Length..];
            if (payload == "[DONE]") yield break;

            using var doc = JsonDocument.Parse(payload);
            yield return doc.RootElement.Clone();
        }
    }

    /// <summary>
    /// Deserializes a JSON value as its natural CLR type (string/long/double/bool/null/
    /// List&lt;object?&gt;/Dictionary&lt;string,object?&gt;) instead of a boxed
    /// <see cref="JsonElement"/>, so tool-call arguments come out shaped the way
    /// <c>BaseTool.Execute</c> implementations already expect (e.g. <c>args["city"]?.ToString()</c>).
    /// </summary>
    private sealed class ObjectToInferredTypesConverter : JsonConverter<object?>
    {
        public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            ReadValue(ref reader, options);

        private static object? ReadValue(ref Utf8JsonReader reader, JsonSerializerOptions options) =>
            reader.TokenType switch
            {
                JsonTokenType.True => true,
                JsonTokenType.False => false,
                JsonTokenType.Null => null,
                JsonTokenType.Number => reader.TryGetInt64(out var l) ? l : reader.GetDouble(),
                JsonTokenType.String => reader.GetString(),
                JsonTokenType.StartArray => ReadArray(ref reader, options),
                JsonTokenType.StartObject => ReadObject(ref reader, options),
                _ => JsonDocument.ParseValue(ref reader).RootElement.Clone()
            };

        private static List<object?> ReadArray(ref Utf8JsonReader reader, JsonSerializerOptions options)
        {
            var list = new List<object?>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                list.Add(ReadValue(ref reader, options));
            return list;
        }

        private static Dictionary<string, object?> ReadObject(ref Utf8JsonReader reader, JsonSerializerOptions options)
        {
            var dict = new Dictionary<string, object?>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                var key = reader.GetString()!;
                reader.Read();
                dict[key] = ReadValue(ref reader, options);
            }
            return dict;
        }

        public override void Write(Utf8JsonWriter writer, object? value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, value, value?.GetType() ?? typeof(object));
    }
}
