using Aiyara.Harness.Models.Config;

using OllamaSharp.Models.Chat;

namespace Aiyara.Harness.Tools;

/// <summary>
/// Lets the model create or update the workspace's <c>FILES.md</c> directly, instead of needing to
/// know it belongs at the workspace root and calling <c>write_file</c> with that exact path. Content
/// is written via <see cref="WriteFileTool.WriteFile"/>, so it goes through the same create/overwrite
/// confirmation prompt (<see cref="ActionConsent"/>, key "write_file") as any other file write - "allow
/// for this session" for one covers the other.
/// </summary>
public class FilesDocumentTool : BaseTool
{
    public FilesDocumentTool()
    {
        Type = "function";
        Function = new Function
        {
            Name = "write_files_document",
            Description = $"Creates or overwrites {FilesDocument.FileName} at the workspace root - a project " +
                          "context file that gets loaded into the system prompt at the start of every future " +
                          "session, so the model already knows the file layout and conventions without exploring " +
                          "the tree again. Call list_files first to see what's actually in the workspace.",
            Parameters = new Parameters
            {
                Properties = new Dictionary<string, Property>
                {
                    {
                        "content",
                        new Property { Type = "string", Description = $"Markdown content to write to {FilesDocument.FileName}." }
                    }
                },
                Required = new List<string> { "content" }
            }
        };
    }

    protected override object? Execute(IDictionary<string, object?>? args) =>
        WriteFileTool.WriteFile(
            FilesDocument.FileName,
            args?.TryGetValue("content", out var content) == true ? (string?)content : null);
}
