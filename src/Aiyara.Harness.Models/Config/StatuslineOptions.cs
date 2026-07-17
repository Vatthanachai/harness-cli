namespace Aiyara.Harness.Models.Config;

/// <summary>
/// Custom status line, stored in <c>statusline.json</c>.
/// </summary>
public sealed class StatuslineOptions
{
    /// <summary>
    /// Shell command run once per prompt; its trimmed first line of stdout becomes the status
    /// line text. Receives a small JSON context object on stdin (model, tool counts, cwd). Left
    /// empty, the harness falls back to its built-in "model: ... - tools x/y" text. If the
    /// command fails, times out, or exits non-zero, the harness also falls back to the built-in
    /// text for that turn.
    /// </summary>
    public string Command { get; set; } = "";
}
