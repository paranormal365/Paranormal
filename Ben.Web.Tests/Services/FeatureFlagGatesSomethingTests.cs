using System.Text.RegularExpressions;
using Ben.Data.WebApi.Services;
using Ben.Web.Services;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// A feature switch must switch something off. Four of the ten currently do not, and this test
/// stops that number growing.
/// </summary>
/// <remarks>
/// <para><b>The failure.</b> <see cref="SiteSettingKeys"/> states the rule these flags exist to
/// keep: "Turning one off must kill the URLs, not just the navigation links." Four flags keep
/// neither. <c>features.discovery</c>, <c>features.cms-pages</c> and <c>features.voting</c> are
/// read by nothing at all, so switching them off changes nothing anywhere.
/// <c>features.events</c> is read only by <c>EventReminderJob</c>, which is worse than untouched:
/// switching it off silently stops the reminder emails while leaving the calendars and RSVPs
/// working, so people sign up for events and are never reminded. That one is invisible to this
/// test for the reason given below.</para>
///
/// <para><b>Why a ratchet and not a ban.</b> Same reasoning as
/// <see cref="SwallowedFailureRatchetTests"/>: closing these four means deciding what each switch
/// takes down — whether <c>features.cms-pages</c> also hides <c>/o/&#123;group&#125;/cases</c>, for
/// instance — which is a product decision per feature, across roughly two dozen surfaces and an
/// anonymous read path. A hard ban would be one unmergeable change. A list that may only shrink
/// makes the debt visible, keeps the work splittable, and makes the one thing that matters
/// impossible: shipping an eleventh switch that lies.</para>
///
/// <para><b>What this measures is a floor, not a guarantee.</b> It asks whether a flag is read
/// anywhere at all — it cannot tell a complete gate from a token one.
/// <c>features.events</c> is the standing example and is deliberately NOT listed below: it passes
/// this test on the strength of one read in <c>EventReminderJob</c> while the calendars, event
/// pages and RSVPs ignore it entirely. Passing here means a flag is not obviously inert; it does
/// not mean the switch does what its description promises.</para>
///
/// <para><b>Fixing one</b> means wrapping its pages in <c>FeatureGate</c>, hiding its navigation,
/// refusing its endpoints, then deleting its line below. Never add a line.</para>
/// </remarks>
public sealed class FeatureFlagGatesSomethingTests
{
    /// <summary>
    /// Flags known to gate nothing, recorded 2026-08-22. This list may only shrink — and on
    /// 2026-08-23 (item 154) it reached empty: discovery, cms-pages and voting each got a
    /// server-side <c>FeatureGated</c> controller refusal, FeatureGate-wrapped pages, and hidden
    /// entry points, and events got the same treatment beyond its reminder-job read. The list
    /// stays as scaffolding so an eleventh inert switch still cannot ship.
    /// </summary>
    private static readonly HashSet<string> _knownUngated = new(StringComparer.Ordinal);

    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!;
    }

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

    /// <summary>
    /// Everything except the three files that merely declare or transport the flags. The public
    /// features endpoint is excluded for the same reason the admin page is excluded from
    /// <see cref="SiteSettingConsumerGuardTests"/>: publishing a flag is not gating on it.
    /// </summary>
    private static IEnumerable<string> GateSites(string root) =>
        new[] { "Ben.Data.WebApi", "Ben.Web.Services", "Ben.Web.Website", "Ben.Web.Website.Library" }
            .Select(p => Path.Combine(root, p))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories)
                        .Concat(Directory.EnumerateFiles(d, "*.razor", SearchOption.AllDirectories)))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => Path.GetFileName(f) is not ("SiteSettingsService.cs"
                                                or "SiteFeaturesProvider.cs"
                                                or "PublicSiteFeaturesController.cs"
                                                or "AdminSiteSettings.razor"));

    [Fact]
    public void No_new_feature_flag_may_gate_nothing()
    {
        var root = RepoRoot().FullName;
        // The API's declaration, not the website's SiteFeatures.All — All is a hand-maintained
        // list, and a flag someone forgets to add to it would slip past this test entirely while
        // still appearing on the admin page. FeatureDefaults is what the endpoint publishes and
        // what the settings page renders, so it is the list that decides what exists.
        var ungated = SiteSettingKeys.FeatureFlags.ToHashSet(StringComparer.Ordinal);

        var namesByKey = typeof(SiteSettingKeys)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .ToDictionary(f => (string)f.GetRawConstantValue()!, f => f.Name, StringComparer.Ordinal);

        var websiteNames = typeof(SiteFeatures)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .ToDictionary(f => (string)f.GetRawConstantValue()!, f => f.Name, StringComparer.Ordinal);

        foreach (var file in GateSites(root))
        {
            if (ungated.Count == 0) break;
            var text = StripComments(File.ReadAllText(file));

            foreach (var key in ungated.ToList())
            {
                if ((namesByKey.TryGetValue(key, out var apiName)
                        && text.Contains($"SiteSettingKeys.{apiName}", StringComparison.Ordinal))
                    || (websiteNames.TryGetValue(key, out var webName)
                        && text.Contains($"SiteFeatures.{webName}", StringComparison.Ordinal)))
                {
                    ungated.Remove(key);
                }
            }
        }

        var regressions = ungated.Except(_knownUngated, StringComparer.Ordinal).OrderBy(k => k).ToList();
        Assert.True(regressions.Count == 0,
            "These feature switches gate nothing, so turning one off changes nothing:\n  "
            + string.Join("\n  ", regressions)
            + "\n\nWrap the feature's pages in FeatureGate, hide its navigation, and refuse its "
            + "endpoints. A switch that reports Off while the section keeps working is the bug "
            + "this test exists to stop.");

        var fixedButStillListed = _knownUngated.Except(ungated, StringComparer.Ordinal).OrderBy(k => k).ToList();
        Assert.True(fixedButStillListed.Count == 0,
            "These flags now gate something and must be removed from _knownUngated — the list "
            + "only means anything while it is honest:\n  " + string.Join("\n  ", fixedButStillListed));
    }
}
