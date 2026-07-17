namespace Aiyara.Harness.Models.Config;

/// <summary>
/// A single MCP server the assistant can connect to.
/// </summary>
public sealed class McpServerOptions
{
    /// <summary>
    /// Name used to identify this server.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Command used to launch the MCP server process.
    /// </summary>
    public required string Command { get; init; }

    /// <summary>
    /// Arguments passed to <see cref="Command"/>.
    /// </summary>
    public string[] Args { get; init; } = [];

    /// <summary>
    /// Environment variables set on the MCP server process.
    /// </summary>
    public Dictionary<string, string> Env { get; init; } = [];
}

/// <summary>
/// MCP server settings, stored in <c>mcp.json</c>.
/// </summary>
public sealed class McpOptions
{
    /// <summary>
    /// MCP servers available to the assistant.
    /// </summary>
    public List<McpServerOptions> Servers { get; init; } = [];
}
