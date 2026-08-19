using System.Text.RegularExpressions;

namespace Ben.Web.Playwright;

/// <summary>Reads the app's routes straight out of the .razor sources.</summary>
internal static class RouteCrawlHelper
{
    public static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    private static IEnumerable<string> AllRoutes()
    {
        foreach (var dir in new[] { "Ben.Web.Website", "Ben.Web.Website.Library" })
        foreach (var file in Directory.EnumerateFiles(Path.Combine(RepoRoot(), dir), "*.razor", SearchOption.AllDirectories))
        foreach (Match m in Regex.Matches(File.ReadAllText(file), @"^@page\s+""([^""]+)""", RegexOptions.Multiline))
            yield return m.Groups[1].Value;
    }

    public static List<string> ParameterisedRoutes()
        => new SortedSet<string>(AllRoutes().Where(r => r.Contains('{')), StringComparer.Ordinal).ToList();

    public static List<string> PlainRoutes(IEnumerable<string> excluded)
    {
        var skip = new HashSet<string>(excluded, StringComparer.OrdinalIgnoreCase);
        return new SortedSet<string>(
            AllRoutes().Where(r => !r.Contains('{') && !skip.Contains(r)),
            StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Substitutes known ids into a route template. Returns null when any placeholder has no
    /// value, so the caller skips rather than visiting a URL with a literal "{OrgId:guid}" in it.
    /// </summary>
    public static string? Fill(string route, IReadOnlyDictionary<string, string> ids)
    {
        var missing = false;
        var filled = Regex.Replace(route, @"\{(\w+)(?::[^}]+)?\}", m =>
        {
            if (ids.TryGetValue(m.Groups[1].Value, out var v)) return v;
            missing = true;
            return m.Value;
        });
        return missing ? null : filled;
    }
}
