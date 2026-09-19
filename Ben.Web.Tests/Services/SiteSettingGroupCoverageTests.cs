using Ben.Data.WebApi.Services;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Every sitewide setting belongs to exactly one section of the admin page.
/// </summary>
/// <remarks>
/// <para>The Site Settings page used to be twenty-seven identical cards in declaration order, and
/// is now laid out in the sections <see cref="SiteSettingKeys.Groups"/> declares. That is a better
/// page and a new way to lose a setting: one added to the seed and filed nowhere would simply not
/// be drawn under any heading, and a setting nobody can find is the same as one that does not
/// exist.</para>
///
/// <para>So the pairing is checked rather than trusted. The page falls back to an "Other" section
/// if this ever fails in production, but the failure belongs here, at the moment the setting is
/// added, not on a screen somebody is trying to use.</para>
/// </remarks>
public sealed class SiteSettingGroupCoverageTests
{
    [Fact]
    public void Every_declared_setting_is_filed_under_a_section()
    {
        var ungrouped = SiteSettingKeys.Seed
            .Select(s => s.Key)
            .Where(key => string.IsNullOrWhiteSpace(SiteSettingKeys.GroupFor(key).Name))
            .ToList();

        Assert.True(ungrouped.Count == 0,
            $"""
             {ungrouped.Count} site setting(s) belong to no section, so the admin page cannot
             place them. Add each key to a section in SiteSettingKeys.Groups.

               {string.Join("\n  ", ungrouped)}
             """);
    }

    [Fact]
    public void No_section_claims_a_setting_twice_or_one_that_does_not_exist()
    {
        var declared = SiteSettingKeys.Seed.Select(s => s.Key).ToHashSet(StringComparer.Ordinal);
        var claimed = SiteSettingKeys.Groups.SelectMany(g => g.Keys).ToList();

        var unknown = claimed.Where(k => !declared.Contains(k)).ToList();
        Assert.True(unknown.Count == 0,
            $"Section(s) name settings that are not declared: {string.Join(", ", unknown)}");

        var twice = claimed.GroupBy(k => k, StringComparer.Ordinal)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(twice.Count == 0,
            $"Setting(s) appear in more than one section: {string.Join(", ", twice)}");
    }

    [Fact]
    public void Every_section_says_what_it_is_for()
    {
        // The sentence travels with the setting so the page cannot supply its own and drift.
        // A section with no sentence would print a heading and a blank line.
        var silent = SiteSettingKeys.Groups
            .Where(g => string.IsNullOrWhiteSpace(g.Blurb))
            .Select(g => g.Name)
            .ToList();

        Assert.True(silent.Count == 0,
            $"Section(s) with no explanatory line: {string.Join(", ", silent)}");
    }
}
