using System.Text;

using Aiyara.Harness.Cli.Commands;

namespace Aiyara.Harness.Cli;

/// <summary>
/// Reads a line of input inside a Claude Code-style bordered box, with live slash command
/// suggestions - or a persistent status line - displayed below it. On submit, the box collapses
/// into a single "&gt; message" line.
/// </summary>
internal static class SlashInputReader
{
    private const string HintText = "  ? for shortcuts";

    public static string ReadLine(SlashCommandRegistry registry, TerminalUI ui, string statusText = "")
    {
        if (!ui.IsActive)
        {
            Console.Write("> ");
            return Console.ReadLine() ?? "";
        }

        var width = Math.Max(Console.WindowWidth, 20);
        var maxHintRows = Math.Max(ui.MaxSuggestions, 1);

        // Writing lines here can scroll the whole buffer if the cursor is already at the bottom
        // (common once enough turns have printed). A scroll shifts every row that was read
        // *before* it happened, so only the LAST read (after all writes) is trustworthy — derive
        // the earlier rows from it by subtraction instead of reading CursorTop after each write.
        //
        // The hint/suggestion area below the box is never written via WriteLine (SetCursorPosition
        // there directly, to redraw it in place on every keystroke without scrolling mid-edit) —
        // so unlike the box rows above, nothing forces the buffer to grow enough to hold it. On
        // terminals where BufferHeight == WindowHeight (Windows Terminal, VS Code's integrated
        // terminal), SetCursorPosition never scrolls, so targeting a row that hasn't been written
        // into yet throws ArgumentOutOfRangeException. Reserve the rows with WriteLine up front so
        // they're guaranteed to exist before anything ever targets them directly.
        Console.WriteLine();
        ConsoleTheme.WriteBoxTop(width);
        Console.WriteLine();
        ConsoleTheme.WriteBoxBottom(width);
        for (var i = 0; i < maxHintRows; i++) Console.WriteLine();

        var hintRow = Console.CursorTop - maxHintRows;
        var bottomRow = hintRow - 1;
        var inputRow = hintRow - 2;
        var topRow = hintRow - 3;

        var buffer = new StringBuilder();
        var cursor = 0;
        var lastSuggestionCount = 0;

        RenderInputRow(inputRow, width, buffer);
        RenderBelowBox(buffer, registry, hintRow, maxHintRows, statusText, ref lastSuggestionCount);
        Console.SetCursorPosition(PromptColumn(cursor, buffer.Length, GetMaxTextLen(width)), inputRow);

        while (true)
        {
            var key = Console.ReadKey(intercept: true);

            switch (key.Key)
            {
                case ConsoleKey.Enter:
                    ClearRow(hintRow, Math.Max(lastSuggestionCount, 1));
                    ClearRow(inputRow, 1);
                    ClearRow(bottomRow, 1);
                    ClearRow(topRow, 1);
                    Console.SetCursorPosition(0, topRow);
                    ConsoleTheme.WriteColored("  > ", $"{ConsoleTheme.Dim}{ConsoleTheme.Orange}");
                    ConsoleTheme.WriteLineColored(buffer.ToString(), ConsoleTheme.Gray);
                    return buffer.ToString();

                case ConsoleKey.Escape:
                    buffer.Clear();
                    cursor = 0;
                    break;

                case ConsoleKey.Backspace:
                    if (cursor > 0)
                    {
                        buffer.Remove(cursor - 1, 1);
                        cursor--;
                    }
                    break;

                case ConsoleKey.Delete:
                    if (cursor < buffer.Length)
                        buffer.Remove(cursor, 1);
                    break;

                case ConsoleKey.LeftArrow:
                    if (cursor > 0) cursor--;
                    break;

                case ConsoleKey.RightArrow:
                    if (cursor < buffer.Length) cursor++;
                    break;

                case ConsoleKey.Home:
                    cursor = 0;
                    break;

                case ConsoleKey.End:
                    cursor = buffer.Length;
                    break;

                case ConsoleKey.Tab:
                    if (buffer.Length > 0 && buffer[0] == '/')
                    {
                        var match = registry.Match(buffer.ToString(1, buffer.Length - 1)).FirstOrDefault();
                        if (match is not null)
                        {
                            buffer.Clear();
                            buffer.Append('/').Append(match.Name).Append(' ');
                            cursor = buffer.Length;
                        }
                    }
                    break;

                default:
                    if (!char.IsControl(key.KeyChar))
                    {
                        buffer.Insert(cursor, key.KeyChar);
                        cursor++;
                    }
                    break;
            }

            var currentWidth = Math.Max(Console.WindowWidth, 20);
            if (currentWidth != width)
            {
                width = currentWidth;
                RedrawBorders(topRow, bottomRow, width);
            }

            RenderInputRow(inputRow, width, buffer);
            RenderBelowBox(buffer, registry, hintRow, maxHintRows, statusText, ref lastSuggestionCount);
            Console.SetCursorPosition(PromptColumn(cursor, buffer.Length, GetMaxTextLen(width)), inputRow);
        }
    }

