namespace Aiyara.Harness.Models.Config;

/// <summary>
/// Where a skill lives: <see cref="Workspace"/> (this project only, under its own <c>.aiyara/</c>,
/// meant to be committed and shared via the repo) or <see cref="User"/> (every project, under
/// <c>%USERPROFILE%\.aiyara\</c>, for personal cross-project playbooks).
/// </summary>
public enum SkillScope
{
    Workspace,
    User
}

/// <summary>
/// One entry from <see cref="SkillStore.List"/>: a skill's name (from its frontmatter, falling
/// back to its folder name), one-line description, absolute path to its SKILL.md, and which scope
/// it was loaded from.
/// </summary>
public sealed record SkillInfo(string Name, string Description, string Path, SkillScope Scope);

/// <summary>
/// Reads, writes, deletes and lists skills under <c>.aiyara/skills/&lt;name&gt;/SKILL.md</c> - this
/// harness's equivalent of Claude Code's own Skill system. Two scopes share the same on-disk shape
/// (see <see cref="SkillScope"/>): a <see cref="SkillScope.Workspace"/> skill and a
/// <see cref="SkillScope.User"/> skill with the same name can both exist at once, in which case the
/// workspace one shadows the user one everywhere a single, name-keyed skill is expected (listing,
/// loading, deleting without an explicit scope) - the project-specific copy wins over the personal
/// one. Each SKILL.md starts with a small frontmatter block (<c>name</c>, <c>description</c>)
/// followed by the skill's actual instructions as Markdown, the same shape Claude Code's own skills
/// use.
/// </summary>
public static class SkillStore
{
    private const string FrontmatterDelimiter = "---";

    public static string SkillsRoot => RootFor(SkillScope.Workspace);
    public static string UserSkillsRoot => RootFor(SkillScope.User);

    private static string RootFor(SkillScope scope) => scope switch
    {
        SkillScope.User => Path.Combine(UserConfigPaths.Directory, "skills"),
        _ => Path.Combine(Workspace.Root, ".aiyara", "skills")
    };

    /// <summary>
    /// The absolute path <c>SKILL.md</c> would have for <paramref name="name"/> in
    /// <paramref name="scope"/>, after normalizing it to a filesystem-safe slug (letters/digits
    /// only, hyphen-separated) - this also rules out path traversal via a crafted name, since the
    /// slug can never contain "/", "\" or "..".
    /// </summary>
    public static string SkillFilePath(string name, SkillScope scope = SkillScope.Workspace) =>
        Path.Combine(RootFor(scope), NormalizeName(name), "SKILL.md");

    /// <summary>Whether <paramref name="name"/> exists in <paramref name="scope"/> specifically.</summary>
    public static bool Exists(string name, SkillScope scope) => File.Exists(SkillFilePath(name, scope));

    /// <summary>Whether <paramref name="name"/> exists in either scope.</summary>
    public static bool Exists(string name) => Exists(name, SkillScope.Workspace) || Exists(name, SkillScope.User);

    /// <summary>
    /// The scope a name-only lookup (loading, or deleting without an explicit scope) would resolve
    /// to - workspace if present there, else user, else null if it exists in neither.
    /// </summary>
    public static SkillScope? ResolveScope(string name)
    {
        if (Exists(name, SkillScope.Workspace)) return SkillScope.Workspace;
        if (Exists(name, SkillScope.User)) return SkillScope.User;
        return null;
    }

    /// <summary>
    /// Every skill currently on disk across both scopes, sorted by name, with a workspace skill
    /// shadowing a user skill of the same name. Cheap to call repeatedly - it always reflects the
    /// current filesystem state rather than a cached snapshot, since skills can be created or
    /// deleted mid-session.
    /// </summary>
    public static IReadOnlyList<SkillInfo> List()
    {
        var byName = new Dictionary<string, SkillInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var info in ListScope(SkillScope.User)) byName[info.Name] = info;
        foreach (var info in ListScope(SkillScope.Workspace)) byName[info.Name] = info; // shadows same-named user skill

        return byName.Values.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IEnumerable<SkillInfo> ListScope(SkillScope scope)
    {
        var root = RootFor(scope);
        if (!Directory.Exists(root)) return [];

        var skills = new List<SkillInfo>();
        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            var skillFile = Path.Combine(dir, "SKILL.md");
            if (!File.Exists(skillFile)) continue;

            var (name, description, _) = ParseFrontmatter(File.ReadAllText(skillFile));
            skills.Add(new SkillInfo(name ?? Path.GetFileName(dir), description ?? "", skillFile, scope));
        }

        return skills;
    }

    /// <summary>
    /// The skill's instructions (the content after its frontmatter block). Resolves the name via
    /// <see cref="ResolveScope"/> (workspace shadows user) unless <paramref name="scope"/> is given
    /// explicitly. Null if it doesn't exist in the resolved/given scope.
    /// </summary>
    public static string? LoadContent(string name, SkillScope? scope = null)
    {
        var resolved = scope ?? ResolveScope(name);
        if (resolved is null) return null;

        var path = SkillFilePath(name, resolved.Value);
        if (!File.Exists(path)) return null;

        var (_, _, content) = ParseFrontmatter(File.ReadAllText(path));
        return content;
    }

    /// <summary>
    /// Creates or overwrites the named skill's SKILL.md in <paramref name="scope"/> with a
    /// frontmatter block plus <paramref name="content"/>, creating its folder if needed.
    /// </summary>
    public static void Save(string name, string description, string content, SkillScope scope = SkillScope.Workspace)
    {
        var path = SkillFilePath(name, scope);
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
    /// Deletes the named skill's whole folder from <paramref name="scope"/> (resolved via
    /// <see cref="ResolveScope"/>, workspace shadowing user, if not given explicitly). Returns
    /// false if it didn't exist there.
    /// </summary>
    public static bool Delete(string name, SkillScope? scope = null)
    {
        var resolved = scope ?? ResolveScope(name);
        if (resolved is null) return false;

        var folder = Path.GetDirectoryName(SkillFilePath(name, resolved.Value))!;
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
