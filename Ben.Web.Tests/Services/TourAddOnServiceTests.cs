using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Billing;
using Ben.Data.WebApi.Services.Billing.StripeIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// A tour added part-way through a paid period pays for the days that are left (item 233).
/// </summary>
/// <remarks>
/// Decided with Ben, 2026-09-10, over the alternative of counting it only at renewal — which on a
/// yearly plan would have handed a business up to a year of a second tour for nothing. The other
/// half of the rule is the one these tests spend the most effort on: <b>a payment problem never
/// blocks the tour</b>, because a business locked out of its own product cannot fix the payment.
/// </remarks>
public sealed class TourAddOnServiceTests
{
    private sealed class FakeGateway : IStripeGateway
    {
        public readonly List<StripeRenewalCharge> Charges = [];
        public bool Configured = true;
        public bool NextSucceeds = true;
        public bool IsConfigured => Configured;
        public Task<StripeCheckoutHandle> CreateCheckoutSessionAsync(StripeCheckoutSpec spec, CancellationToken ct)
            => throw new NotSupportedException();
        public Task<StripeChargeOutcome> ChargeSavedCardAsync(StripeRenewalCharge charge, CancellationToken ct)
        {
            Charges.Add(charge);
            return Task.FromResult(new StripeChargeOutcome(
                NextSucceeds, $"pi_{Charges.Count}", NextSucceeds ? null : "card_declined"));
        }
        public StripeCompletedCheckout? ParseCompletedCheckout(string payload, string signatureHeader)
            => throw new NotSupportedException();
    }

    private static IDbContextFactory<BenDataContext> Db()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed record World(IDbContextFactory<BenDataContext> Factory, Guid OrgId, Guid UserId, Guid AddressId);

    /// <summary>
    /// A ghost walk on the flat $30-monthly business tier, half way through a 30-day period it
    /// paid for one tour of, with a card on file. $30 and thirty days so the arithmetic in the
    /// assertions is readable rather than a rounding puzzle.
    /// </summary>
    private static async Task<World> SeedAsync(
        IDbContextFactory<BenDataContext> factory,
        int paidTours = 1, int liveTours = 1, bool card = true,
        SubscriptionStatus status = SubscriptionStatus.Active,
        OrganizationKind kind = OrganizationKind.GhostWalkingTour)
    {
        var now = DateTime.UtcNow;
        Guid orgId = Guid.NewGuid(), userId = Guid.NewGuid(), addressId = Guid.NewGuid(), tierId = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();
        db.AppUsers.Add(new AppUser { Id = userId, UserName = "o", Email = "o@t.com", DisplayName = "Owner", DateCreated = now });
        db.Organizations.Add(new Organization
        {
            Id = orgId, Name = "Printers Alley Walks", UrlName = $"paw{Guid.NewGuid():N}"[..10],
            Kind = kind, DateCreated = now, CreatedByAppUserId = userId,
        });
        db.OrganizationAddresses.Add(new OrganizationAddress
        {
            Id = addressId, OrganizationId = orgId, OrganizationAddressTypeId = Guid.NewGuid(),
            StreetAddress1 = "1 Printers Alley", City = "Nashville", State = "TN", ZipCode = "37201",
            Country = "US", DateCreated = now, CreatedByAppUserId = userId,
        });
        for (var i = 0; i < liveTours; i++)
            db.Tours.Add(new Tour
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, Name = $"Walk {i}", UrlName = $"walk-{i}",
                StartOrganizationAddressId = addressId, DateCreated = now, CreatedByAppUserId = userId,
            });

