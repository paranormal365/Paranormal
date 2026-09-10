using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.Source.Services;
using Ben.Data.WebApi.Services.Billing;
using Ben.Data.WebApi.Services.Billing.StripeIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Item 233: a tour business pays per tour. Ben, 2026-09-10: "The $29 per month is for a single
/// tour no matter how many times scheduled." The arithmetic is pinned here before anything sits
/// on it, and the one counter every price goes through is checked against the real entities.
/// </summary>
public sealed class TourBillingTests
{
    // ── the pure arithmetic ──────────────────────────────────────────────────

    [Theory]
    [InlineData(OrganizationKind.GhostWalkingTour, 0, 1)]
    [InlineData(OrganizationKind.GhostWalkingTour, 1, 1)]
    [InlineData(OrganizationKind.GhostWalkingTour, 3, 3)]
    [InlineData(OrganizationKind.PublicEventProvider, 2, 2)]
    [InlineData(OrganizationKind.InvestigationGroup, 5, 1)]
    [InlineData(OrganizationKind.HauntedProperty, 5, 1)]
    public void Units_are_the_live_tours_for_a_business_floored_at_one_and_one_for_anyone_else(
        OrganizationKind kind, int tours, int expected)
        => Assert.Equal(expected, TourBilling.Units(kind, tours));

    [Fact]
    public void List_price_is_unit_times_units_rounded_to_the_cent()
    {
        Assert.Equal(87m, TourBilling.ListPrice(29m, 3));
        Assert.Equal(29m, TourBilling.ListPrice(29m, 0));      // never below one unit
        Assert.Equal(59.98m, TourBilling.ListPrice(29.99m, 2));
    }

    [Fact]
    public void A_tour_added_on_day_ten_of_a_thirty_day_period_owes_twenty_one_thirtieths()
    {
        var start = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = start.AddDays(30);
        var now = start.AddDays(9).AddHours(15);   // day 10, mid-afternoon: today still counts
        Assert.Equal(Math.Round(29m * 21m / 30m, 2), TourBilling.Remainder(29m, start, end, now));
    }

    [Fact]
    public void Nothing_is_owed_once_the_period_is_over_and_never_more_than_one_unit()
    {
        var start = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = start.AddDays(30);
        Assert.Equal(0m, TourBilling.Remainder(29m, start, end, end));
        Assert.Equal(0m, TourBilling.Remainder(29m, start, end, end.AddDays(3)));
        Assert.Equal(0m, TourBilling.Remainder(29m, end, start, start));          // malformed
        Assert.Equal(29m, TourBilling.Remainder(29m, start, end, start.AddDays(-5))); // clock skew
    }

    // ── the counter the quote, checkout and renewal share ────────────────────

    private static IDbContextFactory<BenDataContext> Db()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed record Seeded(Guid OrgId, Guid UserId, Guid FlatTierId, Guid BandTierId);

