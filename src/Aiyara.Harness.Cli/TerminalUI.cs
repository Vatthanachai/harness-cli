namespace Aiyara.Harness.Cli;

/// <summary>
/// Simplified terminal UI — no split-screen ANSI regions. Uses a simple scrolling layout
/// inspired by Claude Code / Cave with colored output and Unicode markers.
/// </summary>
public sealed class TerminalUI : IDisposable
{
    public int MaxSuggestions { get; }
    public bool IsActive { get; }

    public TerminalUI(int maxSuggestions)
    {
        MaxSuggestions = Math.Max(0, maxSuggestions);
        IsActive = !Console.IsInputRedirected && !Console.IsOutputRedirected;
    }

    public void Initialize(string modelName, string workspaceRoot)
    {
        if (!IsActive) return;

        Console.Clear();
        ConsoleTheme.WriteBanner(modelName, workspaceRoot);
    }

    public void Dispose()
    {
        if (!IsActive) return;
        Console.WriteLine();
        ConsoleTheme.WriteLineColored("  Goodbye! 👋", ConsoleTheme.Dim + ConsoleTheme.Gray);
        Console.Write(ConsoleTheme.Reset);
    }
}
