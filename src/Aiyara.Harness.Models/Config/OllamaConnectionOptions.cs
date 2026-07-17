namespace Aiyara.Harness.Models.Config;

/// <summary>
/// Connection settings for the Ollama server, stored in <c>ollama.json</c>.
/// </summary>
public sealed class OllamaConnectionOptions
{
    /// <summary>
    /// Base URL of the Ollama server, e.g. "http://localhost:11434".
    /// </summary>
    public string BaseUrl { get; init; } = "http://localhost:11434";

    /// <summary>
    /// Bearer token sent as the Authorization header on requests to the Ollama server. Empty when
    /// the server does not require authentication.
    /// </summary>
    public string AccessToken { get; init; } = "";
}
