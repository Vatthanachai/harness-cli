using Aiyara.Harness.Models.Config;

using OllamaSharp.Models.Chat;

namespace Aiyara.Harness.Tools;

/// <summary>
/// Lets the model create or update the workspace's <c>AGENTS.md</c> directly, instead of needing to
/// know it belongs at the workspace root and calling <c>write_file</c> with that exact path. Content
/// is written via <see cref="WriteFileTool.WriteFile"/>, so it goes through the same create/overwrite
/// confirmation prompt (<see cref="ActionConsent"/>, key "write_file") as any other file write - "allow
/// for this session" for one covers all of them.
/// </summary>
public class AgentsDocumentTool : BaseTool
{
    public AgentsDocumentTool()
    {
        Type = "function";
        Function = new Function
        {
            Name = "write_agents_document",
            Description = $"Creates or overwrites {AgentsDocument.FileName} at the workspace root - the " +
                          "cross-tool convention other AI coding agents (e.g. Codex, Cursor) also read, loaded " +
                          "into the system prompt at the start of every future session the same way as " +
                          "AIYARA.md. Prefer write_aiyara_document unless this project specifically needs to " +
                          "stay compatible with other agent tools that look for AGENTS.md.",
            Parameters = new Parameters
            {
                Properties = new Dictionary<string, Property>
                {
                    {
                        "content",
                        new Property { Type = "string", Description = $"Markdown content to write to {AgentsDocument.FileName}." }
                    }
                },
                Required = new List<string> { "content" }
            }
        };
    }

    protected override object? Execute(IDictionary<string, object?>? args) =>
        WriteFileTool.WriteFile(
            AgentsDocument.FileName,
            args?.TryGetValue("content", out var content) == true ? (string?)content : null);
}
