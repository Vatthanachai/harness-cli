using System.Runtime.CompilerServices;
using System.Text.Json;

using OllamaSharp.Models.Chat;
using OllamaSharp.Tools;

namespace Aiyara.Harness.Tools.Providers.LmStudio;

/// <summary>
/// Hand-rolled <see cref="IChatEngine"/> for LM Studio's OpenAI-compatible
/// <c>/v1/chat/completions</c> endpoint (LM Studio has no equivalent of Ollama's native
/// <c>/api/chat</c>). Owns the full agentic tool-call loop itself - streaming content and
/// reasoning tokens, invoking tools as the model asks for them, and re-sending the growing
/// message history - so <c>ChatSession</c>'s <c>await foreach (var token in chat.SendAsync(...))</c>
/// keeps seeing only final assistant answer tokens, exactly like the real
/// <c>OllamaSharp.Chat.SendAsync</c> it stands in for.
/// </summary>
public sealed class LmStudioChatEngine : IChatEngine
{
    // Safety guard against a model stuck calling tools forever within a single turn - not a
    // verified parity number with OllamaSharp's own AllowRecursiveToolCalls, just a sane cap.
    private const int MaxToolIterationsPerTurn = 25;

    private readonly HttpClient _http;
    private readonly float _temperature;
    private readonly float _topP;
    private readonly bool _enableThinking;

    // Keyed by message reference rather than content: LM Studio's tool_call_id is expected on the
    // matching tool-result message, but OllamaSharp's Message type has no such field to store it
    // on directly.
    private readonly Dictionary<Message, string> _toolCallIdByMessage = new();

    public LmStudioChatEngine(
        HttpClient http, string model, string systemPrompt, float temperature = 0.7f, float topP = 0.9f, bool enableThinking = true)
    {
        _http = http;
        _temperature = temperature;
        _topP = topP;
        _enableThinking = enableThinking;
        Model = model;
        Messages = [new Message(ChatRole.System, systemPrompt)];
    }

    public List<Message> Messages { get; set; }

    public string Model { get; set; }

    public event EventHandler<string>? OnThink;
    public event EventHandler<Message.ToolCall>? OnToolCall;
    public event EventHandler<ToolResult>? OnToolResult;

    public async IAsyncEnumerable<string> SendAsync(
        string message,
        IEnumerable<object> tools,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var toolList = tools.Cast<Tool>().ToList();
        Messages.Add(new Message(ChatRole.User, message));

        for (var iteration = 0; ; iteration++)
        {
            if (iteration >= MaxToolIterationsPerTurn)
                throw new InvalidOperationException($"Exceeded {MaxToolIterationsPerTurn} tool-call round-trips in a single turn.");

            var assistantMessage = new Message(ChatRole.Assistant, "");
            var accumulator = new LmStudioToolCallAccumulator();
            string? finishReason = null;

            using var request = LmStudioJson.BuildChatCompletionRequest(Model, Messages, toolList, _temperature, _topP, _toolCallIdByMessage);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            await foreach (var frame in LmStudioJson.ReadSseDataFramesAsync(response, ct))
            {
                if (!frame.TryGetProperty("choices", out var choicesEl) || choicesEl.GetArrayLength() == 0)
                    continue;

                var choice = choicesEl[0];
                if (!choice.TryGetProperty("delta", out var delta))
                    continue;

                if (_enableThinking &&
                    delta.TryGetProperty("reasoning_content", out var reasoning) && reasoning.ValueKind == JsonValueKind.String)
                {
                    var thought = reasoning.GetString()!;
                    if (thought.Length > 0) OnThink?.Invoke(this, thought);
                }

                if (delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                {
                    var text = content.GetString()!;
                    if (text.Length > 0)
                    {
                        assistantMessage.Content += text;
                        yield return text;
                    }
                }

                if (delta.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.ValueKind == JsonValueKind.Array)
                    accumulator.Apply(toolCalls);

                if (choice.TryGetProperty("finish_reason", out var fr) && fr.ValueKind == JsonValueKind.String)
                    finishReason = fr.GetString();
            }

            if (!accumulator.HasAny || finishReason != "tool_calls")
            {
                Messages.Add(assistantMessage);
                yield break;
            }

            var completedCalls = accumulator.Build();
            assistantMessage.ToolCalls = completedCalls;
            Messages.Add(assistantMessage);

            foreach (var call in completedCalls)
            {
                OnToolCall?.Invoke(this, call);

                var tool = toolList.FirstOrDefault(t => t.Function?.Name == call.Function?.Name);
                object? result = tool is IInvokableTool invokable
                    ? invokable.InvokeMethod(call.Function?.Arguments)
                    : $"Error: tool '{call.Function?.Name}' is not enabled or does not exist.";

                OnToolResult?.Invoke(this, new ToolResult(tool!, call, result));

                var toolMessage = new Message(ChatRole.Tool, result?.ToString() ?? "") { ToolName = call.Function?.Name };
                if (!string.IsNullOrEmpty(call.Id))
                    _toolCallIdByMessage[toolMessage] = call.Id;

                Messages.Add(toolMessage);
            }

            // Loop back and re-send the full history so the model can continue past the tool result.
        }
    }
}
