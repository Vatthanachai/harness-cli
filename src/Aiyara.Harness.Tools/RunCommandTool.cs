using System.Diagnostics;
using System.Text;

using Aiyara.Harness.Models.Config;

using OllamaSharp.Models.Chat;

using Serilog;

namespace Aiyara.Harness.Tools;

/// <summary>
/// Lets the model run an external shell command (e.g. <c>dotnet build</c>, <c>npm install</c>).
/// Confirmed via <see cref="ActionConsent"/>'s "allow once / allow for this session / no" prompt,
/// same as the file-writing tools - but keyed by the exact command line and working directory
/// (not a single broad "run_command" key), so approving "dotnet build" for the session doesn't
/// also silently approve some other, unreviewed command later in the same run.
/// </summary>
public class RunCommandTool : BaseTool
{
    private const int DefaultTimeoutSeconds = 120;

    public RunCommandTool()
    {
        Type = "function";
        Function = new Function
        {
            Name = "run_command",
            Description = "Runs a shell command (e.g. 'dotnet build', 'npm install') and returns its " +
                          "stdout, stderr and exit code. The user is asked to approve it first - allow once, " +
                          "allow for this session (remembered per exact command + directory), or no.",
            Parameters = new Parameters
            {
                Properties = new Dictionary<string, Property>
                {
                    {
                        "command",
                        new Property { Type = "string", Description = "The full command line to run, e.g. 'dotnet build'." }
                    },
                    {
                        "working_directory",
                        new Property
                        {
                            Type = "string",
                            Description = "Directory to run the command in. Defaults to the workspace root if omitted."
                        }
                    },
                    {
                        "timeout_seconds",
                        new Property
                        {
                            Type = "number",
                            Description = $"Seconds to wait before killing the process. Defaults to {DefaultTimeoutSeconds}."
                        }
                    }
                },
                Required = new List<string> { "command" }
            }
        };
    }

    protected override object? Execute(IDictionary<string, object?>? args)
    {
        var command = args?.TryGetValue("command", out var c) == true ? c?.ToString() : null;
        var workingDirectory = args?.TryGetValue("working_directory", out var wd) == true ? wd?.ToString() : null;
        var timeoutSeconds = args?.TryGetValue("timeout_seconds", out var t) == true && t is not null
            ? Convert.ToInt32(t)
            : DefaultTimeoutSeconds;

        return RunCommandAsync(command, workingDirectory, timeoutSeconds, ToolCancellation.Current).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Asks the user to approve <paramref name="command"/>, then - only if approved - runs it via
    /// the platform shell and returns its combined stdout/stderr output along with its exit code.
    /// Cancelling <paramref name="ct"/> (the user's Esc/Ctrl+C, via <see cref="ToolCancellation"/>)
    /// kills the process tree just like a timeout does, but is reported back distinctly so the model
    /// - and the log - can tell "the user gave up on this" from "this command is genuinely slow".
    /// </summary>
    /// <exception cref="ArgumentException"></exception>
    /// <exception cref="UnauthorizedAccessException"></exception>
    public static async Task<string> RunCommandAsync(
        string? command, string? workingDirectory, int timeoutSeconds = DefaultTimeoutSeconds, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            throw new ArgumentException("Command cannot be null or empty.", nameof(command));
        }

        var resolvedDirectory = string.IsNullOrWhiteSpace(workingDirectory)
            ? Workspace.Root
            : Workspace.ResolvePath(workingDirectory);

        var approved = ActionConsent.Confirm(
            $"run_command:{resolvedDirectory}:{command}",
            "command",
            $"{command} (in: {resolvedDirectory})");

        if (!approved)
        {
            Log.Warning("run_command denied by user: {Command} (in {Directory})", command, resolvedDirectory);
            throw new UnauthorizedAccessException($"Running '{command}' was not approved.");
        }

        var startInfo = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe", ["/c", command])
            : new ProcessStartInfo("/bin/sh", ["-c", command]);

        startInfo.WorkingDirectory = resolvedDirectory;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;

        using var process = new Process { StartInfo = startInfo };

        var output = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };

        Log.Information("run_command: {Command} (in {Directory})", command, resolvedDirectory);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);

            if (ct.IsCancellationRequested)
            {
                Log.Information("run_command interrupted by user and killed: {Command}", command);
                return $"Interrupted by user and killed. Output so far:\n{output}";
            }

            Log.Warning("run_command timed out after {TimeoutSeconds}s and was killed: {Command}", timeoutSeconds, command);
            return $"Command timed out after {timeoutSeconds}s and was killed. Output so far:\n{output}";
        }

        Log.Information("run_command finished: exit {ExitCode}: {Command}", process.ExitCode, command);
        return $"Exit code: {process.ExitCode}\n{output}";
    }
}
