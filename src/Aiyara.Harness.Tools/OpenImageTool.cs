using Aiyara.Harness.Models.Config;

using OllamaSharp.Models.Chat;

namespace Aiyara.Harness.Tools;

/// <summary>
/// A tool for opening image files.
/// </summary>
public class OpenImageTool : BaseTool
{
    public OpenImageTool()
    {
        Type = "function";
        Function = new Function
        {
            Name = "open_image",
            Description = "Open an image file and return its contents as a Base64-encoded string.",
            Parameters = new Parameters
            {
                Properties = new Dictionary<string, Property>
                {
                    {
                        "file_path",
                        new Property { Type = "string", Description = "The path to the image file to open." }
                    }
                },
                Required = new List<string> { "file_path" }
            }
        };
    }

    /// <summary>
    /// Invokes the method to open an image file and return its contents as a Base64 string.
    /// </summary>
    /// <param name="args"></param>
    /// <returns></returns>
    protected override object? Execute(IDictionary<string, object?>? args) =>
        OpenImage(args?.TryGetValue("file_path", out var value) == true ? (string?)value : null);

    /// <summary>
    /// Opens an image file and returns its contents as a Base64-encoded string so the model can read it directly.
    /// </summary>
    /// <param name="filePath"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    /// <exception cref="FileNotFoundException"></exception>
    public static string OpenImage(string? filePath)
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

        var bytes = File.ReadAllBytes(resolvedPath);
        return Convert.ToBase64String(bytes);
    }
}
