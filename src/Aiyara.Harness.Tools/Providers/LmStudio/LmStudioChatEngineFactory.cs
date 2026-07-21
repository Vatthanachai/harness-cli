namespace Aiyara.Harness.Tools.Providers.LmStudio;

/// <summary>
/// Builds a fresh <see cref="LmStudioChatEngine"/> against the same <see cref="HttpClient"/>
/// <c>Program.cs</c> already constructed for the primary conversation - no new connection per
/// engine.
/// </summary>
public sealed class LmStudioChatEngineFactory(HttpClient client) : IChatEngineFactory
{
    public IChatEngine Create(string model, string systemPrompt) =>
        new LmStudioChatEngine(client, model, systemPrompt);
}
