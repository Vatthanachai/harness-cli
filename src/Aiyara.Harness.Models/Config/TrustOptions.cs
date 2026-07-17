namespace Aiyara.Harness.Models.Config;

/// <summary>
/// Folders and files the user has already approved, stored in <c>trust.json</c> so the harness
/// doesn't ask again for the same one next run.
/// </summary>
public sealed class TrustOptions
{
    /// <summary>
    /// Workspace roots the user has confirmed trusting - once approved, launching the harness in
    /// the same folder again skips the "do you trust this folder" prompt.
    /// </summary>
    public List<string> TrustedWorkspaces { get; set; } = [];

    /// <summary>
    /// Exact file paths outside a workspace the user has approved a tool accessing - once
    /// approved, the same exact path is allowed again without re-asking. Approving one path does
    /// not grant access to other files in the same folder.
    /// </summary>
    public List<string> AllowedExternalPaths { get; set; } = [];
}
