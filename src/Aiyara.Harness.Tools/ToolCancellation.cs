namespace Aiyara.Harness.Tools;

/// <summary>
/// Ambient cancellation token for whichever tool call is currently running. Exists because
/// OllamaSharp's <c>IInvokableTool.InvokeMethod(args)</c> - the method it actually calls mid-turn -
/// takes no <see cref="CancellationToken"/> of its own and isn't ours to change, so a per-turn token
/// (e.g. <c>ChatSession</c>'s Esc/Ctrl+C-triggered one) can't be threaded through it as a normal
/// parameter. <see cref="AsyncLocal{T}"/> flows across the await boundaries inside
/// <c>chat.SendAsync</c> instead, reaching <see cref="BaseTool.Execute"/> without needing
/// OllamaSharp's cooperation.
/// </summary>
public static class ToolCancellation
{
    private static readonly AsyncLocal<CancellationToken> Ambient = new();

    /// <summary>
    /// The token for whichever turn is currently running, or <see cref="CancellationToken.None"/>
    /// outside of one (e.g. a tool invoked directly, as in a unit test).
    /// </summary>
    public static CancellationToken Current => Ambient.Value;

    /// <summary>
    /// Makes <paramref name="token"/> <see cref="Current"/> for the scope of the returned
    /// <see cref="IDisposable"/> - wrap the <c>await foreach (chat.SendAsync(...))</c> loop that
    /// drives one turn in it.
    /// </summary>
    public static IDisposable Scope(CancellationToken token)
    {
        var previous = Ambient.Value;
        Ambient.Value = token;
        return new Restorer(previous);
    }

    private sealed class Restorer(CancellationToken previous) : IDisposable
    {
        public void Dispose() => Ambient.Value = previous;
    }
}
