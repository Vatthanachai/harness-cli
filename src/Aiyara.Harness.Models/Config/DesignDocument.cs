namespace Aiyara.Harness.Models.Config;

/// <summary>
/// Optional per-project context file, the design/architecture counterpart to <see cref="FilesDocument"/>,
/// <see cref="ToolsDocument"/>, <see cref="CommandsDocument"/> and <see cref="MemoryDocument"/>. If a
/// <c>DESIGN.md</c> file exists at the workspace root, its contents are appended to the system prompt
/// so the model starts every session already knowing this project's architecture and the rationale
/// behind non-obvious structural choices - trade-offs considered, alternatives rejected, and why -
/// instead of re-deriving them from the code each time. The model can read or update it like any
/// other file, via the normal file tools - there's nothing special about its content, only about it
/// being auto-loaded.
/// </summary>
public static class DesignDocument
{
    public const string FileName = "DESIGN.md";

    /// <summary>
    /// The absolute path <c>DESIGN.md</c> would have at <see cref="Workspace.Root"/>, whether or not
    /// it currently exists.
    /// </summary>
    public static string PathAtWorkspaceRoot => Path.Combine(Workspace.Root, FileName);

    /// <summary>
    /// Reads <c>DESIGN.md</c> from <see cref="Workspace.Root"/>, or returns null if it doesn't exist.
    /// </summary>
    public static string? Load()
    {
        var path = PathAtWorkspaceRoot;
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }
}
