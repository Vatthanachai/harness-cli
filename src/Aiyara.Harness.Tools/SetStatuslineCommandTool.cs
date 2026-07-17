using Aiyara.Harness.Models.Config;

using OllamaSharp.Models.Chat;

namespace Aiyara.Harness.Tools;

/// <summary>
/// Lets the model configure the harness's custom status line directly, instead of needing to
/// know statusline.json's location or schema and edit it via <c>write_file</c>.
/// </summary>
public class SetStatuslineCommandTool : BaseTool
{
    public SetStatuslineCommandTool()
    {
        Type = "function";
        Function = new Function
        {
            Name = "set_statusline_command",
            Description = "Sets the shell command the harness runs each turn to build its status line " +
                          "(shown under the input box). The command receives a JSON object on stdin - " +
                          "{Model, ToolsEnabled, ToolsTotal, Cwd} - and its first line of stdout becomes " +
                          "the status text. Pass an empty string to clear it and restore the built-in " +
                          "'model: ... - tools x/y' text.",
            Parameters = new Parameters
            {
                Properties = new Dictionary<string, Property>
                {
                    {
                        "command",
                        new Property { Type = "string", Description = "Shell command to run, or \"\" to clear it." }
                    }
                },
                Required = new List<string> { "command" }
            }
        };
    }

    /// <summary>
    /// Invokes the method to set the statusline command.
    /// </summary>
    /// <param name="args"></param>
    /// <returns></returns>
    protected override object? Execute(IDictionary<string, object?>? args) =>
        SetCommand(args?.TryGetValue("command", out var command) == true ? (string?)command : null);

    /// <summary>
    /// Saves <paramref name="command"/> to statusline.json, taking effect on the harness's very next
    /// prompt since it reloads that file fresh each turn.
    /// </summary>
    /// <param name="command"></param>
    /// <returns></returns>
    public static string SetCommand(string? command)
    {
        UserConfigStore.Save("statusline.json", new StatuslineOptions { Command = command ?? "" });

        return string.IsNullOrWhiteSpace(command)
            ? "Statusline command cleared; the default 'model: ... - tools x/y' text will show again next turn."
            : $"Statusline command set to: {command}";
    }
}
