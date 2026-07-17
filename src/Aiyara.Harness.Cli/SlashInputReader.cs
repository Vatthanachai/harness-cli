using System.Text;

using Aiyara.Harness.Cli.Commands;

namespace Aiyara.Harness.Cli;

/// <summary>
/// Reads a line of input inside a Claude Code-style bordered box, with live slash command
/// suggestions - or a persistent status line - displayed below it. Long input wraps onto
/// additional rows within the box (up to <see cref="MaxInputLines"/>) instead of scrolling
/// horizontally, and the buffer can hold real line breaks - typed via Shift/Alt+Enter, or
/// inserted automatically when a multi-line paste is detected. On submit, the box collapses into
/// a "&gt; message" block (still multi-line if the message has embedded breaks).
/// </summary>
internal static class SlashInputReader
{
    private const string HintText = "  ? for shortcuts";
    private const int MaxInputLines = 6;

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
        // *before* it happened, so only the LAST read (after all writes) is trustworthy - derive
        // the earlier rows from it by subtraction instead of reading CursorTop after each write.
        //
        // The box is reserved at its tallest possible size (MaxInputLines rows) up front so that
        // growing/shrinking it while the user types never targets a row that hasn't been written
        // into yet (SetCursorPosition throws on terminals where BufferHeight == WindowHeight).
        // Reserved as blank lines, not pre-drawn border content - Render() below paints the real
        // content on its first call, and ClearRow (used on every redraw) never touches the very
        // last column of a row, so anything left there by this initial write would never get
        // cleared again for the rest of the session.
        var reservedRows = MaxInputLines + maxHintRows + 3; // spacer + top border + input rows + bottom border + hints
        for (var i = 0; i < reservedRows; i++) Console.WriteLine();

        var hintRowMax = Console.CursorTop - maxHintRows;
        var topRow = hintRowMax - MaxInputLines - 2;

        var buffer = new StringBuilder();
        var cursor = 0;

        Render(topRow, width, maxHintRows, buffer, cursor, registry, statusText);

        while (true)
        {
            var key = Console.ReadKey(intercept: true);

            switch (key.Key)
            {
                case ConsoleKey.Enter:
                    // A terminal delivers a pasted block as a burst of synthetic keystrokes,
                    // including a key event for every embedded line break - there's no way to
                    // tell "the user pressed Enter" from "the clipboard contained a newline"
                    // other than this: a real keypress has nothing queued up right behind it, a
                    // paste does. Shift/Alt+Enter is the deliberate manual equivalent for typing
                    // a line break outright.
                    var isPasteBurst = Console.KeyAvailable;
                    var isManualBreak = key.Modifiers.HasFlag(ConsoleModifiers.Shift) || key.Modifiers.HasFlag(ConsoleModifiers.Alt);
                    if (isPasteBurst || isManualBreak)
                    {
                        buffer.Insert(cursor, '\n');
                        cursor++;
                        break;
                    }

                    ClearRow(topRow, 1);
                    ClearRow(topRow + 1, MaxInputLines + 1 + maxHintRows);
                    Console.SetCursorPosition(0, topRow);
                    ConsoleTheme.WriteColored("  > ", $"{ConsoleTheme.Dim}{ConsoleTheme.Orange}");
                    ConsoleTheme.WriteLineColored(buffer.ToString().Replace("\n", "\n    "), ConsoleTheme.Gray);
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

            width = Math.Max(Console.WindowWidth, 20);
            Render(topRow, width, maxHintRows, buffer, cursor, registry, statusText);
        }
    }

