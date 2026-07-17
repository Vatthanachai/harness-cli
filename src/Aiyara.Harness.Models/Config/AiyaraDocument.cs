namespace Aiyara.Harness.Models.Config;

/// <summary>
/// Optional per-project context file, this harness's equivalent of Claude Code's <c>CLAUDE.md</c>:
/// a general-purpose project overview and instructions file, auto-loaded into the system prompt
/// when present at the workspace root, ahead of the more specialized <see cref="FilesDocument"/>,
/// <see cref="ToolsDocument"/>, <see cref="CommandsDocument"/> and <see cref="MemoryDocument"/>. The
/// model can read or update it like any other file, via the normal file tools - there's nothing
/// special about its content, only about it being auto-loaded. Unlike the other four, the harness
/// also offers to generate one for a project that doesn't have it yet (see <c>Program.cs</c>),
/// mirroring Claude Code's own onboarding prompt.
/// </summary>
public static class AiyaraDocument
{
    public const string FileName = "AIYARA.md";

    /// <summary>
    /// The absolute path <c>AIYARA.md</c> would have at <see cref="Workspace.Root"/>, whether or
    /// not it currently exists.
    /// </summary>
    public static string PathAtWorkspaceRoot => Path.Combine(Workspace.Root, FileName);

    /// <summary>
    /// Reads <c>AIYARA.md</c> from <see cref="Workspace.Root"/>, or returns null if it doesn't exist.
    /// </summary>
    public static string? Load()
    {
        var path = PathAtWorkspaceRoot;
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }
}
