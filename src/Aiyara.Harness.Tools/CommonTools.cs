using System.Globalization;

using OllamaSharp;

namespace Harness.Tools;

/// <summary>
/// Common tool for the harness CLI.
/// </summary>
public class CommonTools
{
    /// <summary>
    /// Gets the current date.
    /// </summary>
    /// <returns></returns>
    [OllamaTool]
    public static string GetCurrentDate()
    {
        CultureInfo culture = new CultureInfo("en-TH");
        var currentDate = DateTime.Now;

        return currentDate.ToString("D", culture);
    }

    /// <summary>
    /// Gets the current time.
    /// </summary>
    /// <returns></returns>
    [OllamaTool]
    public static string GetCurrentTime()
    {
        CultureInfo culture = new CultureInfo("en-TH");
        var currentDate = DateTime.Now;

        return currentDate.ToString("T", culture);
    }

    /// <summary>
    /// Get the current weather for a city
    /// </summary>
    /// <param name="city">Name of the city</param>
    /// <param name="unit">Temperature unit for the weather</param>
    [OllamaTool]
    public static string GetWeather(string city, Models.Enums.Unit unit = Models.Enums.Unit.Celsius) => $"It's cold at only 6° {unit} in {city}.";
}