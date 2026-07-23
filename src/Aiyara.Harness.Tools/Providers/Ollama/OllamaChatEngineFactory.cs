using Aiyara.Harness.Models.Config;

using OllamaSharp;
using OllamaSharp.Models;
using OllamaSharp.Models.Chat;

namespace Aiyara.Harness.Tools.Providers.Ollama;

/// <summary>
/// Builds a fresh <see cref="OllamaChatEngine"/> against the same <see cref="IOllamaApiClient"/>
/// <c>Program.cs</c> already constructed for the primary conversation - no new connection per
/// engine. Uses the same <see cref="RequestOptions"/> as the primary chat (see <c>Program.cs</c>'s
/// comment on why <c>NumCtx</c> is 32768: enough for a thinking model to reach a final answer
/// without the KV cache costing more VRAM than this machine can spare) so a sub-agent on a
/// reasoning model behaves the same way.
/// </summary>
public sealed class OllamaChatEngineFactory(IOllamaApiClient client) : IChatEngineFactory
{
    public async Task<IChatEngine> CreateAsync(string model, string systemPrompt, CancellationToken ct = default)
    {
        // Reloaded fresh per sub-agent (rather than captured once at Program.cs startup) so a
        // "/config set models EnableThinking false" between dispatches applies to the next one,
        // same reasoning as ChatSession.FlushThinking rereading logging.json each flush.
        var enableThinking = UserConfigStore.Load("models.json", new ModelsOptions()).EnableThinking;
        var supportsThinking = enableThinking && await new OllamaModelCatalog(client).SupportsThinkingAsync(model, ct);

        var chat = new Chat(client, systemPrompt)
        {
            Model = model,
            Think = supportsThinking ? ThinkValue.High : null,
            Options = new RequestOptions { Temperature = 0.7f, TopP = 0.9f, NumCtx = 32768 }
        };

        return new OllamaChatEngine(chat);
    }
}
