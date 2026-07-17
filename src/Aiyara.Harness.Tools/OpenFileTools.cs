using Aiyara.Harness.Models.Config;

using OllamaSharp.Models.Chat;

namespace Aiyara.Harness.Tools;

/// <summary>
/// A tool for opening files.
/// </summary>
public class OpenFileTools : BaseTool
{
    public OpenFileTools()
    {
        Type = "function";
        Function = new Function
        {
            Name = "open_file",
            Description = "Open a file and return its contents.",
            Parameters = new Parameters
            {
                Properties = new Dictionary<string, Property>
                {
                    {
                        "file_path",
                        new Property { Type = "string", Description = "The path to the file to open." }
                    }
                },
                Required = new List<string> { "file_path" }
            }
        };
    }

    /// <summary>
    /// Invokes the method to open a file and return its contents as text.
    /// </summary>
    /// <param name="args"></param>
    /// <returns></returns>
    protected override object? Execute(IDictionary<string, object?>? args) =>
        OpenFile(args?.TryGetValue("file_path", out var value) == true ? (string?)value : null);

    /// <summary>
    /// Opens a file and returns its contents as text so the model can read them directly.
    /// </summary>
    /// <param name="filePath"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    /// <exception cref="FileNotFoundException"></exception>
    public static string OpenFile(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));
        }

        var resolvedPath = Workspace.ResolvePath(filePath);

        if (!File.Exists(resolvedPath))
        {
            throw new FileNotFoundException($"The file '{filePath}' does not exist.");
        }

        return File.ReadAllText(resolvedPath);
    }
}