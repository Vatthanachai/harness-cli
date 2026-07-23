namespace Aiyara.Harness.Cli;

/// <summary>
/// ANSI color and style constants for the Claude Code-inspired console theme.
/// </summary>
internal static class ConsoleTheme
{
    public const string Reset = "\x1b[0m";
    public const string Bold = "\x1b[1m";
    public const string Dim = "\x1b[2m";
    public const string Italic = "\x1b[3m";

    // Accent color, mapped to the standard "yellow" ANSI slot rather than a truecolor escape —
    // this way it renders using whatever the user's terminal color scheme defines for that slot
    // (e.g. a Windows Terminal profile) instead of a fixed RGB that ignores their chosen scheme.
    public const string Orange = "\x1b[33m";

    public const string Cyan = "\x1b[36m";
    public const string BrightCyan = "\x1b[96m";
    public const string Blue = "\x1b[34m";
    public const string BrightBlue = "\x1b[94m";
    public const string Green = "\x1b[32m";
    public const string BrightGreen = "\x1b[92m";
    public const string Yellow = "\x1b[33m";
    public const string BrightYellow = "\x1b[93m";
    public const string Magenta = "\x1b[35m";
    public const string BrightMagenta = "\x1b[95m";
    public const string Red = "\x1b[31m";
    public const string White = "\x1b[97m";
    public const string Gray = "\x1b[90m";

    public const string BgBlue = "\x1b[44m";
    public const string BgCyan = "\x1b[46m";

    // Box-drawing
    public const string TopLeft = "╭";
    public const string TopRight = "╮";
    public const string BottomLeft = "╰";
    public const string BottomRight = "╯";
    public const string Horizontal = "─";
    public const string Vertical = "│";

    // Markers
    public const string ToolCall = "⏺";
    public const string ToolResult = "⎿";
    public const string Thinking = "✻";
    public const string Star = "✻";
    public const string Arrow = "▶";

    public static string Colorize(string text, string color) => $"{color}{text}{Reset}";

    public static void WriteColored(string text, string color)
    {
        Console.Write($"{color}{text}{Reset}");
    }

    public static void WriteLineColored(string text, string color)
    {
        Console.WriteLine($"{color}{text}{Reset}");
    }

    public static void WriteBoxTop(int width)
    {
        WriteLineColored($"{TopLeft}{new string(Horizontal[0], Math.Max(width - 2, 0))}{TopRight}", Orange);
    }

    public static void WriteBoxBottom(int width)
    {
        WriteLineColored($"{BottomLeft}{new string(Horizontal[0], Math.Max(width - 2, 0))}{BottomRight}", Orange);
    }

    public static void WriteBoxLine(string text, int width, string color)
    {
        var interior = Math.Max(width - 4, 0);
        if (text.Length > interior)
            text = text[..interior];

        WriteColored(Vertical, Orange);
        Console.Write(" ");
        WriteColored(text.PadRight(interior), color);
        Console.Write(" ");
        WriteLineColored(Vertical, Orange);
    }

    public static void WriteBanner(string modelName, string workspaceRoot)
    {
        var width = Math.Clamp(Console.WindowWidth, 40, 70);

        Console.WriteLine();
        WriteBoxTop(width);
        WriteBoxLine($"{Star} Welcome to Aiyara Harness!", width, $"{Bold}{Orange}");
        WriteBoxLine("", width, "");
        WriteBoxLine($"/help for commands · model: {modelName}", width, White);
        WriteBoxLine($"cwd: {workspaceRoot}", width, Gray);
        WriteBoxBottom(width);
        Console.WriteLine();
        WriteLineColored("  Type your message, /command, or 'exit' to quit.", Gray);
        Console.WriteLine();
    }

    public static void WriteAssistantHeader()
    {
        Console.WriteLine();
    }

    public static void WriteToolCallHeader(string toolName)
    {
        Console.WriteLine();
        WriteColored($"  {ToolCall} ", Orange);
        WriteLineColored(toolName, $"{Bold}{White}");
    }

    public static void WriteToolResult(string result)
    {
        var lines = result.Split('\n');
        foreach (var line in lines)
        {
            WriteColored($"    {ToolResult}  ", Gray);
            WriteLineColored(line, Gray);
        }
    }

    public static void WriteThinking(string thought)
    {
        Console.WriteLine();
        WriteColored($"  {Thinking} ", Gray);
        WriteLineColored("Thinking…", $"{Dim}{Italic}{Gray}");

        var lines = thought.Split('\n');
        var maxLines = Math.Min(lines.Length, 5);
        for (var i = 0; i < maxLines; i++)
        {
            var line = lines[i];
            if (line.Length > Console.WindowWidth - 8)
                line = line[..(Console.WindowWidth - 11)] + "...";
            WriteLineColored($"      {line}", $"{Dim}{Gray}");
        }
        if (lines.Length > 5)
            WriteLineColored($"      ... ({lines.Length - 5} more lines)", $"{Dim}{Gray}");
    }

    /// <summary>
    /// Writes <paramref name="left"/> at the current cursor position and, space permitting,
    /// right-aligns <paramref name="right"/> against <paramref name="width"/> on the same row -
    /// e.g. the "? for shortcuts" hint alongside a "model: ..." status line.
    /// </summary>
    public static void WriteStatusLine(string left, string right, int width)
    {
        if (string.IsNullOrEmpty(right))
        {
            WriteColored(left, Gray);
            return;
        }

        var gap = width - left.Length - right.Length;
        if (gap <= 0)
        {
            WriteColored(left, Gray);
            return;
        }

        WriteColored(left, Gray);
        Console.Write(new string(' ', gap));
        WriteColored(right, Gray);
    }

    public static void WriteSlashResult(string text)
    {
        WriteLineColored($"  {text}", White);
    }

    public static void WriteError(string text)
    {
        WriteLineColored($"  ✗ {text}", Red);
    }

    public static void WriteInterruptHint()
    {
        WriteLineColored("  (Esc or Ctrl+C to interrupt)", $"{Dim}{Gray}");
    }

    public static void WriteInterrupted()
    {
        WriteLineColored($"  {ToolResult} Interrupted by user", Gray);
    }

    public static void WriteSeparator()
    {
        var width = Math.Min(Console.WindowWidth - 4, 60);
        WriteLineColored($"  {new string('─', width)}", Gray);
    }
}
