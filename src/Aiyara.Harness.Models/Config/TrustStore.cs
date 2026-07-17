namespace Aiyara.Harness.Models.Config;

/// <summary>
/// Reads and updates the persisted trust decisions in <c>trust.json</c>.
/// </summary>
public static class TrustStore
{
    private const string FileName = "trust.json";

    /// <summary>
    /// Whether <paramref name="workspaceRoot"/> was previously approved via the "do you trust
    /// this folder" prompt.
    /// </summary>
    public static bool IsWorkspaceTrusted(string workspaceRoot) =>
        Load().TrustedWorkspaces.Any(p => PathsEqual(p, workspaceRoot));

    /// <summary>
    /// Records <paramref name="workspaceRoot"/> as trusted so future runs there skip the prompt.
    /// </summary>
    public static void TrustWorkspace(string workspaceRoot)
    {
        var options = Load();
        if (options.TrustedWorkspaces.Any(p => PathsEqual(p, workspaceRoot))) return;

        options.TrustedWorkspaces.Add(workspaceRoot);
        UserConfigStore.Save(FileName, options);
    }

    /// <summary>
    /// Whether <paramref name="path"/> was previously approved for tool access outside the
    /// workspace.
    /// </summary>
    public static bool IsExternalPathAllowed(string path) =>
        Load().AllowedExternalPaths.Any(p => PathsEqual(p, path));

    /// <summary>
    /// Records <paramref name="path"/> as an approved external path so future access to that
    /// exact file skips the prompt.
    /// </summary>
    public static void AllowExternalPath(string path)
    {
        var options = Load();
        if (options.AllowedExternalPaths.Any(p => PathsEqual(p, path))) return;

        options.AllowedExternalPaths.Add(path);
        UserConfigStore.Save(FileName, options);
    }

    private static TrustOptions Load() => UserConfigStore.Load(FileName, new TrustOptions());

    private static bool PathsEqual(string a, string b)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var separators = new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
        return string.Equals(a.TrimEnd(separators), b.TrimEnd(separators), comparison);
    }
}
