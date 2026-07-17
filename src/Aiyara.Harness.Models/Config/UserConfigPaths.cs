namespace Aiyara.Harness.Models.Config;

/// <summary>
/// Resolves the location of the per-user, editable configuration directory (<c>%USERPROFILE%\.aiyara\</c>).
/// </summary>
public static class UserConfigPaths
{
    /// <summary>
    /// Absolute path to the user's Aiyara config directory, e.g. <c>C:\Users\alice\.aiyara</c>.
    /// </summary>
    public static string Directory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".aiyara");
}
