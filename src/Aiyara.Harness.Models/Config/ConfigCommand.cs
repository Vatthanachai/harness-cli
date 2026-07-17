using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Aiyara.Harness.Models.Config;

/// <summary>
/// Implements the <c>harness config</c> CLI commands for viewing and editing the JSON files under
/// <see cref="UserConfigPaths.Directory"/>.
/// </summary>
public static class ConfigCommand
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly Dictionary<string, Func<string>> Categories = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ollama"] = () => Seed("ollama.json", new OllamaConnectionOptions()),
        ["models"] = () => Seed("models.json", new ModelsOptions()),
        ["mcp"] = () => Seed("mcp.json", new McpOptions()),
        ["rag"] = () => Seed("rag.json", new RagOptions()),
        ["statusline"] = () => Seed("statusline.json", new StatuslineOptions()),
        ["trust"] = () => Seed("trust.json", new TrustOptions()),
        ["logging"] = () => Seed("logging.json", new LoggingOptions())
    };

    /// <summary>
    /// Runs a <c>config</c> subcommand (<c>path</c>, <c>show</c>, <c>set</c> or <c>edit</c>) and returns the process
    /// exit code.
    /// </summary>
    public static int Run(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        return args[0] switch
        {
            "path" => Path_(),
            "show" => Show(args.Length > 1 ? args[1] : null),
            "set" => Set(args[1..]),
            "edit" => Edit(args.Length > 1 ? args[1] : null),
            _ => Unknown()
        };
    }

    private static int Path_()
    {
        Console.WriteLine(UserConfigPaths.Directory);
        return 0;
    }

    private static int Show(string? category)
    {
        if (category is null)
        {
            foreach (var name in Categories.Keys)
                PrintCategory(name);
            return 0;
        }

        if (!Categories.ContainsKey(category))
            return UnknownCategory(category);

        PrintCategory(category);
        return 0;
    }

    private static int Set(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Usage: harness config set <ollama|models|mcp|rag|statusline|trust> <key> <value>");
            return 1;
        }

        var category = args[0];
        var key = args[1];
        var value = string.Join(' ', args[2..]);

        if (!Categories.TryGetValue(category, out var seed))
            return UnknownCategory(category);

        var path = seed();
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();

        var existingKey = root.Select(p => p.Key).FirstOrDefault(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
        if (existingKey is null)
        {
            Console.Error.WriteLine($"'{category}' has no key '{key}'. Existing keys: {string.Join(", ", root.Select(p => p.Key))}");
            return 1;
        }

        root[existingKey] = ParseValue(value);
        File.WriteAllText(path, root.ToJsonString(WriteOptions));

        Console.WriteLine($"{category}.{existingKey} = {value}");
        return 0;
    }

    private static int Edit(string? category)
    {
        if (category is null || !Categories.TryGetValue(category, out var seed))
        {
            Console.Error.WriteLine("Usage: harness config edit <ollama|models|mcp|rag>");
            return category is null ? 1 : UnknownCategory(category);
        }

        var path = seed();
        using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        process?.WaitForExit();
        return 0;
    }

    private static void PrintCategory(string category)
    {
        var path = Categories[category]();
        Console.WriteLine($"# {category} ({path})");
        Console.WriteLine(File.ReadAllText(path));
        Console.WriteLine();
    }

    private static string Seed<T>(string fileName, T defaults) where T : class
    {
        UserConfigStore.Load(fileName, defaults);
        return System.IO.Path.Combine(UserConfigPaths.Directory, fileName);
    }

    private static JsonNode ParseValue(string value) =>
        value switch
        {
            "true" => JsonValue.Create(true),
            "false" => JsonValue.Create(false),
            _ when int.TryParse(value, out var i) => JsonValue.Create(i),
            _ when double.TryParse(value, out var d) => JsonValue.Create(d),
            _ => JsonValue.Create(value)
        };

    private static int UnknownCategory(string category)
    {
        Console.Error.WriteLine($"Unknown config category '{category}'. Expected one of: {string.Join(", ", Categories.Keys)}");
        return 1;
    }

    private static int Unknown()
    {
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            Usage: harness config <command>

              config path                          Show the config directory location.
              config show [category]               Print current config (all categories, or one).
              config set <category> <key> <value>  Change a single value.
              config edit <category>                Open a category's JSON file in your default editor.

            Categories: ollama, models, mcp, rag, statusline, trust, logging
            """);
    }
}
