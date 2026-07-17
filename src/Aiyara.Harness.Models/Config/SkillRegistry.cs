namespace Aiyara.Harness.Models.Config;

/// <summary>
/// Tracks which of the harness's skills (see <see cref="SkillStore"/>) are enabled for the running
/// session - the skill-level counterpart to <c>ToolRegistry</c>'s per-tool enable/disable. Every
/// skill starts enabled; "/skills off &lt;name&gt;" removes one from what <c>use_skill</c> will load,
/// without needing to restart the process. Lives in Models (not alongside <c>ToolRegistry</c> in
/// Cli) because <c>UseSkillTool</c>/<c>ListSkillsTool</c> in the Tools project need to read it, and
/// Tools cannot depend on Cli.
/// </summary>
public sealed class SkillRegistry
{
    private readonly HashSet<string> _disabledNames = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Every skill currently on disk (see <see cref="SkillStore.List"/>), regardless of enabled state.
    /// </summary>
    public IReadOnlyList<SkillInfo> All => SkillStore.List();

    /// <summary>
    /// The subset of <see cref="All"/> currently enabled.
    /// </summary>
    public IReadOnlyList<SkillInfo> Enabled => All.Where(s => !_disabledNames.Contains(s.Name)).ToList();

    /// <summary>
    /// Each skill's name, description and current enabled state.
    /// </summary>
    public IEnumerable<(string Name, string Description, bool Enabled)> Describe() =>
        All.Select(s => (s.Name, s.Description, !_disabledNames.Contains(s.Name)));

    /// <summary>
    /// Whether <paramref name="name"/> both exists and is currently enabled.
    /// </summary>
    public bool IsEnabled(string name) =>
        FindName(name) is { } actual && !_disabledNames.Contains(actual);

    /// <summary>
    /// Enables the named skill. Returns false if no skill with that name exists on disk.
    /// </summary>
    public bool Enable(string name)
    {
        var actual = FindName(name);
        if (actual is null) return false;
        _disabledNames.Remove(actual);
        return true;
    }

    /// <summary>
    /// Disables the named skill. Returns false if no skill with that name exists on disk.
    /// </summary>
    public bool Disable(string name)
    {
        var actual = FindName(name);
        if (actual is null) return false;
        _disabledNames.Add(actual);
        return true;
    }

    private string? FindName(string name) =>
        All.Select(s => s.Name).FirstOrDefault(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
}
