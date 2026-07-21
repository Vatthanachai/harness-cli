namespace Aiyara.Harness.Models.Config;

/// <summary>
/// Optional per-project context file, the goals/requirements counterpart to <see cref="AiyaraDocument"/>.
/// Where <see cref="AiyaraDocument"/> (<c>AIYARA.md</c>) covers the project's structure and conventions,
/// <c>PROJECT.md</c> - if it exists at the workspace root - covers what the project is trying to
/// achieve and for whom: goals, scope, and requirements. Its contents are appended to the system
/// prompt so the model starts every session already knowing that context. The model can read or
/// update it like any other file, via the normal file tools - there's nothing special about its
/// content, only about it being auto-loaded.
/// </summary>
public static class ProjectDocument
{
    public const string FileName = "PROJECT.md";

    /// <summary>
    /// The absolute path <c>PROJECT.md</c> would have at <see cref="Workspace.Root"/>, whether or not
    /// it currently exists.
    /// </summary>
    public static string PathAtWorkspaceRoot => Path.Combine(Workspace.Root, FileName);

    /// <summary>
    /// Reads <c>PROJECT.md</c> from <see cref="Workspace.Root"/>, or returns null if it doesn't exist.
    /// </summary>
    public static string? Load()
    {
        var path = PathAtWorkspaceRoot;
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }
}
