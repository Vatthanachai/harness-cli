namespace Aiyara.Harness.Cli.Commands;

/// <summary>
/// Holds the set of registered slash commands and resolves names or prefixes typed at the chat prompt.
/// </summary>
public sealed class SlashCommandRegistry
{
    /// <summary>
    /// All registered commands, sorted by name.
    /// </summary>
    public IReadOnlyList<SlashCommand> All { get; }

    /// <summary>
    /// Registers <paramref name="commands"/>, sorted by name.
    /// </summary>
    public SlashCommandRegistry(IEnumerable<SlashCommand> commands) =>
        All = commands.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>
    /// Finds the command whose name exactly matches <paramref name="name"/> (case-insensitive).
    /// </summary>
    public SlashCommand? Find(string name) =>
        All.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Returns every command whose name starts with <paramref name="prefix"/> (case-insensitive).
    /// An empty prefix matches every command.
    /// </summary>
    public IEnumerable<SlashCommand> Match(string prefix) =>
        All.Where(c => c.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
}