    // No trailing newline here (unlike ConsoleTheme.WriteBoxTop/Bottom) — this runs mid-edit,
    // after the box's rows are already fixed, and a stray newline at the buffer's last row
    // would scroll the screen and invalidate topRow/inputRow/bottomRow/hintRow all at once.
    private static void RedrawBorders(int topRow, int bottomRow, int width)
    {
        ClearRow(topRow, 1);
        ClearRow(bottomRow, 1);

        Console.SetCursorPosition(0, topRow);
        ConsoleTheme.WriteColored($"{ConsoleTheme.TopLeft}{new string(ConsoleTheme.Horizontal[0], Math.Max(width - 2, 0))}{ConsoleTheme.TopRight}", ConsoleTheme.Orange);

        Console.SetCursorPosition(0, bottomRow);
        ConsoleTheme.WriteColored($"{ConsoleTheme.BottomLeft}{new string(ConsoleTheme.Horizontal[0], Math.Max(width - 2, 0))}{ConsoleTheme.BottomRight}", ConsoleTheme.Orange);
    }

    // Once the buffer is longer than fits in the box, RenderInputRow scrolls to show only the last
    // maxTextLen characters - so the cursor's on-screen column has to account for that same scroll
    // offset instead of the raw (unbounded) buffer index, or it drifts past the console's actual
    // width as the user keeps typing and crashes SetCursorPosition ('left' out of range).
    private static int PromptColumn(int cursor, int bufferLength, int maxTextLen)
    {
        var scrollOffset = Math.Max(bufferLength - maxTextLen, 0);
        var visibleCursor = Math.Clamp(cursor - scrollOffset, 0, maxTextLen);
        return 4 + visibleCursor; // "│ > " prefix is 4 columns wide
    }

    private static int GetMaxTextLen(int width) => Math.Max(Math.Max(width - 4, 0) - 2, 0);

    private static void RenderInputRow(int row, int width, StringBuilder buffer)
    {
        Console.SetCursorPosition(0, row);
        var maxTextLen = GetMaxTextLen(width);
        var text = buffer.ToString();
        if (text.Length > maxTextLen)
            text = text[(text.Length - maxTextLen)..];

        ConsoleTheme.WriteColored(ConsoleTheme.Vertical, ConsoleTheme.Orange);
        Console.Write(" ");
        ConsoleTheme.WriteColored("> ", $"{ConsoleTheme.Bold}{ConsoleTheme.Orange}");
        ConsoleTheme.WriteColored(text.PadRight(maxTextLen), ConsoleTheme.White);
        Console.Write(" ");
        ConsoleTheme.WriteColored(ConsoleTheme.Vertical, ConsoleTheme.Orange);
    }

    private static void RenderBelowBox(StringBuilder buffer, SlashCommandRegistry registry, int hintRow, int maxHintRows, string statusText, ref int lastCount)
    {
        var suggestions = buffer.Length > 0 && buffer[0] == '/'
            ? registry.Match(buffer.ToString(1, buffer.Length - 1)).ToList()
            : [];

        var maxShow = Math.Min(suggestions.Count, maxHintRows);
        var rowsToClear = Math.Max(maxShow, Math.Max(lastCount, 1));

        for (var i = 0; i < rowsToClear; i++)
        {
            Console.SetCursorPosition(0, hintRow + i);
            Console.Write(new string(' ', Console.WindowWidth - 1));
            Console.SetCursorPosition(0, hintRow + i);

            if (i < maxShow)
            {
                var cmd = suggestions[i];
                ConsoleTheme.WriteColored($"    /{cmd.Name,-12}", ConsoleTheme.Orange);
                ConsoleTheme.WriteColored(cmd.Description, ConsoleTheme.Gray);
            }
            else if (i == 0 && maxShow == 0)
            {
                ConsoleTheme.WriteStatusLine(HintText, statusText, Console.WindowWidth - 1);
            }
        }

        lastCount = maxShow;
    }

    private static void ClearRow(int row, int count)
    {
        for (var i = 0; i < count; i++)
        {
            Console.SetCursorPosition(0, row + i);
            Console.Write(new string(' ', Console.WindowWidth - 1));
        }
    }
}
