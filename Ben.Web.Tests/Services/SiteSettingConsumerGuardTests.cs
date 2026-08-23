using System.Text.RegularExpressions;
using Ben.Data.WebApi.Services;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Every site setting the admin page offers must be read by something. A setting nobody reads is
/// a control that lies: the page shows it, stores it, reports it as "Set", and changes nothing.
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> Two of these shipped and sat unread for months.
/// <c>site.announcement</c> was saved and displayed nowhere (item 151).
/// <c>org.allow-self-registration</c> was worse — it is a policy control described as "when off,
/// only a SuperAdmin can create one", and an administrator could switch it off, watch the page
/// report Off, and still have every signed-in visitor founding groups (item 152). A control whose
/// failure mode is believing you closed a door is worse than no control at all.</para>
///
/// <para><b>What counts as a consumer.</b> A reference to the <c>SiteSettingKeys</c> constant, or
/// to the literal key string, from anywhere outside the declaration file and outside the admin
/// page that edits them. Editing a setting is not consuming it — that is precisely the failure
/// being guarded, so <c>AdminSiteSettings.razor</c> cannot satisfy this test.</para>
///
/// <para><b>If this fails</b>, the setting was declared before its reader was written. Either
/// finish the reader or drop the declaration; do not add an exception, because an exception list
/// is just this bug with paperwork.</para>
/// </remarks>
public sealed class SiteSettingConsumerGuardTests
{
    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!;
    }

    /// <summary>The files that declare or merely edit settings, which therefore cannot count as
    /// readers of them.</summary>
    private static readonly string[] _notConsumers =
    [
        "SiteSettingsService.cs",       // the declaration itself
        "AdminSiteSettings.razor",      // edits every setting; consuming none
    ];

    /// <summary>Strips comments — this file's own prose names every key it guards.</summary>
    private static string StripComments(string source)
    {
        var s = Regex.Replace(source, @"@\*.*?\*@", string.Empty, RegexOptions.Singleline);
        s = Regex.Replace(s, @"(?<![\w""'])/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        return string.Join('\n', s.Split('\n').Select(line =>
        {
            var slashes = line.IndexOf("//", StringComparison.Ordinal);
            return slashes >= 0 ? line[..slashes] : line;
        }));
    }

    private static IEnumerable<string> SourceFiles(string root) =>
        new[] { "Ben.Data.WebApi", "Ben.Data.Source", "Ben.Service.RepositoryService",
                "Ben.Web.Services", "Ben.Web.Website", "Ben.Web.Website.Library" }
            .Select(p => Path.Combine(root, p))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories)
                        .Concat(Directory.EnumerateFiles(d, "*.razor", SearchOption.AllDirectories)))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !_notConsumers.Contains(Path.GetFileName(f)));

    [Fact]
    public void Every_declared_site_setting_is_read_by_something()
    {
        var root = RepoRoot().FullName;

        // The constant's C# name, so a consumer referencing SiteSettingKeys.Foo counts as well as
        // one hard-coding "site.foo".
        var namesByKey = typeof(SiteSettingKeys)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .ToDictionary(f => (string)f.GetRawConstantValue()!, f => f.Name, StringComparer.Ordinal);

        var unread = SiteSettingKeys.Seed.Select(s => s.Key).ToHashSet(StringComparer.Ordinal);

        foreach (var file in SourceFiles(root))
        {
            if (unread.Count == 0) break;
            var text = StripComments(File.ReadAllText(file));

            foreach (var key in unread.ToList())
            {
                var constantName = namesByKey.TryGetValue(key, out var n) ? n : null;
                if (text.Contains($"\"{key}\"", StringComparison.Ordinal)
                    || (constantName is not null
                        && text.Contains($"SiteSettingKeys.{constantName}", StringComparison.Ordinal)))
                {
                    unread.Remove(key);
                }
            }
        }

        Assert.True(
            unread.Count == 0,
            $"""
             These site settings are offered on the admin page and read by nothing:

               {string.Join("\n  ", unread.OrderBy(k => k, StringComparer.Ordinal))}

             An administrator can set one, see it reported as "Set", and watch it change nothing.
             That has now happened twice — site.announcement (item 151) and
             org.allow-self-registration (item 152), the second of which is a policy control that
             silently failed open. Write the consumer, or remove the declaration.
             """);
    }
}