    /// <summary>A sound ladder ($40 for everyone), a flat business tier ($29 monthly, $290
    /// yearly), one organization of the given kind with two members and N live tours.</summary>
    private static async Task<Seeded> SeedAsync(
        IDbContextFactory<BenDataContext> factory, OrganizationKind kind, int tours,
        bool offerFlatTier = true, int retiredTours = 0)
    {
        await using var db = await factory.CreateDbContextAsync();
        var now = DateTime.UtcNow;
        var userId = Guid.NewGuid();
        db.AppUsers.Add(new AppUser
        {
            Id = userId, UserName = "o@t.com", NormalizedUserName = "O@T.COM",
            Email = "o@t.com", DisplayName = "Owner", DateCreated = now,
        });
        var orgId = Guid.NewGuid();
        db.Organizations.Add(new Organization
        {
            Id = orgId, Name = "Printers Alley Walks", UrlName = $"paw-{Guid.NewGuid():N}"[..12],
            Kind = kind, CreatedByAppUserId = userId, DateCreated = now,
        });
        for (var i = 0; i < 2; i++)
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = userId,
                Role = OrganizationMemberRole.Member, IsActive = true,
                DateCreated = now, CreatedByAppUserId = userId,
            });
        var addressId = Guid.NewGuid();
        db.OrganizationAddresses.Add(new OrganizationAddress
        {
            Id = addressId, OrganizationId = orgId, OrganizationAddressTypeId = Guid.NewGuid(),
            StreetAddress1 = "1 Printers Alley", City = "Nashville", State = "TN", ZipCode = "37201",
            Country = "US", DateCreated = now, CreatedByAppUserId = userId,
        });
        for (var i = 0; i < tours + retiredTours; i++)
            db.Tours.Add(new Tour
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, Name = $"Walk {i}", UrlName = $"walk-{i}",
                StartOrganizationAddressId = addressId,
                RetiredAtUtc = i < tours ? null : now.AddDays(-1),
                DateCreated = now, CreatedByAppUserId = userId,
            });

        var bandId = Guid.NewGuid();
        db.SubscriptionTiers.Add(new SubscriptionTier
        {
            Id = bandId, Name = "Everyone", MinMembers = 1, MaxMembers = null, IsBandedByMembers = true,
            IsActive = true, SortOrder = 10, DateCreated = now, CreatedByAppUserId = userId,
        });
        db.SubscriptionTierPrices.Add(new SubscriptionTierPrice
        {
            Id = Guid.NewGuid(), SubscriptionTierId = bandId,
            Interval = BillingInterval.Monthly, Price = 40m, IsActive = true,
            DateCreated = now, CreatedByAppUserId = userId,
        });
        var flatId = Guid.NewGuid();
        if (offerFlatTier)
        {
            db.SubscriptionTiers.Add(new SubscriptionTier
            {
                Id = flatId, Name = "Tour & Event Business", MinMembers = 1, MaxMembers = null,
                IsBandedByMembers = false, IsActive = true, SortOrder = 90,
                DateCreated = now, CreatedByAppUserId = userId,
            });
            db.SubscriptionTierPrices.Add(new SubscriptionTierPrice
            {
                Id = Guid.NewGuid(), SubscriptionTierId = flatId,
                Interval = BillingInterval.Monthly, Price = 29m, IsActive = true,
                DateCreated = now, CreatedByAppUserId = userId,
            });
            db.SubscriptionTierPrices.Add(new SubscriptionTierPrice
            {
                Id = Guid.NewGuid(), SubscriptionTierId = flatId,
                Interval = BillingInterval.Yearly, Price = 290m, IsActive = true,
                DateCreated = now, CreatedByAppUserId = userId,
            });
        }
        await db.SaveChangesAsync();
        return new Seeded(orgId, userId, flatId, bandId);
    }

    private static async Task<BillableUnits.Priced?> PriceAsync(
        IDbContextFactory<BenDataContext> factory, Seeded seed, OrganizationKind kind,
        BillingInterval interval = BillingInterval.Monthly)
    {
        await using var db = await factory.CreateDbContextAsync();
        var tiers = await db.SubscriptionTiers.AsNoTracking().Include(t => t.Prices).ToListAsync();
        return await BillableUnits.PriceAsync(db, tiers, seed.OrgId, kind, interval, default);
    }

    [Fact]
    public async Task A_business_with_three_tours_lists_three_times_the_unit_price()
    {
        var factory = Db();
        var seed = await SeedAsync(factory, OrganizationKind.GhostWalkingTour, tours: 3);

        var priced = await PriceAsync(factory, seed, OrganizationKind.GhostWalkingTour);

        Assert.NotNull(priced);
        Assert.Equal(seed.FlatTierId, priced.Tier.Id);
        Assert.Equal(3, priced.Units);
        Assert.Equal(29m, priced.UnitPrice);
        Assert.Equal(87m, priced.ListPrice);
        Assert.Equal("3 tours", BillableUnits.Describe(priced));
    }

    [Fact]
    public async Task A_retired_tour_is_not_counted_and_a_business_with_none_still_pays_for_one()
    {
        var factory = Db();
        var seed = await SeedAsync(factory, OrganizationKind.GhostWalkingTour, tours: 1, retiredTours: 2);
        var priced = await PriceAsync(factory, seed, OrganizationKind.GhostWalkingTour);
        Assert.Equal(1, priced!.Units);
        Assert.Equal(29m, priced.ListPrice);

        var empty = Db();
        var none = await SeedAsync(empty, OrganizationKind.GhostWalkingTour, tours: 0);
        var pricedNone = await PriceAsync(empty, none, OrganizationKind.GhostWalkingTour, BillingInterval.Yearly);
        Assert.Equal(1, pricedNone!.Units);
        Assert.Equal(290m, pricedNone.ListPrice);
        Assert.Equal("1 tour", BillableUnits.Describe(pricedNone));
    }

    [Fact]
    public async Task A_group_lists_its_band_once_whatever_it_has_lying_around()
    {
        // Tours rows on a group would be a data accident; the ladder ignores them.
        var factory = Db();
        var seed = await SeedAsync(factory, OrganizationKind.InvestigationGroup, tours: 3);
        var priced = await PriceAsync(factory, seed, OrganizationKind.InvestigationGroup);
        Assert.Equal(seed.BandTierId, priced!.Tier.Id);
        Assert.Equal(1, priced.Units);
        Assert.Equal(40m, priced.ListPrice);
        Assert.Equal("2 members", BillableUnits.Describe(priced));
    }

    [Fact]
    public async Task A_business_with_no_flat_tier_on_offer_is_priced_by_the_ladder_once()
    {
        var factory = Db();
        var seed = await SeedAsync(factory, OrganizationKind.GhostWalkingTour, tours: 3, offerFlatTier: false);
        var priced = await PriceAsync(factory, seed, OrganizationKind.GhostWalkingTour);
        Assert.Equal(seed.BandTierId, priced!.Tier.Id);
        Assert.Equal(1, priced.Units);
        Assert.Equal(40m, priced.ListPrice);
    }

    [Fact]
    public async Task A_cadence_the_tier_does_not_sell_prices_to_nothing()
    {
        var factory = Db();
        var seed = await SeedAsync(factory, OrganizationKind.InvestigationGroup, tours: 0);
        Assert.Null(await PriceAsync(factory, seed, OrganizationKind.InvestigationGroup, BillingInterval.Yearly));
    }

    // ── renewal re-counts the live tours ─────────────────────────────────────

    private sealed class FakeGateway : IStripeGateway
    {
        public readonly List<StripeRenewalCharge> Charges = [];
        public bool IsConfigured => true;
        public Task<StripeCheckoutHandle> CreateCheckoutSessionAsync(StripeCheckoutSpec spec, CancellationToken ct)
            => throw new NotSupportedException();
        public Task<StripeChargeOutcome> ChargeSavedCardAsync(StripeRenewalCharge charge, CancellationToken ct)
        {
            Charges.Add(charge);
            return Task.FromResult(new StripeChargeOutcome(true, $"pi_fake_{Charges.Count}", null));
        }
        public StripeCompletedCheckout? ParseCompletedCheckout(string payload, string signatureHeader)
            => throw new NotSupportedException();
    }

    [Fact]
    public async Task Renewal_charges_the_live_tour_count_and_freezes_it_on_the_new_period()
    {
        var factory = Db();
        var seed = await SeedAsync(factory, OrganizationKind.GhostWalkingTour, tours: 2);
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.OrganizationSubscriptions.Add(new OrganizationSubscription
            {
                Id = Guid.NewGuid(), OrganizationId = seed.OrgId,
                Status = SubscriptionStatus.Active, SubscriptionTierId = seed.FlatTierId,
                Interval = BillingInterval.Monthly,
                CurrentPeriodStart = DateTime.UtcNow.AddMonths(-1),
                CurrentPeriodEnd = DateTime.UtcNow.AddHours(12),
                PriceAtPeriodStart = 29m, MemberCountAtPeriodStart = 2, TourCountAtPeriodStart = 1,
                ProviderName = "Stripe", ProviderCustomerRef = "cus_fake", ProviderPaymentMethodRef = "pm_fake",
                DateCreated = DateTime.UtcNow.AddMonths(-1), CreatedByAppUserId = seed.UserId,
            });
            await db.SaveChangesAsync();
        }

        var gateway = new FakeGateway();
        var job = new StripeRenewalJob(factory, gateway,
            new StripeFulfillmentService(factory, NullLogger<StripeFulfillmentService>.Instance),
            NullLogger<StripeRenewalJob>.Instance);
        await job.RunAsync(default);

        var charge = Assert.Single(gateway.Charges);
        Assert.Equal(58m, charge.Total);              // two live tours, no tax rule seeded
        Assert.Contains("2 tours", charge.Description);

        await using var after = await factory.CreateDbContextAsync();
        var sub = await after.OrganizationSubscriptions.SingleAsync();
        Assert.Equal(2, sub.TourCountAtPeriodStart);
        Assert.Equal(58m, sub.PriceAtPeriodStart);
    }
}
