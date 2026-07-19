using OllamaSharp;
using OllamaSharp.Models.Chat;
using OllamaSharp.Tools;

namespace Aiyara.Harness.Tools.Providers.Ollama;

/// <summary>
/// Thin <see cref="IChatEngine"/> forwarding wrapper around a real <c>OllamaSharp.Chat</c> - all
/// the actual streaming/tool-calling behavior is OllamaSharp's, this just adapts its shape to the
/// interface the rest of the harness depends on so it can also run against LM Studio.
/// </summary>
public sealed class OllamaChatEngine(Chat inner) : IChatEngine
{
    public List<Message> Messages
    {
        get => inner.Messages;
        set => inner.Messages = value;
    }

    public string Model
    {
        get => inner.Model;
        set => inner.Model = value;
    }

    public event EventHandler<string>? OnThink
    {
        add => inner.OnThink += value;
        remove => inner.OnThink -= value;
    }

    public event EventHandler<Message.ToolCall>? OnToolCall
    {
        add => inner.OnToolCall += value;
        remove => inner.OnToolCall -= value;
    }

    public event EventHandler<ToolResult>? OnToolResult
    {
        add => inner.OnToolResult += value;
        remove => inner.OnToolResult -= value;
    }

    public IAsyncEnumerable<string> SendAsync(string message, IEnumerable<object> tools, CancellationToken ct = default) =>
        inner.SendAsync(message, tools, cancellationToken: ct);
}
