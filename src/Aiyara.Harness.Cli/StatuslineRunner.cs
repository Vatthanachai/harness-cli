using System.Diagnostics;
using System.Text.Json;

using Serilog;

namespace Aiyara.Harness.Cli;

/// <summary>
/// Info about the current session, serialized to JSON and piped to a custom statusline command's
/// stdin - the same shape a "model: ..." status line would need to render itself.
/// </summary>
internal sealed record StatuslineContext(string Model, int ToolsEnabled, int ToolsTotal, string Cwd);

/// <summary>
/// Runs the user-configured statusline command (see <c>statusline.json</c>, <c>/config set
/// statusline Command "..."</c>) and captures its output, the same way Claude Code's own
/// customizable status line works.
/// </summary>
internal static class StatuslineRunner
{
    /// <summary>
    /// Runs <paramref name="command"/> through the platform shell, feeding <paramref name="context"/>
    /// as JSON on stdin, and returns the trimmed first line of its stdout. Returns null - meaning
    /// "fall back to the default text" - if the command is empty, fails, times out, or exits
    /// non-zero.
    /// </summary>
    public static async Task<string?> RunAsync(string command, StatuslineContext context, TimeSpan timeout)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;

        var startInfo = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(OperatingSystem.IsWindows() ? "/c" : "-c");
        startInfo.ArgumentList.Add(command);

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null) return null;

            await process.StandardInput.WriteAsync(JsonSerializer.Serialize(context));
            process.StandardInput.Close();

            // Start draining stdout before/while waiting so a chatty command can't deadlock on a
            // full pipe buffer while we're blocked in WaitForExitAsync.
            var stdoutTask = process.StandardOutput.ReadToEndAsync();

            using var cts = new CancellationTokenSource(timeout);
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                Log.Warning("Statusline command timed out after {Timeout}: {Command}", timeout, command);
                return null;
            }

            if (process.ExitCode != 0)
            {
                Log.Warning("Statusline command exited with code {Code}: {Command}", process.ExitCode, command);
                return null;
            }

            var firstLine = (await stdoutTask).Split('\n')[0].TrimEnd('\r').Trim();
            return firstLine.Length == 0 ? null : firstLine;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Statusline command failed: {Command}", command);
            return null;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best-effort cleanup after a timeout; nothing more to do if this fails too.
        }
    }
}
