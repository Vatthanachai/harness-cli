using OllamaSharp.Models.Chat;

namespace Aiyara.Harness.Cli;

/// <summary>
/// Tracks which of the harness's tools are enabled for the running chat session. Every tool starts
/// enabled; "/tools off &lt;name&gt;" removes one from what gets sent to the model on the next turn,
/// without needing to restart the process.
/// </summary>
public sealed class ToolRegistry
{
    private readonly List<object> _all;
    private readonly HashSet<string> _disabledNames = new(StringComparer.OrdinalIgnoreCase);

    public ToolRegistry(IEnumerable<object> tools) => _all = tools.ToList();

    /// <summary>
    /// Every tool the harness knows about, in registration order.
    /// </summary>
    public IReadOnlyList<object> All => _all;

    /// <summary>
    /// The subset of <see cref="All"/> currently enabled - what actually gets sent to the model
    /// each turn.
    /// </summary>
    public IReadOnlyList<object> Enabled => _all.Where(t => !_disabledNames.Contains(NameOf(t))).ToList();

    /// <summary>
    /// Each tool's name, description and current enabled state, in registration order.
    /// </summary>
    public IEnumerable<(string Name, string? Description, bool Enabled)> Describe() =>
        _all.Select(t => (NameOf(t), ((Tool)t).Function?.Description, !_disabledNames.Contains(NameOf(t))));

    /// <summary>
    /// Enables the named tool. Returns false if no tool with that name is registered.
    /// </summary>
    public bool Enable(string name)
    {
        var actual = FindName(name);
        if (actual is null) return false;
        _disabledNames.Remove(actual);
        return true;
    }

    /// <summary>
    /// Disables the named tool. Returns false if no tool with that name is registered.
    /// </summary>
    public bool Disable(string name)
    {
        var actual = FindName(name);
        if (actual is null) return false;
        _disabledNames.Add(actual);
        return true;
    }

    private string? FindName(string name) =>
        _all.Select(NameOf).FirstOrDefault(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));

    private static string NameOf(object tool) => ((Tool)tool).Function?.Name ?? tool.GetType().Name;
}
