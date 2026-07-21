using Aiyara.Harness.Models.Config;

using OllamaSharp.Models.Chat;

namespace Aiyara.Harness.Tools;

/// <summary>
/// Lets the model create or update the workspace's <c>DESIGN.md</c> directly, instead of needing to
/// know it belongs at the workspace root and calling <c>write_file</c> with that exact path. Content
/// is written via <see cref="WriteFileTool.WriteFile"/>, so it goes through the same create/overwrite
/// confirmation prompt (<see cref="ActionConsent"/>, key "write_file") as any other file write - "allow
/// for this session" for one covers all of them.
/// </summary>
public class DesignDocumentTool : BaseTool
{
    public DesignDocumentTool()
    {
        Type = "function";
        Function = new Function
        {
            Name = "write_design_document",
            Description = $"Creates or overwrites {DesignDocument.FileName} at the workspace root - a project " +
                          "context file that gets loaded into the system prompt at the start of every future " +
                          "session, describing this project's architecture and the rationale behind non-obvious " +
                          "structural choices (e.g. trade-offs considered, alternatives rejected, and why).",
            Parameters = new Parameters
            {
                Properties = new Dictionary<string, Property>
                {
                    {
                        "content",
                        new Property { Type = "string", Description = $"Markdown content to write to {DesignDocument.FileName}." }
                    }
                },
                Required = new List<string> { "content" }
            }
        };
    }

    protected override object? Execute(IDictionary<string, object?>? args) =>
        WriteFileTool.WriteFile(
            DesignDocument.FileName,
            args?.TryGetValue("content", out var content) == true ? (string?)content : null);
}
