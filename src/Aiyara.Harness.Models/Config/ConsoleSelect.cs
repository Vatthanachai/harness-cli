namespace Aiyara.Harness.Models.Config;

/// <summary>
/// Shared "This &lt;noun&gt; requires approval" arrow-key/typed option selector backing both
/// <see cref="ActionConsent"/> and <see cref="ConsentPrompt"/>, so their look and interaction stay
/// identical without duplicating the console-cursor handling twice. Modeled on Claude Code's own
/// approval prompt: each option gets a one-line description under it, and an "Esc to cancel" hint
/// is shown below the list (interactive sessions only - a redirected/piped session has no way to
/// send a raw Escape keypress through <c>Console.ReadLine</c>, so showing the hint there would be
/// misleading). Its ANSI constants are self-contained rather than reused from <c>ConsoleTheme</c>
/// (which lives in Aiyara.Harness.Cli): this sits in Models, which both Cli and Tools depend on,
/// while Cli/Tools cannot depend on each other's styling.
/// </summary>
internal static class ConsoleSelect
{
    private const string Reset = "\x1b[0m";
    private const string Dim = "\x1b[2m";
    private const string Orange = "\x1b[33m";
    private const string Gray = "\x1b[90m";

    /// <summary>
    /// Prints "This <paramref name="noun"/> requires approval", <paramref name="detailLines"/>
    /// indented below it, then <paramref name="question"/> followed by <paramref name="options"/>
    /// as a numbered list (each with its description on the line below it) - arrow-key-navigable
    /// (Up/Down, Enter, a digit key, Escape-to-cancel) when a real console is attached, or a plain
    /// typed prompt otherwise (redirected input/output, e.g. scripted or piped runs, where
    /// arrow-key navigation can't work). Returns the chosen option's index; unrecognized typed
    /// input and Escape both resolve to the last option, by convention the declining one.
    /// </summary>
    public static int Prompt(string noun, IReadOnlyList<string> detailLines, string question,
        (string Label, string Description)[] options, int defaultIndex)
    {
        Console.WriteLine();
        Console.WriteLine($"This {noun} requires approval");
        Console.WriteLine();

        foreach (var line in detailLines) Console.WriteLine($"  {line}");
        if (detailLines.Count > 0) Console.WriteLine();

        Console.WriteLine($" {question}");

        var selected = Console.IsInputRedirected || Console.IsOutputRedirected
            ? PromptByTyping(options, defaultIndex)
            : PromptByArrowKeys(options, defaultIndex);

        Console.WriteLine();
        return selected;
    }

    /// <summary>
    /// Fallback for non-interactive sessions where arrow keys and live cursor redraws don't work -
    /// shows the options (each with its description) with a static "❯" marking
    /// <paramref name="defaultIndex"/>, and reads a plain number.
    /// </summary>
    private static int PromptByTyping((string Label, string Description)[] options, int defaultIndex)
    {
        for (var i = 0; i < options.Length; i++)
        {
            var marker = i == defaultIndex ? $" {Orange}❯{Reset} " : "   ";
            Console.WriteLine($"{marker}{i + 1}. {options[i].Label}");
            Console.WriteLine($"     {Dim}{Gray}{options[i].Description}{Reset}");
        }
        Console.Write("> ");

        var input = Console.ReadLine()?.Trim();
        return int.TryParse(input, out var n) && n >= 1 && n <= options.Length
            ? n - 1
            : options.Length - 1;
    }

    /// <summary>
    /// Interactive selection: Up/Down move the highlighted option, Enter confirms whichever is
    /// highlighted, a digit key jumps to and immediately confirms that option, Escape cancels
    /// (the last option, by convention the declining one) - advertised via a dim "Esc to cancel"
    /// hint below the list. Reserves every row it'll need via WriteLine up front so later
    /// SetCursorPosition calls always target rows that already exist in the console buffer - the
    /// fix SlashInputReader needed for terminals where BufferHeight == WindowHeight (no
    /// scrollback), where SetCursorPosition alone never scrolls. Hides the blinking text cursor for
    /// the duration - the "❯" marker is the only cursor that should be visible here - and always
    /// restores it afterward, even if something above throws.
    /// </summary>
    private static int PromptByArrowKeys((string Label, string Description)[] options, int defaultIndex)
    {
        var optionRows = options.Length * 2; // a label line + a description line per option
        var totalRows = optionRows + 2; // + a blank line + the "Esc to cancel" hint

        for (var i = 0; i < totalRows; i++) Console.WriteLine();
        var firstRow = Console.CursorTop - totalRows;

        var selected = defaultIndex;
        RenderOptions(firstRow, options, selected);
        RenderEscHint(firstRow + optionRows + 1);

        SetCursorVisible(false);
        try
        {
            while (true)
            {
                var key = Console.ReadKey(intercept: true);
                switch (key.Key)
                {
                    case ConsoleKey.UpArrow:
                        selected = (selected - 1 + options.Length) % options.Length;
                        RenderOptions(firstRow, options, selected);
                        break;
                    case ConsoleKey.DownArrow:
                        selected = (selected + 1) % options.Length;
                        RenderOptions(firstRow, options, selected);
                        break;
                    case ConsoleKey.Enter:
                        return Finish(selected);
                    case ConsoleKey.Escape:
                        return Finish(options.Length - 1);
                    default:
                        if (key.KeyChar is >= '1' and <= '9' && key.KeyChar - '1' < options.Length)
                            return Finish(key.KeyChar - '1');
                        break;
                }
            }

            int Finish(int index)
            {
                RenderOptions(firstRow, options, index);
                Console.SetCursorPosition(0, firstRow + totalRows);
                return index;
            }
        }
        finally
        {
            SetCursorVisible(true);
        }
    }

    private static void RenderOptions(int firstRow, (string Label, string Description)[] options, int selected)
    {
        for (var i = 0; i < options.Length; i++)
        {
            var labelRow = firstRow + i * 2;

            Console.SetCursorPosition(0, labelRow);
            var marker = i == selected ? $" {Orange}❯{Reset} " : "   ";
            Console.Write($"{marker}{i + 1}. {options[i].Label}");

            Console.SetCursorPosition(0, labelRow + 1);
            Console.Write($"     {Dim}{Gray}{options[i].Description}{Reset}");
        }
    }

    private static void RenderEscHint(int row)
    {
        Console.SetCursorPosition(0, row);
        Console.Write($" {Dim}{Gray}Esc to cancel{Reset}");
    }

    // Console.CursorVisible can throw on some terminals/platforms even when a real console is
    // attached; this is a cosmetic touch, so swallow that rather than let it take the prompt down.
    private static void SetCursorVisible(bool visible)
    {
        try
        {
            Console.CursorVisible = visible;
        }
        catch (Exception ex) when (ex is PlatformNotSupportedException or IOException)
        {
        }
    }
}
