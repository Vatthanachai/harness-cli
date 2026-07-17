namespace Aiyara.Harness.Models.Config;

/// <summary>
/// Optional per-project context file, the tool-usage counterpart to <see cref="FilesDocument"/>. If
/// a <c>TOOLS.md</c> file exists at the workspace root, its contents are appended to the system
/// prompt so the model starts every session already knowing this project's conventions for using
/// its tools (e.g. "run dotnet build after editing C# files", "never run destructive git commands").
/// The model can read or update it like any other file, via the normal file tools - there's nothing
/// special about its content, only about it being auto-loaded.
/// </summary>
public static class ToolsDocument
{
    public const string FileName = "TOOLS.md";

    /// <summary>
    /// The absolute path <c>TOOLS.md</c> would have at <see cref="Workspace.Root"/>, whether or not
    /// it currently exists.
    /// </summary>
    public static string PathAtWorkspaceRoot => Path.Combine(Workspace.Root, FileName);

    /// <summary>
    /// Reads <c>TOOLS.md</c> from <see cref="Workspace.Root"/>, or returns null if it doesn't exist.
    /// </summary>
    public static string? Load()
    {
        var path = PathAtWorkspaceRoot;
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }
}