        db.SubscriptionTiers.Add(new SubscriptionTier
        {
            Id = tierId, Name = "Tour & Event Business", MinMembers = 1, MaxMembers = null,
            IsBandedByMembers = false, IsActive = true, SortOrder = 90,
            DateCreated = now, CreatedByAppUserId = userId,
        });
        db.SubscriptionTierPrices.Add(new SubscriptionTierPrice
        {
            Id = Guid.NewGuid(), SubscriptionTierId = tierId,
            Interval = BillingInterval.Monthly, Price = 30m, IsActive = true,
            DateCreated = now, CreatedByAppUserId = userId,
        });
        db.OrganizationSubscriptions.Add(new OrganizationSubscription
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, Status = status,
            SubscriptionTierId = tierId, Interval = BillingInterval.Monthly,
            CurrentPeriodStart = now.AddDays(-15), CurrentPeriodEnd = now.AddDays(15),
            PriceAtPeriodStart = 30m, MemberCountAtPeriodStart = 1, TourCountAtPeriodStart = paidTours,
            ProviderName = "Stripe",
            ProviderCustomerRef = card ? "cus_fake" : null,
            ProviderPaymentMethodRef = card ? "pm_fake" : null,
            DateCreated = now.AddDays(-15), CreatedByAppUserId = userId,
        });
        await db.SaveChangesAsync();
        return new World(factory, orgId, userId, addressId);
    }

    private static TourAddOnService Service(FakeGateway gateway)
        => new(gateway, NullLogger<TourAddOnService>.Instance);

    private static async Task<TourAddOnService.Outcome> RunAsync(World w, FakeGateway gateway)
    {
        await using var db = await w.Factory.CreateDbContextAsync();
        var org = await db.Organizations.FirstAsync(o => o.Id == w.OrgId);
        return await Service(gateway).ChargeRemainderAsync(db, org, w.UserId, default);
    }

    // ── the charge ───────────────────────────────────────────────────────────

    [Fact]
    public async Task A_second_tour_half_way_through_a_month_pays_for_the_half_that_is_left()
    {
        var factory = Db();
        var w = await SeedAsync(factory, paidTours: 1, liveTours: 2);
        var gateway = new FakeGateway();

        var outcome = await RunAsync(w, gateway);

        var charge = Assert.Single(gateway.Charges);
        // 15 of 30 days at $30, give or take the day the test runs on.
        Assert.InRange(charge.Total, 14m, 17m);
        Assert.Equal(charge.Total, outcome.Charged);
        Assert.Contains("Charged $", outcome.Note);
        Assert.Contains("2 tours", outcome.Note);

        await using var db = await factory.CreateDbContextAsync();
        var sub = await db.OrganizationSubscriptions.SingleAsync();
        Assert.Equal(2, sub.TourCountAtPeriodStart);

        // Both halves of the money trail, and a receipt on the payment.
        Assert.Equal(1, await db.BillingLedgerEntries.CountAsync(e => e.Kind == BillingLedgerKind.Charge));
        Assert.Equal(1, await db.BillingLedgerEntries.CountAsync(e => e.Kind == BillingLedgerKind.Payment && e.ReceiptNumber != null));
    }

    [Fact]
    public async Task Two_tours_added_at_once_pay_for_two()
    {
        var factory = Db();
        var w = await SeedAsync(factory, paidTours: 1, liveTours: 3);
        var gateway = new FakeGateway();

        await RunAsync(w, gateway);

        var charge = Assert.Single(gateway.Charges);
        Assert.InRange(charge.Total, 28m, 34m);   // two half-months
        Assert.Contains("2 tours added", charge.Description);
    }

    [Fact]
    public async Task A_tour_already_paid_for_this_period_is_charged_nothing()
    {
        var factory = Db();
        var w = await SeedAsync(factory, paidTours: 2, liveTours: 2);
        var gateway = new FakeGateway();

        var outcome = await RunAsync(w, gateway);

        Assert.Empty(gateway.Charges);
        Assert.Equal(0m, outcome.Charged);
        Assert.Contains("already paid for", outcome.Note);
    }

    [Fact]
    public async Task The_charge_is_idempotent_per_period_and_per_resulting_count()
    {
        // A double click, or a retry after a timeout, must not bill the same tour twice.
        var factory = Db();
        var w = await SeedAsync(factory, paidTours: 1, liveTours: 2);
        var gateway = new FakeGateway();

        await RunAsync(w, gateway);
        var first = gateway.Charges[0].IdempotencyKey;

        // Same world, asked again from a stale state: the key it would send is the same one.
        await using (var db = await factory.CreateDbContextAsync())
        {
            var sub = await db.OrganizationSubscriptions.SingleAsync();
            sub.TourCountAtPeriodStart = 1;
            await db.SaveChangesAsync();
        }
        await RunAsync(w, gateway);

        Assert.Equal(first, gateway.Charges[1].IdempotencyKey);
    }

    // ── a payment problem never blocks the tour ──────────────────────────────

    [Fact]
    public async Task With_no_card_on_file_nothing_is_charged_and_the_renewal_is_told_to_count_it()
    {
        var factory = Db();
        var w = await SeedAsync(factory, paidTours: 1, liveTours: 2, card: false);
        var gateway = new FakeGateway();

        var outcome = await RunAsync(w, gateway);

        Assert.Empty(gateway.Charges);
        Assert.Contains("no card on file", outcome.Note);

        await using var db = await factory.CreateDbContextAsync();
        // Left at one: the renewal re-counts from the live tours and will pick the second up.
        Assert.Equal(1, (await db.OrganizationSubscriptions.SingleAsync()).TourCountAtPeriodStart);
    }

    [Fact]
    public async Task A_decline_leaves_the_tour_standing_and_records_no_money()
    {
        var factory = Db();
        var w = await SeedAsync(factory, paidTours: 1, liveTours: 2);
        var gateway = new FakeGateway { NextSucceeds = false };

        var outcome = await RunAsync(w, gateway);

        Assert.Single(gateway.Charges);           // it tried
        Assert.Equal(0m, outcome.Charged);
        Assert.Contains("declined", outcome.Note);
        Assert.Contains("still yours to run", outcome.Note);

        await using var db = await factory.CreateDbContextAsync();
        Assert.Empty(db.BillingLedgerEntries);
        Assert.Equal(1, (await db.OrganizationSubscriptions.SingleAsync()).TourCountAtPeriodStart);
        Assert.Equal(2, await db.Tours.CountAsync());
    }

    [Fact]
    public async Task A_business_with_no_running_subscription_owes_nothing_yet()
    {
        var factory = Db();
        var w = await SeedAsync(factory, paidTours: 0, liveTours: 2, status: SubscriptionStatus.Lapsed);
        var gateway = new FakeGateway();

        var outcome = await RunAsync(w, gateway);

        Assert.Empty(gateway.Charges);
        Assert.Contains("priced together when you subscribe", outcome.Note);
    }

    [Fact]
    public async Task A_group_priced_by_its_members_is_never_charged_for_a_tour()
    {
        var factory = Db();
        var w = await SeedAsync(factory, paidTours: 0, liveTours: 3, kind: OrganizationKind.InvestigationGroup);
        var gateway = new FakeGateway();

        var outcome = await RunAsync(w, gateway);

        Assert.Empty(gateway.Charges);
        Assert.Contains("priced by its members", outcome.Note);
    }
}