    /// <summary>
    /// Redraws the whole box + hint area from scratch: the top border (fixed row), as many
    /// wrapped input rows as the buffer currently needs (growing/shrinking the box), the bottom
    /// border right after them, and the suggestion/status hints right after that. Clearing the
    /// full reserved tail before redrawing keeps this correct in both directions - e.g. a row that
    /// held hint text before the box grew, or held border/input text before it shrank, would
    /// otherwise stay behind as stale output that nothing else overwrites this frame.
    /// </summary>
    private static void Render(int topRow, int width, int maxHintRows, StringBuilder buffer, int cursor, SlashCommandRegistry registry, string statusText)
    {
        ClearRow(topRow, 1);
        Console.SetCursorPosition(0, topRow);
        ConsoleTheme.WriteColored(new string(ConsoleTheme.Horizontal[0], Math.Max(width - 1, 0)), ConsoleTheme.Orange);

        ClearRow(topRow + 1, MaxInputLines + 1 + maxHintRows);

        var maxTextLen = Math.Max(GetMaxTextLen(width), 1);
        var text = buffer.ToString();
        var allRows = BuildRows(text, maxTextLen);
        var (cursorRowIndex, cursorCol) = LocateCursor(allRows, cursor);

        // Keep the cursor's row inside the visible window, pinned to the bottom once there's more
        // content above than fits - the same idea as a normal scrolling text area.
        var windowStart = Math.Clamp(cursorRowIndex - (MaxInputLines - 1), 0, Math.Max(allRows.Count - MaxInputLines, 0));
        var visibleRows = Math.Min(MaxInputLines, allRows.Count);

        for (var i = 0; i < visibleRows; i++)
        {
            var rowIndex = windowStart + i;
            var (start, length) = allRows[rowIndex];

            Console.SetCursorPosition(0, topRow + 1 + i);
            var lineText = text.Substring(start, length);

            Console.Write(" ");
            ConsoleTheme.WriteColored(rowIndex == 0 ? "> " : "  ", $"{ConsoleTheme.Bold}{ConsoleTheme.Orange}");
            ConsoleTheme.WriteColored(lineText.PadRight(maxTextLen), ConsoleTheme.White);
        }

        var bottomRow = topRow + 1 + visibleRows;
        Console.SetCursorPosition(0, bottomRow);
        ConsoleTheme.WriteColored(new string(ConsoleTheme.Horizontal[0], Math.Max(width - 1, 0)), ConsoleTheme.Orange);

        var hintRow = bottomRow + 1;
        RenderBelowBox(buffer, registry, hintRow, maxHintRows, statusText);

        Console.SetCursorPosition(3 + Math.Min(cursorCol, maxTextLen), topRow + 1 + (cursorRowIndex - windowStart)); // " > " prefix is 3 columns wide
    }

    // Splits the buffer on explicit '\n' breaks, then further wraps each of those lines across
    // rows of at most maxTextLen characters - so a hard break from Enter and a soft break from
    // running out of width both end up in the same flat row list. Each row is the (start, length)
    // slice of `text` it displays; an empty explicit line still gets one (empty) row so it renders
    // as a visible blank line rather than disappearing.
    private static List<(int start, int length)> BuildRows(string text, int maxTextLen)
    {
        var rows = new List<(int start, int length)>();
        var lineStart = 0;

        for (var i = 0; i <= text.Length; i++)
        {
            if (i != text.Length && text[i] != '\n') continue;

            var lineLength = i - lineStart;
            if (lineLength == 0)
            {
                rows.Add((lineStart, 0));
            }
            else
            {
                var pos = lineStart;
                var remaining = lineLength;
                while (remaining > 0)
                {
                    var take = Math.Min(maxTextLen, remaining);
                    rows.Add((pos, take));
                    pos += take;
                    remaining -= take;
                }
            }

            lineStart = i + 1;
        }

        return rows;
    }

    // Maps an absolute buffer index to (row, column) within the flat row list. A cursor sitting
    // exactly on a soft-wrap boundary (rows contiguous, no '\n' consumed between them) resolves to
    // the end of the earlier row rather than column 0 of the next - matching how text editors place
    // the caret at wrap points. A hard break resolves the same way for free: the row after it
    // starts one index later (past the consumed '\n'), so the boundary index can only ever match
    // the earlier row's end.
    private static (int row, int col) LocateCursor(List<(int start, int length)> rows, int cursor)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            var (start, length) = rows[i];
            if (cursor <= start + length)
                return (i, cursor - start);
        }

        return (rows.Count - 1, 0);
    }

    // Row layout is " " + prefix (2 chars) + text, with no side borders - so 3 columns of the
    // window width are spoken for before the text starts. One more is held back as a margin so
    // the last visible character never lands on the terminal's final column.
    private static int GetMaxTextLen(int width) => Math.Max(width - 4, 0);

    private static void RenderBelowBox(StringBuilder buffer, SlashCommandRegistry registry, int hintRow, int maxHintRows, string statusText)
    {
        var suggestions = buffer.Length > 0 && buffer[0] == '/'
            ? registry.Match(buffer.ToString(1, buffer.Length - 1)).ToList()
            : [];

        var maxShow = Math.Min(suggestions.Count, maxHintRows);

        for (var i = 0; i < maxHintRows; i++)
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
