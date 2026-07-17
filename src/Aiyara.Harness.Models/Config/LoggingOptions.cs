namespace Aiyara.Harness.Models.Config;

/// <summary>
/// Preferences around the model's thinking/reasoning content, stored in <c>logging.json</c>. The
/// two toggles are independent - the log and the on-screen display are separate channels, each off
/// by default so neither has to opt the other in.
/// </summary>
public sealed class LoggingOptions
{
    /// <summary>
    /// Whether the model's thinking/reasoning content gets written to the log (console + rolling
    /// file, via Serilog) each time a turn flushes it. Off by default - can bloat the log file
    /// quickly if left on, and <see cref="ShowThinking"/> already covers wanting to see it live.
    /// </summary>
    public bool LogThinking { get; set; } = false;

    /// <summary>
    /// Whether the model's thinking/reasoning content is displayed live in the terminal (the
    /// "Thinking…" section, see <c>ConsoleTheme.WriteThinking</c>) as each turn flushes it. Off by
    /// default - most reasoning traces are verbose and not meant to be read, just to improve the
    /// answer that follows.
    /// </summary>
    public bool ShowThinking { get; set; } = false;
}
