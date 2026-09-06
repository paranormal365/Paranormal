using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ben.Web.Tests;

/// <summary>
/// Covers the values that decide how hard the rate limiter bites, and where they come from.
/// </summary>
/// <remarks>
/// The interesting cases are all failure cases. A limit that silently reads as zero locks every
/// caller out of the site, so "unset", "not a number" and "not positive" each have to fall back to
/// the previous working value rather than being taken literally.
/// </remarks>
public class RateLimitSettingsProviderTests
{
    private const int ConfiguredGeocoding = 11;
    private const int ConfiguredAuth      = 22;
    private const int ConfiguredGlobal    = 333;
    private const int ConfiguredEventAttendance = 444;

    private static IConfiguration Configuration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimits:GeocodingPerMinute"] = ConfiguredGeocoding.ToString(),
                ["RateLimits:AuthPerMinute"]      = ConfiguredAuth.ToString(),
                ["RateLimits:GlobalPerMinute"]    = ConfiguredGlobal.ToString(),
                ["RateLimits:EventAttendancePerMinute"] = ConfiguredEventAttendance.ToString(),
            })
            .Build();

    private static RateLimitSettingsProvider Provider(IDbContextFactory<BenDataContext> factory) =>
        new(factory, Configuration(), NullLogger<RateLimitSettingsProvider>.Instance);

    private static async Task StoreAsync(IDbContextFactory<BenDataContext> factory, string key, string? value)
    {
        await using var db = await factory.CreateDbContextAsync();
        db.SiteSettings.Add(new SiteSetting
        {
            Id = Guid.NewGuid(),
            Key = key,
            Value = value,
            DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = Guid.NewGuid(),
        });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// The refresh runs in the background so requests never block on it, so a test has to wait for
    /// it rather than read once. Fails on timeout rather than looping forever.
    /// </summary>
    private static async Task<RateLimitSnapshot> WaitForAsync(
        RateLimitSettingsProvider provider, Func<RateLimitSnapshot, bool> until)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            var snapshot = provider.Current;
            if (until(snapshot)) return snapshot;
            await Task.Delay(25);
        }

        Assert.Fail("Rate limit snapshot did not reach the expected state within the timeout.");
        return null!;
    }

    [Fact]
    public void Starts_from_configuration_before_anything_is_read()
    {
        var provider = Provider(TestDbFactory.Create());

        // Read straight away: whatever the database says, the app has to run on something from the
        // first request, and that something is configuration.
        var snapshot = provider.Current;

        Assert.Equal(ConfiguredGeocoding, snapshot.Geocoding);
        Assert.Equal(ConfiguredAuth,      snapshot.Auth);
        Assert.Equal(ConfiguredGlobal,    snapshot.Global);
    }

    [Fact]
    public async Task Picks_up_a_stored_setting()
    {
        var factory = TestDbFactory.Create();
        await StoreAsync(factory, SiteSettingKeys.RateLimitGeocodingPerMinute, "7");
        var provider = Provider(factory);

        var snapshot = await WaitForAsync(provider, s => s.Geocoding == 7);

        Assert.Equal(7, snapshot.Geocoding);
        // Untouched keys keep their configured values rather than being reset alongside.
        Assert.Equal(ConfiguredAuth,   snapshot.Auth);
        Assert.Equal(ConfiguredGlobal, snapshot.Global);
    }

    [Theory]
    [InlineData("twenty")]   // an admin typing a word into the box
    [InlineData("0")]        // would refuse every request
    [InlineData("-5")]       // same, and nonsense besides
    [InlineData("")]         // cleared the field
    public async Task Keeps_the_fallback_when_a_stored_value_is_unusable(string stored)
    {
        var factory = TestDbFactory.Create();
        await StoreAsync(factory, SiteSettingKeys.RateLimitAuthPerMinute, stored);
        var provider = Provider(factory);

        // Give the background refresh room to run and (incorrectly) apply the bad value.
        await Task.Delay(300);
        _ = provider.Current;
        await Task.Delay(300);

        Assert.Equal(ConfiguredAuth, provider.Current.Auth);
    }

    [Fact]
    public async Task Every_limit_is_read_independently()
    {
        var factory = TestDbFactory.Create();
        await StoreAsync(factory, SiteSettingKeys.RateLimitGeocodingPerMinute, "1");
        await StoreAsync(factory, SiteSettingKeys.RateLimitAuthPerMinute,      "2");
        await StoreAsync(factory, SiteSettingKeys.RateLimitGlobalPerMinute,    "3");
        await StoreAsync(factory, SiteSettingKeys.RateLimitEventAttendancePerMinute, "4");
        await StoreAsync(factory, SiteSettingKeys.RateLimitAudioProcessingPerMinute, "5");
        var provider = Provider(factory);

        var snapshot = await WaitForAsync(
            provider, s => s is { Geocoding: 1, Auth: 2, Global: 3, EventAttendance: 4, AudioProcessing: 5 });

        Assert.Equal(new RateLimitSnapshot(1, 2, 3, 4, 5), snapshot);
    }

    [Fact]
    public void Every_rate_limit_key_is_offered_on_the_admin_page()
    {
        // The admin page renders SiteSettingKeys.Seed. A key that exists but is not seeded is a
        // setting nobody can edit — which is how a feature ends up looking configurable and not
        // being so.
        var seeded = SiteSettingKeys.Seed.Select(s => s.Key).ToList();

        Assert.Contains(SiteSettingKeys.RateLimitGeocodingPerMinute, seeded);
        Assert.Contains(SiteSettingKeys.RateLimitAuthPerMinute,      seeded);
        Assert.Contains(SiteSettingKeys.RateLimitGlobalPerMinute,    seeded);
        Assert.Contains(SiteSettingKeys.RateLimitEventAttendancePerMinute, seeded);
        Assert.Contains(SiteSettingKeys.RateLimitAudioProcessingPerMinute, seeded);
    }
}
