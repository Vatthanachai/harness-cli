namespace Aiyara.Harness.Models.Config;

/// <summary>
/// Optional per-project context file, the persistent-notes counterpart to <see cref="FilesDocument"/>,
/// <see cref="ToolsDocument"/> and <see cref="CommandsDocument"/>. If a <c>MEMORY.md</c> file exists
/// at the workspace root, its contents are appended to the system prompt so the model starts every
/// session already knowing facts, decisions and preferences it (or the user) recorded in past
/// sessions - e.g. "user prefers tabs over spaces", "auth rewrite is blocked on ticket #42" - instead
/// of losing that context when the chat session ends. The model can read or update it like any other
/// file, via the normal file tools - there's nothing special about its content, only about it being
/// auto-loaded.
/// </summary>
public static class MemoryDocument
{
    public const string FileName = "MEMORY.md";

    /// <summary>
    /// The absolute path <c>MEMORY.md</c> would have at <see cref="Workspace.Root"/>, whether or
    /// not it currently exists.
    /// </summary>
    public static string PathAtWorkspaceRoot => Path.Combine(Workspace.Root, FileName);

    /// <summary>
    /// Reads <c>MEMORY.md</c> from <see cref="Workspace.Root"/>, or returns null if it doesn't exist.
    /// </summary>
    public static string? Load()
    {
        var path = PathAtWorkspaceRoot;
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }
}
