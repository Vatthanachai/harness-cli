namespace Aiyara.Harness.Models.Config;

/// <summary>
/// A "This &lt;noun&gt; requires approval" prompt for one-off decisions that don't repeat identically
/// within a run - trusting a workspace folder, approving a tool's access to a path outside it, a
/// one-time yes/no like offering to generate AIYARA.md. Built on the same <see cref="ConsoleSelect"/>
/// primitive as <see cref="ActionConsent"/>, so it looks and behaves identically (same header, same
/// arrow-key/typed Yes/No selection) - it just skips <see cref="ActionConsent"/>'s "allow for this
/// session" memory, since these decisions aren't the kind a single run makes many times over.
/// </summary>
public static class ConsentPrompt
{
    /// <summary>
    /// Shows "This &lt;<paramref name="noun"/>&gt; requires approval", <paramref name="detailLines"/>
    /// indented below it, then <paramref name="question"/> with a Yes/No choice. Returns true only
    /// if "Yes" is chosen.
    /// </summary>
    public static bool Confirm(string noun, IReadOnlyList<string> detailLines, string question) =>
        ConsoleSelect.Prompt(noun, detailLines, question,
            [("Yes", "Approve and continue"), ("No", "Cancel and don’t proceed")],
            defaultIndex: 0) == 0;
}
