using System.Globalization;

using OllamaSharp.Models.Chat;

namespace Aiyara.Harness.Tools;

/// <summary>
/// DateTime tool for the harness CLI.
/// </summary>
public class DateTimeTool : BaseTool
{
    /// <summary>
    /// constructor for the DateTimeTool class.
    /// </summary>
    public DateTimeTool()
    {
        Type = "function";
        Function = new Function
        {
            Name = "get_current_datetime",
            Description = "Get the current date and time in a specific format.",
            Parameters = new Parameters { Properties = new Dictionary<string, Property>(), Required = [] }
        };
    }

    /// <summary>
    /// InvokableTool implementation to invoke the method to get the current date and time.
    /// </summary>
    /// <param name="args">
    /// Unused arguments for this tool, as it does not require any parameters.
    /// </param>
    /// <returns></returns>
    protected override object? Execute(IDictionary<string, object?>? args) => GetCurrentDateTime();

    /// <summary>
    /// Gets the current date and time in a specific format.
    /// </summary>
    /// <returns></returns>
    public static string GetCurrentDateTime()
    {
        CultureInfo culture = new CultureInfo("en-TH");
        return DateTime.Now.ToString("O", culture);
    }
}