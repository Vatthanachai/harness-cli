using Aiyara.Harness.Models.Config;

using OllamaSharp.Models.Chat;

namespace Aiyara.Harness.Tools;

/// <summary>
/// A tool for writing (creating or overwriting) text files.
/// </summary>
public class WriteFileTool : BaseTool
{
    public WriteFileTool()
    {
        Type = "function";
        Function = new Function
        {
            Name = "write_file",
            Description = "Write text content to a file, creating it if it does not exist and overwriting it if it does.",
            Parameters = new Parameters
            {
                Properties = new Dictionary<string, Property>
                {
                    {
                        "file_path",
                        new Property { Type = "string", Description = "The path of the file to write." }
                    },
                    {
                        "content",
                        new Property { Type = "string", Description = "The text content to write to the file." }
                    }
                },
                Required = new List<string> { "file_path", "content" }
            }
        };
    }

    /// <summary>
    /// Invokes the method to write content to a file.
    /// </summary>
    /// <param name="args"></param>
    /// <returns></returns>
    protected override object? Execute(IDictionary<string, object?>? args) =>
        WriteFile(
            args?.TryGetValue("file_path", out var filePath) == true ? (string?)filePath : null,
            args?.TryGetValue("content", out var content) == true ? (string?)content : null);

    /// <summary>
    /// Writes text content to a file, creating any missing parent directories, and returns a confirmation
    /// message so the model knows the write succeeded.
    /// </summary>
    /// <param name="filePath"></param>
    /// <param name="content"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    /// <exception cref="UnauthorizedAccessException"></exception>
    public static string WriteFile(string? filePath, string? content)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));
        }

        var resolvedPath = Workspace.ResolvePath(filePath);
        var overwriting = File.Exists(resolvedPath);

        var approved = ActionConsent.Confirm("write_file",
            "file write",
            $"{(overwriting ? "overwrite" : "create")} {resolvedPath}");

        if (!approved)
        {
            throw new UnauthorizedAccessException($"Writing to '{filePath}' was not approved.");
        }

        var directory = Path.GetDirectoryName(resolvedPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(resolvedPath, content ?? string.Empty);

        return $"Wrote {(content ?? string.Empty).Length} characters to '{filePath}'.";
    }
}
