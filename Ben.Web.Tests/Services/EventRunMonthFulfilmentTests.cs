using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Billing.StripeIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Facts = Ben.Data.WebApi.Services.Billing.StripeIntegration.StripeFulfillmentService.CheckoutFacts;

namespace Ben.Web.Tests.Services;

/// <summary>
/// A payment for extra months on a show's run (10/24/2026): on the show when it can still take them, held for
/// whoever paid when it can't, written once however often Stripe delivers it, never an event credit.
/// </summary>
public sealed class EventRunMonthFulfilmentTests
{
    private static IDbContextFactory<BenDataContext> Db()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static StripeFulfillmentService Service(IDbContextFactory<BenDataContext> db)
        => new(db, NullLogger<StripeFulfillmentService>.Instance);

    private sealed record Seeded(Guid OrgId, Guid UserId, Guid ShowId);

    private static async Task<Seeded> SeedAsync(IDbContextFactory<BenDataContext> factory,
        HostedEventLifecycleState state = HostedEventLifecycleState.Published)
    {
        await using var db = await factory.CreateDbContextAsync();
        var userId = Guid.NewGuid();
        db.AppUsers.Add(new AppUser { Id = userId, UserName = "o@test.com", Email = "o@test.com", DisplayName = "Owner", DateCreated = DateTime.UtcNow });
        var orgId = Guid.NewGuid();
        db.Organizations.Add(new Organization { Id = orgId, Name = "Playhouse", UrlName = $"ph-{Guid.NewGuid():N}"[..12], CreatedByAppUserId = userId, DateCreated = DateTime.UtcNow });
        var showId = Guid.NewGuid();
        var first = DateTime.UtcNow.Date.AddDays(10);
        var show = new HostedEvent
        {
            Id = showId, OrganizationId = orgId, PlaceId = Guid.NewGuid(), Name = "Murder at the Manor", UrlName = "manor",
            StartsOn = first, EndsOn = first.AddDays(7), DatesAreSeparate = true, LifecycleState = state,
            TimeZoneId = "America/Chicago", DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        };
        show.Nights.Add(new HostedEventNight { Id = Guid.NewGuid(), HostedEventId = showId, Date = first, DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId });
        show.Nights.Add(new HostedEventNight { Id = Guid.NewGuid(), HostedEventId = showId, Date = first.AddDays(7), SortOrder = 1, DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId });
        db.HostedEvents.Add(show);
        await db.SaveChangesAsync();
        return new Seeded(orgId, userId, showId);
    }

    private static StripeCompletedCheckout Paid(Seeded seed, int months, decimal total, string reference = "pi_months")
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        return new(
            "cs_" + reference, reference, "cus_x", "pm_x",
            new Dictionary<string, string>
            {
                [Facts.Keys.Organization] = seed.OrgId.ToString(),
                [Facts.Keys.User] = seed.UserId.ToString(),
                [Facts.Keys.RunMonths] = months.ToString(inv),
                [Facts.Keys.RunShow] = seed.ShowId.ToString(),
                [Facts.Keys.List] = total.ToString(inv),
                [Facts.Keys.TaxRate] = "0",
                [Facts.Keys.TaxAmount] = "0",
            });
    }

    [Fact]
    public async Task Paid_months_go_on_the_show_with_a_receipt_and_are_never_credits()
    {
        var factory = Db();
        var seed = await SeedAsync(factory);

        await Service(factory).FulfillAsync(Paid(seed, 2, 99m));

        await using var db = await factory.CreateDbContextAsync();
        var months = await db.EventRunMonths.ToListAsync();
        Assert.Equal(2, months.Count);
        Assert.All(months, m => Assert.Equal((seed.ShowId, seed.OrgId), (m.AppliedToHostedEventId!.Value, m.OwnerOrganizationId!.Value)));
        Assert.Equal(99m, months.Sum(m => m.PriceAtPurchase));
        Assert.All(months, m => Assert.StartsWith("R-", m.ReceiptNumber));
        Assert.Empty(await db.EventCredits.ToListAsync());
        var payment = await db.BillingLedgerEntries.SingleAsync(e => e.Kind == BillingLedgerKind.Payment);
        Assert.Contains("2 extra months for Murder at the Manor's run", payment.Description);
    }

    [Fact]
    public async Task Delivered_again_is_fulfilled_once()
    {
        var factory = Db();
        var seed = await SeedAsync(factory);
        await Service(factory).FulfillAsync(Paid(seed, 1, 49.5m));
        await Service(factory).FulfillAsync(Paid(seed, 1, 49.5m));

        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(1, await db.EventRunMonths.CountAsync());
        Assert.Equal(1, await db.BillingLedgerEntries.CountAsync(e => e.Kind == BillingLedgerKind.Payment));
    }

    [Theory]
    [InlineData(HostedEventLifecycleState.Cancelled)]
    [InlineData(HostedEventLifecycleState.Ended)]
    public async Task A_show_that_can_no_longer_take_them_gets_them_held_for_whoever_paid(HostedEventLifecycleState state)
    {
        var factory = Db();
        var seed = await SeedAsync(factory, state);

        await Service(factory).FulfillAsync(Paid(seed, 1, 49.5m));

        await using var db = await factory.CreateDbContextAsync();
        var month = await db.EventRunMonths.SingleAsync();
        Assert.Null(month.AppliedToHostedEventId);
        Assert.True(month.IsHeld(DateTime.UtcNow));
        Assert.Contains("held for another show", (await db.BillingLedgerEntries.SingleAsync(e => e.Kind == BillingLedgerKind.Payment)).Description);
    }
}
