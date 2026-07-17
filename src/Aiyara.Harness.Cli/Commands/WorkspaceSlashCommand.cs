using Aiyara.Harness.Models.Config;

namespace Aiyara.Harness.Cli.Commands;

/// <summary>
/// "/workspace" — shows the folder the harness's file tools are confined to for this run.
/// </summary>
public static class WorkspaceSlashCommand
{
    public static SlashCommand Create() => new(
        "workspace",
        "Show the folder file tools are confined to",
        (_, _) =>
        {
            var docStatuses = new[]
            {
                (AiyaraDocument.FileName, AiyaraDocument.PathAtWorkspaceRoot),
                (FilesDocument.FileName, FilesDocument.PathAtWorkspaceRoot),
                (ToolsDocument.FileName, ToolsDocument.PathAtWorkspaceRoot),
                (CommandsDocument.FileName, CommandsDocument.PathAtWorkspaceRoot),
                (MemoryDocument.FileName, MemoryDocument.PathAtWorkspaceRoot)
            }.Select(d => File.Exists(d.Item2)
                ? $"{d.Item1}: loaded from {d.Item2}"
                : $"{d.Item1}: none (not found at workspace root)");

            ConsoleTheme.WriteSlashResult(
                $"Workspace: {Workspace.Root}\n{string.Join('\n', docStatuses)}");
            return Task.CompletedTask;
        });
}
