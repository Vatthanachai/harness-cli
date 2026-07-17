using System.Text.Encodings.Web;
using System.Text.Json;

namespace Aiyara.Harness.Models.Config;

/// <summary>
/// Loads a category of user-editable configuration from its own JSON file under <see cref="UserConfigPaths.Directory"/>,
/// seeding the file with defaults the first time it is requested.
/// </summary>
public static class UserConfigStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Reads <paramref name="fileName"/> from the user config directory, creating it with <paramref name="defaults"/>
    /// if it does not already exist. If the file predates fields since added to <typeparamref name="T"/>, it is
    /// rewritten so those fields (with their default values) are present on disk too.
    /// </summary>
    public static T Load<T>(string fileName, T defaults) where T : class
    {
        var path = ResolvePath(fileName);

        if (!File.Exists(path))
        {
            Save(fileName, defaults);
            return defaults;
        }

        var json = File.ReadAllText(path);

        T value;
        try
        {
            value = JsonSerializer.Deserialize<T>(json, SerializerOptions) ?? defaults;
        }
        catch (JsonException ex)
        {
            // A hand- or model-edited config file can end up with broken JSON. Falling back to
            // defaults (without touching the file) keeps the harness usable instead of crashing
            // every caller that loads this category until someone notices and fixes it by hand.
            Console.Error.WriteLine($"Warning: '{fileName}' has invalid JSON ({ex.Message}); using defaults instead.");
            return defaults;
        }

        var normalized = JsonSerializer.Serialize(value, SerializerOptions);
        if (normalized != json)
            File.WriteAllText(path, normalized);

        return value;
    }

    /// <summary>
    /// Writes <paramref name="value"/> to <paramref name="fileName"/> in the user config directory,
    /// overwriting whatever is there.
    /// </summary>
    public static void Save<T>(string fileName, T value) where T : class =>
        File.WriteAllText(ResolvePath(fileName), JsonSerializer.Serialize(value, SerializerOptions));

    private static string ResolvePath(string fileName)
    {
        System.IO.Directory.CreateDirectory(UserConfigPaths.Directory);
        return Path.Combine(UserConfigPaths.Directory, fileName);
    }
}
