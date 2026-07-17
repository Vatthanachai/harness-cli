using OllamaSharp.Models.Chat;
using OllamaSharp.Tools;

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
        catch (Exception ex)
        {
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