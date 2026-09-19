using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Data.WebApi.Services;
using Ben.Service.Models.Support;
using Ben.Web.Services;
using Ben.Web.Services.WebApi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The site-wide announcement (Administration → Site Settings) must actually be shown. It was
/// the seventh write-only feature found by using the screen (2026-08-22): declared, seeded,
/// editable — and read by nothing. It now rides the anonymous site-features response into
/// <see cref="SiteFeaturesProvider"/>, and MainLayout renders it above every page's body.
/// </summary>
public sealed class SiteAnnouncementTests
{
    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var options = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PooledDbContextFactory<BenDataContext>(options);
    }

    private static async Task<SiteFeaturesInfo> GetAsync(IDbContextFactory<BenDataContext> factory)
    {
        var result = await new PublicSiteFeaturesController(factory).Get(default);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return Assert.IsType<SiteFeaturesInfo>(ok.Value);
    }

    [Fact]
    public async Task The_endpoint_publishes_a_set_announcement_and_null_when_unset()
    {
        var factory = CreateFactory();
        Assert.Null((await GetAsync(factory)).Announcement);

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.SiteSettings.Add(new SiteSetting
            {
                Id = Guid.NewGuid(), Key = SiteSettingKeys.SiteAnnouncement,
                Value = "Maintenance tonight 9–10pm Central.",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid(),
            });
            await db.SaveChangesAsync();
        }

        Assert.Equal("Maintenance tonight 9–10pm Central.", (await GetAsync(factory)).Announcement);
    }

    [Fact]
    public async Task The_provider_carries_an_announcement_and_clears_it_when_the_admin_does()
    {
        var info = new SiteFeaturesInfo(
            new Dictionary<string, bool> { [SiteFeatures.Equipment] = true }, "Heads up.");

        var client = new Mock<IBenAdminClient>();
        client.Setup(c => c.GetSiteFeaturesAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync(() => info);

        var services = new ServiceCollection();
        services.AddScoped(_ => client.Object);
        var provider = new SiteFeaturesProvider(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SiteFeaturesProvider>.Instance);

        await provider.PrimeAsync();
        Assert.Equal("Heads up.", provider.Announcement);

        // Clearing the setting must clear the banner on the next refresh — a maintenance notice
        // that cannot be taken down is nearly as bad as one that never shows.
        info = info with { Announcement = null };
        await provider.PrimeAsync();
        Assert.Null(provider.Announcement);
    }

    [Fact]
    public async Task A_failed_refresh_keeps_the_previous_announcement()
    {
        var answer = () => new SiteFeaturesInfo(
            new Dictionary<string, bool> { [SiteFeatures.Equipment] = true }, "Live notice.");

        var client = new Mock<IBenAdminClient>();
        client.Setup(c => c.GetSiteFeaturesAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync(() => answer());

        var services = new ServiceCollection();
        services.AddScoped(_ => client.Object);
        var provider = new SiteFeaturesProvider(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SiteFeaturesProvider>.Instance);

        await provider.PrimeAsync();
        Assert.Equal("Live notice.", provider.Announcement);

        // An empty response is the API failing, not the admin clearing the box.
        answer = () => new SiteFeaturesInfo(new Dictionary<string, bool>());
        await provider.PrimeAsync();
        Assert.Equal("Live notice.", provider.Announcement);
    }

    [Fact]
    public async Task An_unset_switch_reports_what_the_site_actually_does()
    {
        // The admin page drew its switches from the stored value alone, so a flag nobody had ever
        // set rendered "Off" while the feature ran — seven of them at once (item 153).
        var factory = CreateFactory();
        var settings = new SiteSettingsService(factory);
        var ctrl = new Ben.Data.WebApi.Controllers.Entities.AdminSiteSettingController(
            settings, new Moq.Mock<Ben.Service.RepositoryService.GenericInterfaces.IAuditLogService>().Object);

        var result = await ctrl.GetAll(default);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var rows = Assert.IsAssignableFrom<IEnumerable<Ben.Service.Models.Entities.SiteSettingRecord>>(ok.Value)
            .ToDictionary(r => r.Key, StringComparer.Ordinal);

        // Established sections and self-registration are on when nothing is stored …
        foreach (var key in new[]
                 {
                     SiteSettingKeys.FeatureVideoEditor, SiteSettingKeys.FeatureEvents,
                     SiteSettingKeys.FeatureDiscovery, SiteSettingKeys.FeatureCmsPages,
                     SiteSettingKeys.FeatureMediaLibrary, SiteSettingKeys.FeatureOrgMessaging,
                     SiteSettingKeys.FeatureVoting, SiteSettingKeys.AllowOrganizationSelfRegistration,
                 })
        {
            Assert.Null(rows[key].Value);
            Assert.True(rows[key].DefaultWhenUnset, $"{key} should read as on while unset.");
        }

        // … and the two unbuilt features stay off, so the page cannot advertise them either.
        Assert.False(rows[SiteSettingKeys.FeaturePublicFeed].DefaultWhenUnset);
        Assert.False(rows[SiteSettingKeys.FeaturePublications].DefaultWhenUnset);
    }

    [Fact]
    public void MainLayout_renders_the_announcement_the_provider_hands_it()
    {
        // No bUnit here, so the wiring is asserted at the source: the layout must read
        // Features.Announcement and put it in the site-announcement box above @Body. Regressed
        // by removing the block and watching this name the file.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);

        var layout = File.ReadAllText(Path.Combine(
            dir!.FullName, "Ben.Web.Website", "Components", "Layout", "MainLayout.razor"));

        Assert.Contains("Features.Announcement", layout);
        Assert.Contains("id=\"site-announcement\"", layout);
    }
}
