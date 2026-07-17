using Aiyara.Harness.Models.Config;

using OllamaSharp.Models.Chat;

namespace Aiyara.Harness.Tools;

/// <summary>
/// Lets the model create or update the workspace's <c>COMMANDS.md</c> directly, instead of needing
/// to know it belongs at the workspace root and calling <c>write_file</c> with that exact path.
/// Content is written via <see cref="WriteFileTool.WriteFile"/>, so it goes through the same
/// create/overwrite confirmation prompt (<see cref="ActionConsent"/>, key "write_file") as any other
/// file write - "allow for this session" for one covers all of them.
/// </summary>
public class CommandsDocumentTool : BaseTool
{
    public CommandsDocumentTool()
    {
        Type = "function";
        Function = new Function
        {
            Name = "write_commands_document",
            Description = $"Creates or overwrites {CommandsDocument.FileName} at the workspace root - a " +
                          "project context file that gets loaded into the system prompt at the start of every " +
                          "future session, listing this project's build/test/run commands (e.g. 'build: dotnet " +
                          "build aiyara-harness.slnx') so run_command doesn't need to rediscover them each time.",
            Parameters = new Parameters
            {
                Properties = new Dictionary<string, Property>
                {
                    {
                        "content",
                        new Property { Type = "string", Description = $"Markdown content to write to {CommandsDocument.FileName}." }
                    }
                },
                Required = new List<string> { "content" }
            }
        };
    }

    protected override object? Execute(IDictionary<string, object?>? args) =>
        WriteFileTool.WriteFile(
            CommandsDocument.FileName,
            args?.TryGetValue("content", out var content) == true ? (string?)content : null);
}
