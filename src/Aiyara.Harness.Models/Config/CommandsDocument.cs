namespace Aiyara.Harness.Models.Config;

/// <summary>
/// Optional per-project context file, the shell-command counterpart to <see cref="FilesDocument"/>
/// and <see cref="ToolsDocument"/>. If a <c>COMMANDS.md</c> file exists at the workspace root, its
/// contents are appended to the system prompt so the model starts every session already knowing
/// this project's build/test/run commands (e.g. "build: dotnet build aiyara-harness.slnx", "test:
/// dotnet test"), instead of guessing them via trial-and-error through <c>run_command</c>. The model
/// can read or update it like any other file, via the normal file tools - there's nothing special
/// about its content, only about it being auto-loaded.
/// </summary>
public static class CommandsDocument
{
    public const string FileName = "COMMANDS.md";

    /// <summary>
    /// The absolute path <c>COMMANDS.md</c> would have at <see cref="Workspace.Root"/>, whether or
    /// not it currently exists.
    /// </summary>
    public static string PathAtWorkspaceRoot => Path.Combine(Workspace.Root, FileName);

    /// <summary>
    /// Reads <c>COMMANDS.md</c> from <see cref="Workspace.Root"/>, or returns null if it doesn't exist.
    /// </summary>
    public static string? Load()
    {
        var path = PathAtWorkspaceRoot;
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }
}
