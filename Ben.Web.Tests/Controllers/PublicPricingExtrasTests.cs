using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Data.WebApi.Services;
using Ben.Service.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// What the pricing page says beside the bands (Ben, 10/13/2026): the free account's storage and
/// the event offer, read from the same settings, with the same fallbacks, as the code that
/// enforces them — so the page can't promise a number the site doesn't keep.
/// </summary>
public sealed class PublicPricingExtrasTests
{
    private static IDbContextFactory<BenDataContext> CreateFactory()
        => new PooledDbContextFactory<BenDataContext>(new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task SetAsync(IDbContextFactory<BenDataContext> factory, params (string Key, string Value)[] settings)
    {
        await using var db = await factory.CreateDbContextAsync();
        foreach (var (key, value) in settings)
            db.SiteSettings.Add(new SiteSetting
            {
                Id = Guid.NewGuid(), Key = key, Value = value,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid(),
            });
        await db.SaveChangesAsync();
    }

    private static async Task<PublicPricingExtras> ReadAsync(IDbContextFactory<BenDataContext> factory)
    {
        var answer = await new PublicPricingController(factory).GetExtras(default);
        return Assert.IsType<PublicPricingExtras>(Assert.IsType<OkObjectResult>(answer.Result).Value);
    }

    [Fact]
    public async Task With_nothing_set_the_defaults_the_site_enforces_are_shown()
    {
        var extras = await ReadAsync(CreateFactory());

        Assert.Equal(2048, extras.FreeAccountStorageMegabytes);
        Assert.True(extras.Events.OnSale);
        Assert.Equal(99m, extras.Events.UnitPrice);
        Assert.Equal(365, extras.Events.LifeDays);
        Assert.Equal(20, extras.Events.MaximumPerPurchase);
        Assert.Equal(48, extras.Events.CancellationWindowHours);
        Assert.Equal(2000, extras.Events.StorageMegabytes);
        Assert.Equal(90, extras.Events.FilesKeptDays);
    }

    [Fact]
    public async Task The_settings_are_what_the_page_shows()
    {
        var factory = CreateFactory();
        await SetAsync(factory,
            (SiteSettingKeys.FreeAccountStorageMegabytes, "5120"),
            (SiteSettingKeys.EventCreditPriceUsd, "149.50"),
            (SiteSettingKeys.EventCancellationCreditWindowHours, "72"),
            (SiteSettingKeys.EventStorageMegabytes, "4000"),
            (SiteSettingKeys.EventRetentionDays, "30"));

        var extras = await ReadAsync(factory);

        Assert.Equal(5120, extras.FreeAccountStorageMegabytes);
        Assert.Equal(149.50m, extras.Events.UnitPrice);
        Assert.Equal(72, extras.Events.CancellationWindowHours);
        Assert.Equal(4000, extras.Events.StorageMegabytes);
        Assert.Equal(30, extras.Events.FilesKeptDays);
    }

    [Fact]
    public async Task Events_switched_off_are_not_on_sale()
    {
        var factory = CreateFactory();
        await SetAsync(factory, (SiteSettingKeys.EventCreditsEnabled, "false"));

        Assert.False((await ReadAsync(factory)).Events.OnSale);
    }

    [Fact]
    public async Task A_nonsense_price_falls_back_to_the_price_checkout_charges()
    {
        var factory = CreateFactory();
        await SetAsync(factory, (SiteSettingKeys.EventCreditPriceUsd, "-5"));

        var extras = await ReadAsync(factory);
        Assert.Equal(99m, extras.Events.UnitPrice);
        Assert.True(extras.Events.OnSale);
    }
}
