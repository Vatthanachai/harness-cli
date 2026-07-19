using OllamaSharp.Models.Chat;
using OllamaSharp.Tools;

namespace Aiyara.Harness.Tools.Providers;

/// <summary>
/// The subset of <c>OllamaSharp.Chat</c>'s surface that the harness actually depends on, factored
/// out so a chat session can run against either Ollama (<see cref="Ollama.OllamaChatEngine"/>,
/// wrapping the real <c>Chat</c>) or LM Studio (<see cref="LmStudio.LmStudioChatEngine"/>, a
/// hand-rolled OpenAI-compatible client) interchangeably.
/// </summary>
public interface IChatEngine
{
    /// <summary>
    /// The running conversation, including the system message. Reused as-is from OllamaSharp's
    /// own <see cref="Message"/> type since it's already a plain, freely-constructible POCO.
    /// </summary>
    List<Message> Messages { get; set; }

    /// <summary>
    /// The model used for the next <see cref="SendAsync"/> call.
    /// </summary>
    string Model { get; set; }

    /// <summary>
    /// Fires with each streamed reasoning/thinking token, if the model produces any.
    /// </summary>
    event EventHandler<string>? OnThink;

    /// <summary>
    /// Fires once per tool call the model asks for, before it is invoked.
    /// </summary>
    event EventHandler<Message.ToolCall>? OnToolCall;

    /// <summary>
    /// Fires once per tool call after it has been invoked, with its result.
    /// </summary>
    event EventHandler<ToolResult>? OnToolResult;

    /// <summary>
    /// Sends <paramref name="message"/> as a user turn and streams back the final assistant
    /// answer's tokens, transparently running zero or more tool-call round-trips against
    /// <paramref name="tools"/> in between.
    /// </summary>
    IAsyncEnumerable<string> SendAsync(string message, IEnumerable<object> tools, CancellationToken ct = default);
}
