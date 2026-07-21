namespace Aiyara.Harness.Tools.Providers;

/// <summary>
/// Builds a fresh, independent <see cref="IChatEngine"/> against whichever provider is active for
/// this session - the same construction <c>Program.cs</c> does once for the primary conversation,
/// factored out so other callers (namely <see cref="DispatchAgentTool"/>, spinning up an isolated
/// sub-agent) can build their own without duplicating provider-specific setup or opening new
/// connections.
/// </summary>
public interface IChatEngineFactory
{
    /// <summary>
    /// Creates a new <see cref="IChatEngine"/> with its own message history, using <paramref name="model"/>
    /// and <paramref name="systemPrompt"/> - entirely independent of any other engine this factory
    /// has created, including the primary conversation's.
    /// </summary>
    IChatEngine Create(string model, string systemPrompt);
}
