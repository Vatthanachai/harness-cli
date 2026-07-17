namespace Aiyara.Harness.Models.Config;

public enum WorkItemStatus { Pending, InProgress, Completed }

public sealed class WorkItem
{
    public required string Description { get; init; }
    public WorkItemStatus Status { get; set; } = WorkItemStatus.Pending;
}

/// <summary>
/// In-memory task list the model can create and update to make multi-step work visible to the
/// user as it happens - this harness's equivalent of Claude Code's task list. Lives only for the
/// current run; nothing here is persisted to disk.
/// </summary>
public sealed class TaskBoard
{
    private readonly List<WorkItem> _items = [];

    public IReadOnlyList<WorkItem> Items => _items;

    /// <summary>
    /// Replaces the whole list with <paramref name="descriptions"/>, all starting as pending, and
    /// returns the rendered list.
    /// </summary>
    public string SetTasks(IEnumerable<string> descriptions)
    {
        _items.Clear();
        _items.AddRange(descriptions
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Select(d => new WorkItem { Description = d.Trim() }));

        return Render();
    }

    /// <summary>
    /// Marks the task at <paramref name="index"/> (0-based) with <paramref name="status"/> and
    /// returns the rendered list.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public string UpdateStatus(int index, WorkItemStatus status)
    {
        if (index < 0 || index >= _items.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index), $"No task at index {index}; there are {_items.Count} task(s). Call write_tasks first.");
        }

        _items[index].Status = status;
        return Render();
    }

    public string Render()
    {
        if (_items.Count == 0) return "(no tasks)";

        return string.Join('\n', _items.Select((item, i) =>
        {
            var box = item.Status switch
            {
                WorkItemStatus.Completed => "[x]",
                WorkItemStatus.InProgress => "[~]",
                _ => "[ ]"
            };
            return $"{box} {i}. {item.Description}";
        }));
    }
}
