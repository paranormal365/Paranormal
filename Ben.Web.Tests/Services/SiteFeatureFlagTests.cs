using Ben.Data.WebApi.Services;
using Ben.Web.Services;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Guards the feature-flag contract, which is split across two projects that cannot see each
/// other.
/// </summary>
/// <remarks>
/// <para>The API declares the switches in <see cref="SiteSettingKeys"/>; the website names the
/// same strings in <see cref="SiteFeatures"/> and repeats their defaults in
/// <see cref="SiteFeaturesProvider.Defaults"/>, because the Blazor library cannot reference the
/// API project. Duplication that nothing checks is duplication that drifts — and the way it
/// drifts here is silent: rename a key on one side and the gate stops gating, with no error
/// anywhere and a section that looks switched on because its flag no longer matches anything.
/// </para>
///
/// <para>This test is the substitute for the reference the projects cannot have.</para>
/// </remarks>
public sealed class SiteFeatureFlagTests
{
    [Fact]
    public void The_website_and_the_api_name_the_same_feature_keys()
    {
        var api = SiteSettingKeys.FeatureFlags.OrderBy(k => k, StringComparer.Ordinal).ToArray();
        var website = SiteFeatures.All.OrderBy(k => k, StringComparer.Ordinal).ToArray();

        Assert.True(api.SequenceEqual(website, StringComparer.Ordinal),
            "The API and the website disagree about which feature flags exist. A key present on "
            + "only one side gates nothing and reports nothing.\n"
            + $"  API only:     {string.Join(", ", api.Except(website, StringComparer.Ordinal))}\n"
            + $"  Website only: {string.Join(", ", website.Except(api, StringComparer.Ordinal))}");
    }

    [Fact]
    public void The_website_and_the_api_agree_on_every_default()
    {
        var mismatched = SiteSettingKeys.FeatureDefaults
            .Where(f => !SiteFeaturesProvider.Defaults.TryGetValue(f.Key, out var website)
                        || website != f.DefaultWhenUnset)
            .Select(f => $"{f.Key}: api={f.DefaultWhenUnset}, "
                       + $"website={(SiteFeaturesProvider.Defaults.TryGetValue(f.Key, out var w) ? w.ToString() : "absent")}")
            .ToList();

        Assert.True(mismatched.Count == 0,
            "These flags default differently on the two sides. The website's default is what a "
            + "visitor sees while the API is unreachable, so a disagreement here is a section "
            + "that appears or vanishes exactly when nobody can investigate why:\n  "
            + string.Join("\n  ", mismatched));
    }

    [Fact]
    public void Every_feature_flag_is_offered_on_the_admin_page()
    {
        // Mirrors Every_rate_limit_key_is_offered_on_the_admin_page: a flag the admin page never
        // lists is a feature that looks configurable and is not.
        var seeded = SiteSettingKeys.Seed.Select(s => s.Key).ToHashSet(StringComparer.Ordinal);

        var missing = SiteSettingKeys.FeatureFlags
            .Where(k => !seeded.Contains(k))
            .ToList();

        Assert.True(missing.Count == 0,
            "These feature flags are declared but not in the admin page's seed list, so no-one "
            + "can switch them:\n  " + string.Join("\n  ", missing));
    }

    [Fact]
    public void Every_feature_flag_renders_as_a_switch_rather_than_a_text_box()
    {
        var missing = SiteSettingKeys.FeatureFlags
            .Where(k => !SiteSettingKeys.BooleanKeys.Contains(k))
            .ToList();

        Assert.True(missing.Count == 0,
            "These flags are not in BooleanKeys, so the admin page gives them a free-text input "
            + "and an administrator has to know to type \"true\":\n  " + string.Join("\n  ", missing));
    }

    [Fact]
    public void The_two_unbuilt_features_are_off_until_someone_turns_them_on()
    {
        // The whole point of shipping these flags before the features: a half-built section must
        // not be reachable because a default was written optimistically.
        Assert.False(SiteSettingKeys.DefaultFor(SiteSettingKeys.FeaturePublicFeed));
        Assert.False(SiteSettingKeys.DefaultFor(SiteSettingKeys.FeaturePublications));

        // ...and everything that already works must stay working when the flags ship.
        foreach (var (key, defaultWhenUnset) in SiteSettingKeys.FeatureDefaults)
        {
            if (key is SiteSettingKeys.FeaturePublicFeed or SiteSettingKeys.FeaturePublications)
                continue;

            Assert.True(defaultWhenUnset,
                $"{key} defaults to off, which would switch a working section off for every site "
                + "that has never touched the setting.");
        }
    }
}
