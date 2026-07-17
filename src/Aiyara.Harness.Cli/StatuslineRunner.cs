using System.Diagnostics;
using System.Text;
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

        // UTF8Encoding(false) (no BOM) rather than Encoding.UTF8, since a leading BOM would land
        // inside the first line of output and corrupt the status text.
        var utf8 = new UTF8Encoding(false);

        var startInfo = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "powershell.exe" : "/bin/sh",
            // Without this, the command inherits the harness process's own working directory -
            // which only happens to match the workspace when the harness was launched from inside
            // it with no explicit path argument. A `git`/`pwd`-based command would otherwise read
            // the wrong repo whenever the two differ.
            WorkingDirectory = context.Cwd,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = utf8,
            StandardOutputEncoding = utf8,
            StandardErrorEncoding = utf8
        };

        if (OperatingSystem.IsWindows())
        {
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-Command");
            // A freshly spawned, redirected-output cmd.exe/PowerShell process starts on the
            // system's legacy ANSI codepage (e.g. 874 on a Thai-locale machine), not UTF-8 - any
            // non-ASCII text a custom command writes (or embeds, like Thai labels here) gets
            // mangled into replacement characters once .NET decodes it as UTF-8 on the other end.
            // Forcing the encoding as the first statement, before the user's command runs, is what
            // actually fixes it - a same-line "chcp 65001 &&" prefix does NOT work, because cmd.exe
            // parses/tokenizes the whole /c argument under the OLD codepage before executing
            // anything in it. PowerShell also gives $(...) command-substitution syntax, matching
            // what users typically write (and copy from bash/zsh examples) for a dynamic statusline.
            startInfo.ArgumentList.Add($"[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false); {command}");
        }
        else
        {
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add(command);
        }

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
