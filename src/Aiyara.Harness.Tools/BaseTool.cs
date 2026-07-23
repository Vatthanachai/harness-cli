using OllamaSharp.Models.Chat;
using OllamaSharp.Tools;

using Serilog;

namespace Aiyara.Harness.Tools;

/// <summary>
/// BaseTool class that implements the IInvokableTool interface.
/// </summary>
public class BaseTool : Tool, IInvokableTool
{
    /// <summary>
    /// Invokes <see cref="Execute"/> and catches any exception it throws, turning it into an error string.
    /// OllamaSharp's tool invoker calls this method directly with no exception handling of its own, so an
    /// unhandled exception here would otherwise bubble all the way up and crash the whole chat session
    /// instead of being reported back to the model as a tool result.
    /// </summary>
    /// <param name="args"></param>
    /// <returns></returns>
    public object? InvokeMethod(IDictionary<string, object?>? args)
    {
        try
        {
            return Execute(args);
        }
        catch (OperationCanceledException)
        {
            // The user hitting Esc/Ctrl+C mid-call (see ToolCancellation), not a tool failure - logged
            // at Information rather than the Warning below so an interrupted session doesn't read like
            // an error in harness-*.log.
            Log.Information("Tool '{Tool}' was interrupted by the user", Function?.Name);
            return "Error: interrupted by the user.";
        }
        catch (Exception ex)
        {
            // The tool-result string alone (what the model sees, and what ChatSession's generic
            // OnToolResult log line records) loses the exception's type and stack trace - logged here,
            // in the one place every tool's exception already passes through, so a failure is
            // diagnosable from the log file without having to reproduce it live.
            Log.Warning(ex, "Tool '{Tool}' threw", Function?.Name);
            return $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// Runs the tool's logic. This method is intended to be overridden in derived classes.
    /// </summary>
    /// <param name="args"></param>
    /// <returns></returns>
    /// <exception cref="NotImplementedException"></exception>
    protected virtual object? Execute(IDictionary<string, object?>? args)
    {
        throw new NotImplementedException();
    }
}