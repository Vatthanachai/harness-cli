using Aiyara.Harness.Models.Config;

using OllamaSharp.Models.Chat;

namespace Aiyara.Harness.Tools;

/// <summary>
/// Lets the model update one task's status on the task list created by <see cref="WriteTasksTool"/>.
/// </summary>
public class UpdateTaskTool : BaseTool
{
    private readonly TaskBoard _board;

    public UpdateTaskTool(TaskBoard board)
    {
        _board = board;

        Type = "function";
        Function = new Function
        {
            Name = "update_task",
            Description = "Marks one task from the current task list (see write_tasks) as pending, in_progress or " +
                          "completed, by its 0-based index. Mark a task in_progress right before starting it and " +
                          "completed right after finishing it - don't batch status updates until the end.",
            Parameters = new Parameters
            {
                Properties = new Dictionary<string, Property>
                {
                    {
                        "index",
                        new Property { Type = "number", Description = "0-based index of the task, as shown by write_tasks/update_task's output." }
                    },
                    {
                        "status",
                        new Property { Type = "string", Description = "One of: pending, in_progress, completed." }
                    }
                },
                Required = new List<string> { "index", "status" }
            }
        };
    }

    protected override object? Execute(IDictionary<string, object?>? args)
    {
        var index = args?.TryGetValue("index", out var i) == true && i is not null ? Convert.ToInt32(i) : -1;
        var statusRaw = args?.TryGetValue("status", out var s) == true ? s?.ToString() : null;

        var status = statusRaw?.Trim().ToLowerInvariant() switch
        {
            "in_progress" or "in-progress" or "inprogress" => WorkItemStatus.InProgress,
            "completed" or "complete" or "done" => WorkItemStatus.Completed,
            "pending" or "todo" or "not_started" => WorkItemStatus.Pending,
            _ => throw new ArgumentException($"Unknown status '{statusRaw}'. Use pending, in_progress, or completed.")
        };

        return _board.UpdateStatus(index, status);
    }
}
