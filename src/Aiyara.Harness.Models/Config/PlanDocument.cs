namespace Aiyara.Harness.Models.Config;

/// <summary>
/// Optional per-project context file, the roadmap counterpart to <see cref="MemoryDocument"/>. Where
/// <see cref="MemoryDocument"/> (<c>MEMORY.md</c>) records facts and decisions from the past, a
/// <c>PLAN.md</c> file at the workspace root records what's being worked on next and in what order -
/// the current plan or roadmap for ongoing work. Its contents are appended to the system prompt so
/// the model starts every session already knowing what's planned, instead of losing that context
/// when the chat session ends. The model can read or update it like any other file, via the normal
/// file tools - there's nothing special about its content, only about it being auto-loaded.
/// </summary>
public static class PlanDocument
{
    public const string FileName = "PLAN.md";

    /// <summary>
    /// The absolute path <c>PLAN.md</c> would have at <see cref="Workspace.Root"/>, whether or not it
    /// currently exists.
    /// </summary>
    public static string PathAtWorkspaceRoot => Path.Combine(Workspace.Root, FileName);

    /// <summary>
    /// Reads <c>PLAN.md</c> from <see cref="Workspace.Root"/>, or returns null if it doesn't exist.
    /// </summary>
    public static string? Load()
    {
        var path = PathAtWorkspaceRoot;
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }
}
