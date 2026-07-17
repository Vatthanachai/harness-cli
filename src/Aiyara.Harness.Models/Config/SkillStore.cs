namespace Aiyara.Harness.Models.Config;

/// <summary>
/// One entry from <see cref="SkillStore.List"/>: a skill's name (from its frontmatter, falling
/// back to its folder name), one-line description, and the absolute path to its SKILL.md.
/// </summary>
public sealed record SkillInfo(string Name, string Description, string Path);

/// <summary>
/// Reads, writes, deletes and lists skills under <c>.aiyara/skills/&lt;name&gt;/SKILL.md</c> in the
/// workspace - this harness's equivalent of Claude Code's own Skill system. Each SKILL.md starts
/// with a small frontmatter block (<c>name</c>, <c>description</c>) followed by the skill's actual
/// instructions as Markdown, the same shape Claude Code's own skills use.
/// </summary>
public static class SkillStore
{
    private const string FrontmatterDelimiter = "---";

    public static string SkillsRoot => Path.Combine(Workspace.Root, ".aiyara", "skills");

    /// <summary>
    /// The absolute path <c>SKILL.md</c> would have for <paramref name="name"/>, after normalizing
    /// it to a filesystem-safe slug (letters/digits only, hyphen-separated) - this also rules out
    /// path traversal via a crafted name, since the slug can never contain "/", "\" or "..".
    /// </summary>
    public static string SkillFilePath(string name) => Path.Combine(SkillsRoot, NormalizeName(name), "SKILL.md");

    public static bool Exists(string name) => File.Exists(SkillFilePath(name));

    /// <summary>
    /// Every skill currently on disk, sorted by name. Cheap to call repeatedly - it always reflects
    /// the current filesystem state rather than a cached snapshot, since skills can be created or
    /// deleted mid-session.
    /// </summary>
    public static IReadOnlyList<SkillInfo> List()
    {
        if (!Directory.Exists(SkillsRoot)) return [];

        var skills = new List<SkillInfo>();
        foreach (var dir in Directory.EnumerateDirectories(SkillsRoot))
        {
            var skillFile = Path.Combine(dir, "SKILL.md");
            if (!File.Exists(skillFile)) continue;

            var (name, description, _) = ParseFrontmatter(File.ReadAllText(skillFile));
            skills.Add(new SkillInfo(name ?? Path.GetFileName(dir), description ?? "", skillFile));
        }

        return skills.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// The skill's instructions (the content after its frontmatter block), or null if it doesn't exist.
    /// </summary>
    public static string? LoadContent(string name)
    {
        var path = SkillFilePath(name);
        if (!File.Exists(path)) return null;

        var (_, _, content) = ParseFrontmatter(File.ReadAllText(path));
        return content;
    }

    /// <summary>
    /// Creates or overwrites the named skill's SKILL.md with a frontmatter block plus
    /// <paramref name="content"/>, creating its folder if needed.
    /// </summary>
    public static void Save(string name, string description, string content)
    {
        var path = SkillFilePath(name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var normalized = NormalizeName(name);
        var text =
            $"{FrontmatterDelimiter}{Environment.NewLine}" +
            $"name: {normalized}{Environment.NewLine}" +
            $"description: {description}{Environment.NewLine}" +
            $"{FrontmatterDelimiter}{Environment.NewLine}{Environment.NewLine}{content}";

        File.WriteAllText(path, text);
    }

    /// <summary>
    /// Deletes the named skill's whole folder. Returns false if it didn't exist.
    /// </summary>
    public static bool Delete(string name)
    {
        var folder = Path.GetDirectoryName(SkillFilePath(name))!;
        if (!Directory.Exists(folder)) return false;

        Directory.Delete(folder, recursive: true);
        return true;
    }

    /// <summary>
    /// Reduces <paramref name="name"/> to a lowercase, hyphen-separated slug of letters and digits
    /// only, so it's always safe to use as a single path segment.
    /// </summary>
    /// <exception cref="ArgumentException"></exception>
    private static string NormalizeName(string name)
    {
        var slugged = new string(name.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray());

        while (slugged.Contains("--", StringComparison.Ordinal))
            slugged = slugged.Replace("--", "-");

        slugged = slugged.Trim('-');

        if (string.IsNullOrEmpty(slugged))
            throw new ArgumentException("Skill name must contain at least one letter or digit.", nameof(name));

        return slugged;
    }

    private static (string? Name, string? Description, string Content) ParseFrontmatter(string text)
    {
        if (!text.StartsWith(FrontmatterDelimiter, StringComparison.Ordinal))
            return (null, null, text);

        var end = text.IndexOf(FrontmatterDelimiter, FrontmatterDelimiter.Length, StringComparison.Ordinal);
        if (end < 0) return (null, null, text);

        var frontmatter = text[FrontmatterDelimiter.Length..end];
        var body = text[(end + FrontmatterDelimiter.Length)..].TrimStart('\r', '\n');

        string? name = null;
        string? description = null;
        foreach (var line in frontmatter.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("name:", StringComparison.OrdinalIgnoreCase))
                name = trimmed["name:".Length..].Trim();
            else if (trimmed.StartsWith("description:", StringComparison.OrdinalIgnoreCase))
                description = trimmed["description:".Length..].Trim();
        }

        return (name, description, body);
    }
}
