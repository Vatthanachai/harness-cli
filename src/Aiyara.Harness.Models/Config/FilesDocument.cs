namespace Aiyara.Harness.Models.Config;

/// <summary>
/// Optional per-project context file, mirroring how Claude Code reads <c>CLAUDE.md</c>. If a
/// <c>FILES.md</c> file exists at the workspace root, its contents are appended to the system
/// prompt so the model starts each session already knowing the project's file layout and
/// conventions, without needing to explore the tree itself. The model can read or update it like
/// any other file, via the normal file tools - there's nothing special about its content, only
/// about it being auto-loaded.
/// </summary>
public static class FilesDocument
{
    public const string FileName = "FILES.md";

    /// <summary>
    /// The absolute path <c>FILES.md</c> would have at <see cref="Workspace.Root"/>, whether or
    /// not it currently exists.
    /// </summary>
    public static string PathAtWorkspaceRoot => Path.Combine(Workspace.Root, FileName);

    /// <summary>
    /// Reads <c>FILES.md</c> from <see cref="Workspace.Root"/>, or returns null if it doesn't exist.
    /// </summary>
    public static string? Load()
    {
        var path = PathAtWorkspaceRoot;
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }
}
