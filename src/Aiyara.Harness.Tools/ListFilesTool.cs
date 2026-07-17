using System.Text;

using Aiyara.Harness.Models.Config;

using OllamaSharp.Models.Chat;

namespace Aiyara.Harness.Tools;

/// <summary>
/// A read-only tool that lists the workspace's file tree, giving the model something OpenFileTools
/// alone can't: an overview of what files exist before reading or writing any one of them. Its main
/// intended use is gathering enough context to write a meaningful <c>FILES.md</c>
/// (see <see cref="FilesDocumentTool"/>), so it needs no user confirmation, same as OpenFileTools.
/// </summary>
public class ListFilesTool : BaseTool
{
    private const int MaxEntries = 1000;

    private static readonly HashSet<string> SkippedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", ".idea", "bin", "obj", "node_modules", "dist", "build", "packages", "__pycache__", ".venv"
    };

    public ListFilesTool()
    {
        Type = "function";
        Function = new Function
        {
            Name = "list_files",
            Description = "Recursively lists files and folders under the workspace (or a subdirectory of it) as " +
                          "an indented tree, skipping common noise directories (.git, bin, obj, node_modules, " +
                          "etc.). Useful for understanding the project layout, e.g. before writing FILES.md.",
            Parameters = new Parameters
            {
                Properties = new Dictionary<string, Property>
                {
                    {
                        "path",
                        new Property
                        {
                            Type = "string",
                            Description = "Subdirectory to list, relative to the workspace root. Defaults to the workspace root itself."
                        }
                    }
                },
                Required = new List<string>()
            }
        };
    }

    protected override object? Execute(IDictionary<string, object?>? args) =>
        ListFiles(args?.TryGetValue("path", out var path) == true ? (string?)path : null);

    /// <summary>
    /// Builds an indented tree of files and folders under <paramref name="path"/> (or the workspace
    /// root if null/empty), skipping <see cref="SkippedDirectoryNames"/> and stopping once
    /// <see cref="MaxEntries"/> entries have been listed.
    /// </summary>
    /// <exception cref="DirectoryNotFoundException"></exception>
    public static string ListFiles(string? path)
    {
        var resolvedRoot = string.IsNullOrWhiteSpace(path) ? Workspace.Root : Workspace.ResolvePath(path);

        if (!Directory.Exists(resolvedRoot))
        {
            throw new DirectoryNotFoundException($"The directory '{path ?? resolvedRoot}' does not exist.");
        }

        var builder = new StringBuilder();
        var count = 0;
        AppendTree(resolvedRoot, "", builder, ref count);

        if (count >= MaxEntries)
        {
            builder.AppendLine($"... truncated after {MaxEntries} entries.");
        }

        return builder.Length == 0 ? "(empty directory)" : builder.ToString();
    }

    private static void AppendTree(string directory, string indent, StringBuilder builder, ref int count)
    {
        IEnumerable<string> subdirectories;
        IEnumerable<string> files;
        try
        {
            subdirectories = Directory.EnumerateDirectories(directory)
                .Where(d => !SkippedDirectoryNames.Contains(Path.GetFileName(d)))
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase);
            files = Directory.EnumerateFiles(directory).OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        foreach (var file in files)
        {
            if (count >= MaxEntries) return;
            builder.AppendLine($"{indent}{Path.GetFileName(file)}");
            count++;
        }

        foreach (var subdirectory in subdirectories)
        {
            if (count >= MaxEntries) return;
            builder.AppendLine($"{indent}{Path.GetFileName(subdirectory)}/");
            count++;
            AppendTree(subdirectory, indent + "  ", builder, ref count);
        }
    }
}
