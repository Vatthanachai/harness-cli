using OllamaSharp;
using OllamaSharp.Models;
using OllamaSharp.Models.Chat;

namespace Aiyara.Harness.Tools.Providers.Ollama;

/// <summary>
/// Builds a fresh <see cref="OllamaChatEngine"/> against the same <see cref="IOllamaApiClient"/>
/// <c>Program.cs</c> already constructed for the primary conversation - no new connection per
/// engine. Uses the same <see cref="RequestOptions"/> as the primary chat (see <c>Program.cs</c>'s
/// comment on why <c>NumCtx</c> is 65536: a thinking model blows through a smaller context well
/// before it reaches a final answer) so a sub-agent on a reasoning model behaves the same way.
/// </summary>
public sealed class OllamaChatEngineFactory(IOllamaApiClient client) : IChatEngineFactory
{
    public IChatEngine Create(string model, string systemPrompt)
    {
        var chat = new Chat(client, systemPrompt)
        {
            Model = model,
            Think = ThinkValue.High,
            Options = new RequestOptions { Temperature = 0.7f, TopP = 0.9f, NumCtx = 65536 }
        };

        return new OllamaChatEngine(chat);
    }
}
