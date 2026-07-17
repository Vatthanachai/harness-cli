using Aiyara.Harness.Models.Config;

using OllamaSharp.Models.Chat;

namespace Aiyara.Harness.Tools;

/// <summary>
/// Lets the model create or update the workspace's <c>AIYARA.md</c> directly, instead of needing to
/// know it belongs at the workspace root and calling <c>write_file</c> with that exact path. Content
/// is written via <see cref="WriteFileTool.WriteFile"/>, so it goes through the same create/overwrite
/// confirmation prompt (<see cref="ActionConsent"/>, key "write_file") as any other file write - "allow
/// for this session" for one covers all of them.
/// </summary>
public class AiyaraDocumentTool : BaseTool
{
    public AiyaraDocumentTool()
    {
        Type = "function";
        Function = new Function
        {
            Name = "write_aiyara_document",
            Description = $"Creates or overwrites {AiyaraDocument.FileName} at the workspace root - this " +
                          "project's main context file (this harness's equivalent of Claude Code's CLAUDE.md), " +
                          "loaded into the system prompt at the start of every future session. Use it for a " +
                          "general overview: what the project is, its structure, conventions and anything else " +
                          "worth knowing up front - as opposed to FILES.md/TOOLS.md/COMMANDS.md/MEMORY.md, which " +
                          "cover their own narrower topics.",
            Parameters = new Parameters
            {
                Properties = new Dictionary<string, Property>
                {
                    {
                        "content",
                        new Property { Type = "string", Description = $"Markdown content to write to {AiyaraDocument.FileName}." }
                    }
                },
                Required = new List<string> { "content" }
            }
        };
    }

    protected override object? Execute(IDictionary<string, object?>? args) =>
        WriteFileTool.WriteFile(
            AiyaraDocument.FileName,
            args?.TryGetValue("content", out var content) == true ? (string?)content : null);
}
