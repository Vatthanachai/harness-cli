namespace Aiyara.Harness.Models.Config;

/// <summary>
/// Connection settings for the LM Studio server, stored in <c>lmstudio.json</c>.
/// </summary>
public sealed class LmStudioConnectionOptions
{
    /// <summary>
    /// Base URL of the LM Studio server, e.g. "http://localhost:1234".
    /// </summary>
    public string BaseUrl { get; init; } = "http://localhost:1234";

    /// <summary>
    /// Bearer token sent as the Authorization header on requests to the LM Studio server. Empty
    /// when the server does not require authentication.
    /// </summary>
    public string AccessToken { get; init; } = "";
}
