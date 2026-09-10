using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// One place knows the map library (item 228).
/// </summary>
/// <remarks>
/// Before this, four components each carried their own tile URL, marker template and resize
/// plumbing, and the second, third and fourth were copies of the first with the names changed.
/// Swapping the provider meant swapping it four times. <c>Kit/Maps/</c> is the only folder that
/// may mention MapKit JS or the OpenStreetMap tile server; a page that wants a map takes a
/// <c>BenMap</c>. Matched on content, not on one spelling, because a guard that reads one file
/// name is defeated by a rename (see the source-scan-guard lesson).
/// </remarks>
public class MapProviderGuardTests
{
    private static readonly Regex ProviderMention = new(
        @"mapkit\.|apple-mapkit|tile\.openstreetmap\.org|<TelerikMap\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Components still on the Telerik path, one per remaining phase of item 228. Each line goes
    /// when its phase lands; the test then refuses a new one.
    /// </summary>
    private static readonly HashSet<string> StillCrossing = new(StringComparer.Ordinal)
    {
        "Ben.Web.Website.Library/Shared/PublicCaseDiscovery.razor",          // phase 3
        "Ben.Web.Website.Library/Shared/PublicCaseDiscovery.razor.js",
        "Ben.Web.Website.Library/Manage/Maps/AddressMapPlayer.razor",         // phase 4
        "Ben.Web.Website.Library/Manage/Maps/AddressMapPlayer.razor.js",
        "Ben.Web.Website.Library/Manage/Maps/DirectionsMapModal.razor",       // phase 5
        "Ben.Web.Website.Library/Manage/Maps/DirectionsMapModal.razor.js",
    };

    /// <summary>The signer that gives MapKit JS its token. About the provider by definition, and not a map.</summary>
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        "Ben.Web.Website/Services/MapKitTokenService.cs",
    };

    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;
        return dir ?? throw new InvalidOperationException("repo root not found");
    }

    [Fact]
    public void OnlyKitMapsKnowsTheMapProvider()
    {
        var root = RepoRoot();
        var offenders = new List<string>();
        foreach (var project in new[] { "Ben.Web.Website.Library", "Ben.Web.Website" })
        {
            var dir = new DirectoryInfo(Path.Combine(root.FullName, project));
            foreach (var file in dir.EnumerateFiles("*.*", SearchOption.AllDirectories))
            {
                if (file.Extension is not (".razor" or ".js" or ".cs")) continue;
                var rel = Path.GetRelativePath(root.FullName, file.FullName).Replace('\\', '/');
                if (rel.Contains("/bin/") || rel.Contains("/obj/") || rel.Contains("/wwwroot/lib/")) continue;
                if (rel.StartsWith("Ben.Web.Website.Library/Kit/Maps/", StringComparison.Ordinal)) continue;
                if (StillCrossing.Contains(rel) || Allowed.Contains(rel)) continue;
                if (ProviderMention.IsMatch(File.ReadAllText(file.FullName))) offenders.Add(rel);
            }
        }
        Assert.True(offenders.Count == 0,
            "These files mention a map provider directly; they should take a BenMap instead:\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>The allow-list only shrinks. A file on it that no longer exists is a line to delete.</summary>
    [Fact]
    public void TheCrossingListNamesOnlyFilesThatStillExist()
    {
        var root = RepoRoot();
        var stale = StillCrossing.Where(rel => !File.Exists(Path.Combine(root.FullName, rel))).ToList();
        Assert.True(stale.Count == 0, "Remove from StillCrossing:\n  " + string.Join("\n  ", stale));
    }
}
