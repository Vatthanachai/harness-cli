using Aiyara.Harness.Models.Config;

using OllamaSharp.Models.Chat;

namespace Aiyara.Harness.Tools;

/// <summary>
/// Lets the model create (or replace) the visible task list for the current piece of multi-step
/// work - this harness's equivalent of Claude Code's task list. Takes a plain newline-separated
/// string rather than a JSON array, matching every other tool's parameter shapes in this project.
/// </summary>
public class WriteTasksTool : BaseTool
{
    private readonly TaskBoard _board;

    public WriteTasksTool(TaskBoard board)
    {
        _board = board;

        Type = "function";
        Function = new Function
        {
            Name = "write_tasks",
            Description = "Replaces the visible task list with the given steps, so the user can see progress as " +
                          "you go. Call this before starting multi-step work (roughly 3+ meaningfully distinct " +
                          "steps); skip it for a single quick action. All tasks start pending - use update_task to " +
                          "mark them in_progress/completed as you work through them.",
            Parameters = new Parameters
            {
                Properties = new Dictionary<string, Property>
                {
                    {
                        "tasks",
                        new Property
                        {
                            Type = "string",
                            Description = "The steps to track, one per line, in order, as short imperative descriptions."
                        }
                    }
                },
                Required = new List<string> { "tasks" }
            }
        };
    }

    protected override object? Execute(IDictionary<string, object?>? args)
    {
        var raw = args?.TryGetValue("tasks", out var t) == true ? t?.ToString() : null;
        var descriptions = (raw ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return _board.SetTasks(descriptions);
    }
}
