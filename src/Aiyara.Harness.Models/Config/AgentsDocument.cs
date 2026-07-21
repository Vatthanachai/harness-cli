namespace Aiyara.Harness.Models.Config;

/// <summary>
/// Optional per-project context file, this harness's compatibility counterpart to
/// <see cref="AiyaraDocument"/> (<c>AIYARA.md</c>). <c>AGENTS.md</c> is the emerging cross-tool
/// convention other AI coding agents (e.g. Codex, Cursor) already look for, so a project that has one
/// for those tools gets picked up here too, without needing an <c>AIYARA.md</c> of its own. If it
/// exists at the workspace root, its contents are appended to the system prompt the same way as
/// <c>AIYARA.md</c>. The model can read or update it like any other file, via the normal file tools -
/// there's nothing special about its content, only about it being auto-loaded.
/// </summary>
public static class AgentsDocument
{
    public const string FileName = "AGENTS.md";

    /// <summary>
    /// The absolute path <c>AGENTS.md</c> would have at <see cref="Workspace.Root"/>, whether or not
    /// it currently exists.
    /// </summary>
    public static string PathAtWorkspaceRoot => Path.Combine(Workspace.Root, FileName);

    /// <summary>
    /// Reads <c>AGENTS.md</c> from <see cref="Workspace.Root"/>, or returns null if it doesn't exist.
    /// </summary>
    public static string? Load()
    {
        var path = PathAtWorkspaceRoot;
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }
}
