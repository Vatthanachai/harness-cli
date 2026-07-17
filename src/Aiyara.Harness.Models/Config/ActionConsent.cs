namespace Aiyara.Harness.Models.Config;

/// <summary>
/// A three-way permission prompt for actions that need per-call approval - writing/overwriting a
/// file, or running a shell command. Built on <see cref="ConsoleSelect"/> for its "This &lt;noun&gt;
/// requires approval" look and arrow-key/typed selection; unlike <see cref="ConsentPrompt"/>, it also
/// remembers an "allow for this session" answer by action key, since the same action can
/// legitimately repeat many times in one run. Unlike <see cref="TrustStore"/>, that memory lives
/// only in memory for the current run - it is never written to disk, so the next run of the
/// harness asks again.
/// </summary>
public static class ActionConsent
{
    private static readonly HashSet<string> AllowedForSession = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns true if <paramref name="actionKey"/> was already allowed for this session - a file
    /// tool keys broadly (e.g. "write_file", so approving one write covers all writes for the rest
    /// of the session); a command tool should key narrowly, e.g. by the exact command line, so
    /// approving one command doesn't silently approve a different, unreviewed one. Otherwise shows
    /// "This &lt;<paramref name="noun"/>&gt; requires approval" with <paramref name="detail"/> (the
    /// specific command/path) folded into the "allow for session" option, then asks. Choosing
    /// "allow for session" remembers <paramref name="actionKey"/> so later calls with the same key
    /// skip the prompt for the rest of this run.
    /// </summary>
    public static bool Confirm(string actionKey, string noun, string detail)
    {
        if (AllowedForSession.Contains(actionKey)) return true;

        (string Label, string Description)[] options =
        [
            ("Yes", "Approve this one time only"),
            ($"Yes, and don’t ask again for: {detail}", "Approve and skip this confirmation for the rest of the session"),
            ("No", "Decline and cancel this action")
        ];
        var selected = ConsoleSelect.Prompt(noun, [], "Do you want to proceed?", options, defaultIndex: 0);

        switch (selected)
        {
            case 0:
                return true;
            case 1:
                AllowedForSession.Add(actionKey);
                return true;
            default:
                return false;
        }
    }
}
