using Aiyara.Harness.Models.Config;

using ModelContextProtocol.Client;

using Serilog;

namespace Aiyara.Harness.Tools.Mcp;

/// <summary>
/// Connects to every MCP server configured in <c>mcp.json</c> at startup and wraps each of their
/// tools as an <see cref="McpTool"/>. A server that fails to launch or complete the MCP handshake
/// is skipped (logged as a warning) rather than taking the whole harness down with it - the same
/// resilience policy already applied to a broken project doc or an invalid config file elsewhere
/// in this codebase.
/// </summary>
public static class McpToolLoader
{
    public static async Task<(List<object> Tools, List<IAsyncDisposable> Clients)> LoadAsync(IEnumerable<McpServerOptions> servers)
    {
        var tools = new List<object>();
        var clients = new List<IAsyncDisposable>();

        foreach (var server in servers)
        {
            try
            {
                var transport = new StdioClientTransport(new StdioClientTransportOptions
                {
                    Name = server.Name,
                    Command = server.Command,
                    Arguments = server.Args,
                    EnvironmentVariables = server.Env
                });

                var client = await McpClient.CreateAsync(transport);
                clients.Add(client);

                var serverTools = await client.ListToolsAsync();
                tools.AddRange(serverTools.Select(t => new McpTool(server.Name, t, client)));

                Log.Information("Connected to MCP server '{Server}': {Count} tool(s)", server.Name, serverTools.Count);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Couldn't connect to MCP server '{Server}' - skipping its tools", server.Name);
            }
        }

        return (tools, clients);
    }
}
