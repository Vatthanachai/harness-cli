using Aiyara.Harness.Models.Config;

using OllamaSharp.Models.Chat;

namespace Aiyara.Harness.Tools;

/// <summary>
/// Lets the model create or update the workspace's <c>PLAN.md</c> directly, instead of needing to
/// know it belongs at the workspace root and calling <c>write_file</c> with that exact path. Content
/// is written via <see cref="WriteFileTool.WriteFile"/>, so it goes through the same create/overwrite
/// confirmation prompt (<see cref="ActionConsent"/>, key "write_file") as any other file write - "allow
/// for this session" for one covers all of them. Since this overwrites the whole file, use
/// <c>open_file</c> to read the current content first when adding to it rather than replacing it.
/// </summary>
public class PlanDocumentTool : BaseTool
{
    public PlanDocumentTool()
    {
        Type = "function";
        Function = new Function
        {
            Name = "write_plan_document",
            Description = $"Creates or overwrites {PlanDocument.FileName} at the workspace root - a project " +
                          "context file that gets loaded into the system prompt at the start of every future " +
                          "session, recording the current plan or roadmap for ongoing work: what's being built " +
                          "next and in what order. Read the file first with open_file if you want to add to it " +
                          "rather than replace it, since this overwrites the whole file.",
            Parameters = new Parameters
            {
                Properties = new Dictionary<string, Property>
                {
                    {
                        "content",
                        new Property { Type = "string", Description = $"Markdown content to write to {PlanDocument.FileName}." }
                    }
                },
                Required = new List<string> { "content" }
            }
        };
    }

    protected override object? Execute(IDictionary<string, object?>? args) =>
        WriteFileTool.WriteFile(
            PlanDocument.FileName,
            args?.TryGetValue("content", out var content) == true ? (string?)content : null);
}
