namespace Aiyara.Harness.Models.Config;

/// <summary>
/// The project folder this run of the harness is scoped to, mirroring how Claude Code confines
/// its file tools to the directory it was launched in. File-touching tools must resolve every
/// path through <see cref="ResolvePath"/> rather than using a raw path directly, so a path that
/// would land outside the workspace is rejected instead of silently reading/writing there.
/// </summary>
public static class Workspace
{
    private static string _root = Directory.GetCurrentDirectory();

    /// <summary>
    /// The current workspace root: an absolute path with no trailing directory separator.
    /// </summary>
    public static string Root => _root;

    /// <summary>
    /// Sets the workspace root for this run - <paramref name="path"/> if given (e.g. a CLI
    /// argument), otherwise the directory the process was launched from. Throws if the resolved
    /// path doesn't exist as a directory.
    /// </summary>
    public static void Initialize(string? path)
    {
        var candidate = string.IsNullOrWhiteSpace(path)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(path);

        if (!Directory.Exists(candidate))
            throw new DirectoryNotFoundException($"Workspace folder '{candidate}' does not exist.");

        _root = candidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    /// <summary>
    /// Resolves <paramref name="path"/> against the workspace root (relative paths are taken as
    /// relative to it). A path that stays inside the workspace is returned immediately. One that
    /// escapes it - via "..", a rooted path elsewhere, or similar - is not rejected outright:
    /// if it was already approved in a past session it's allowed silently, otherwise the user is
    /// asked to approve it right now via <see cref="ConsentPrompt"/>. Throws
    /// <see cref="UnauthorizedAccessException"/> only if the user declines.
    /// </summary>
    public static string ResolvePath(string path)
    {
        var combined = Path.IsPathRooted(path) ? path : Path.Combine(_root, path);
        var full = Path.GetFullPath(combined);

        if (IsInsideRoot(full)) return full;

        if (TrustStore.IsExternalPathAllowed(full)) return full;

        var approved = ConsentPrompt.Confirm(
            "external path access",
            [full, $"(workspace: {_root})"],
            "Allow this?");

        if (!approved)
        {
            throw new UnauthorizedAccessException(
                $"Access to '{full}' was not approved; it is outside the workspace ('{_root}').");
        }

        TrustStore.AllowExternalPath(full);
        return full;
    }

    private static bool IsInsideRoot(string full)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var rootWithSeparator = _root + Path.DirectorySeparatorChar;
        return full.Equals(_root, comparison) || full.StartsWith(rootWithSeparator, comparison);
    }
}
