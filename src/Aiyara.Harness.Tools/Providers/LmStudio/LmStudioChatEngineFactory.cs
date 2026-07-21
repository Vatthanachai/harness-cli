using Aiyara.Harness.Models.Config;

namespace Aiyara.Harness.Tools.Providers.LmStudio;

/// <summary>
/// Builds a fresh <see cref="LmStudioChatEngine"/> against the same <see cref="HttpClient"/>
/// <c>Program.cs</c> already constructed for the primary conversation - no new connection per
/// engine.
/// </summary>
public sealed class LmStudioChatEngineFactory(HttpClient client) : IChatEngineFactory
{
    public Task<IChatEngine> CreateAsync(string model, string systemPrompt, CancellationToken ct = default)
    {
        // Reloaded fresh per sub-agent, same reasoning as OllamaChatEngineFactory.
        var enableThinking = UserConfigStore.Load("models.json", new ModelsOptions()).EnableThinking;
        return Task.FromResult<IChatEngine>(new LmStudioChatEngine(client, model, systemPrompt, enableThinking: enableThinking));
    }
}
