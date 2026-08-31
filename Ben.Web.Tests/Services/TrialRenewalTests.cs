using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Billing.StripeIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Months two and three of the three-month trial — the half of item 195 nobody had walked.
/// </summary>
/// <remarks>
/// <para>A group that redeems a 100%-off coupon never sees a card form, so it finishes checkout
/// with no <c>ProviderCustomerRef</c> and no <c>ProviderPaymentMethodRef</c>. Everything after
/// that first period depends on the renewal job picking it up anyway: the free continuing period
/// costs nothing to grant and there is nothing to charge.</para>
///
/// <para>"Your first three months are free" is a promise about the two periods NOBODY tested.
/// These are the tests for them.</para>
/// </remarks>
public sealed class TrialRenewalTests
{
    private sealed class FakeGateway : IStripeGateway
    {
        public readonly List<StripeRenewalCharge> Charges = [];
        public bool IsConfigured => true;

        public Task<StripeCheckoutHandle> CreateCheckoutSessionAsync(StripeCheckoutSpec spec, CancellationToken ct)
            => throw new NotSupportedException("renewals never open checkout sessions");

        public Task<StripeChargeOutcome> ChargeSavedCardAsync(StripeRenewalCharge charge, CancellationToken ct)
        {
            Charges.Add(charge);
            return Task.FromResult(new StripeChargeOutcome(true, $"pi_fake_{Charges.Count}", null));
        }

        public StripeCompletedCheckout? ParseCompletedCheckout(string payload, string signatureHeader)
            => throw new NotSupportedException();
    }

    private static IDbContextFactory<BenDataContext> Db()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static StripeRenewalJob Job(IDbContextFactory<BenDataContext> db, FakeGateway gateway)
        => new(db, gateway,
               new StripeFulfillmentService(db, NullLogger<StripeFulfillmentService>.Instance),
               NullLogger<StripeRenewalJob>.Instance);

    private sealed record Seeded(Guid OrgId, Guid UserId, DateTime PeriodEnd);

    /// <summary>
    /// A group one month into the trial: Active on Stripe, period ending inside the window, and
    /// <b>no card</b> — because a 100%-off checkout never asked for one.
    /// </summary>
    private static async Task<Seeded> SeedMidTrialAsync(
        IDbContextFactory<BenDataContext> factory, int periodsRemaining = 2, decimal price = 15m)
    {
        await using var db = await factory.CreateDbContextAsync();
        var userId = Guid.NewGuid();
        db.AppUsers.Add(new AppUser
        {
            Id = userId, UserName = "o@t.com", NormalizedUserName = "O@T.COM",
            Email = "o@t.com", DisplayName = "Owner", DateCreated = DateTime.UtcNow,
        });
        var orgId = Guid.NewGuid();
        db.Organizations.Add(new Organization
        {
            Id = orgId, Name = "Trial Group", UrlName = $"tg-{Guid.NewGuid():N}"[..12],
            CreatedByAppUserId = userId, DateCreated = DateTime.UtcNow,
        });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = userId,
            Role = OrganizationMemberRole.Owner, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        var tierId = Guid.NewGuid();
        db.SubscriptionTiers.Add(new SubscriptionTier
        {
            Id = tierId, Name = "Small group", MinMembers = 1, MaxMembers = null,
            IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        db.SubscriptionTierPrices.Add(new SubscriptionTierPrice
        {
            Id = Guid.NewGuid(), SubscriptionTierId = tierId,
            Interval = BillingInterval.Monthly, Price = price, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });

        var couponId = Guid.NewGuid();
        var codeId = Guid.NewGuid();
        db.Coupons.Add(new Coupon
        {
            Id = couponId, Name = "Three months free", Kind = CouponKind.Shared,
            PercentOff = 100, Duration = CouponDuration.Repeating, DurationPeriods = 3,
            AppliesToInterval = BillingInterval.Monthly, IsActive = true, RedemptionCount = 1,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        db.CouponCodes.Add(new CouponCode
        {
            Id = codeId, CouponId = couponId, Code = "TRIAL3", IsActive = true, RedemptionCount = 1,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        db.CouponRedemptions.Add(new CouponRedemption
        {
            Id = Guid.NewGuid(), CouponId = couponId, CouponCodeId = codeId, OrganizationId = orgId,
            PeriodsRemaining = periodsRemaining, RedeemedAtUtc = DateTime.UtcNow.AddMonths(-1),
            ListPrice = price, Discount = price, Payable = 0m,
            DateCreated = DateTime.UtcNow.AddMonths(-1), CreatedByAppUserId = userId,
        });

        var periodEnd = DateTime.UtcNow.AddHours(12);
        db.OrganizationSubscriptions.Add(new OrganizationSubscription
        {
            Id = Guid.NewGuid(), OrganizationId = orgId,
            Status = SubscriptionStatus.Active, SubscriptionTierId = tierId,
            Interval = BillingInterval.Monthly,
            CurrentPeriodStart = DateTime.UtcNow.AddMonths(-1),
            CurrentPeriodEnd = periodEnd,
            CancelAtPeriodEnd = false,
            PriceAtPeriodStart = 0m, MemberCountAtPeriodStart = 1,
            // The free-checkout signature: Stripe is the provider, but no customer and no card.
            ProviderName = "Stripe",
            ProviderCustomerRef = null,
            ProviderPaymentMethodRef = null,
            DateCreated = DateTime.UtcNow.AddMonths(-1), CreatedByAppUserId = userId,
        });
        await db.SaveChangesAsync();
        return new Seeded(orgId, userId, periodEnd);
    }

    /// <summary>
    /// The promise: month two is free too, and it arrives without anybody doing anything.
    /// </summary>
    [Fact]
    public async Task A_trial_group_with_no_card_is_carried_into_its_next_free_month()
    {
        var factory = Db();
        var seed = await SeedMidTrialAsync(factory);
        var gateway = new FakeGateway();

        await Job(factory, gateway).RunAsync(default);

        Assert.Empty(gateway.Charges);   // there is nothing to charge, and no card to charge it to

        await using var db = await factory.CreateDbContextAsync();
        var sub = await db.OrganizationSubscriptions.SingleAsync();
        Assert.Equal(SubscriptionStatus.Active, sub.Status);
        Assert.Equal(seed.PeriodEnd, sub.CurrentPeriodStart);          // stitched onto the old period
        Assert.Equal(seed.PeriodEnd.AddMonths(1), sub.CurrentPeriodEnd);

        // The meter moved: one of the two remaining free months has been spent.
        Assert.Equal(1, (await db.CouponRedemptions.SingleAsync()).PeriodsRemaining);

        // And the free month is in the money trail, like the first one.
        var entry = Assert.Single(await db.BillingLedgerEntries.ToListAsync());
        Assert.Equal(BillingLedgerKind.Charge, entry.Kind);
        Assert.Equal(0m, entry.Amount);
    }

    /// <summary>
    /// And when the free ride ends, a group with no card is left to the lapse machinery rather
    /// than charged, crashed on, or quietly given a fourth free month.
    /// </summary>
    [Fact]
    public async Task Once_the_free_periods_run_out_a_group_with_no_card_is_not_renewed()
    {
        var factory = Db();
        var seed = await SeedMidTrialAsync(factory, periodsRemaining: 0);
        var gateway = new FakeGateway();

        await Job(factory, gateway).RunAsync(default);

        Assert.Empty(gateway.Charges);   // nothing to charge it to

        await using var db = await factory.CreateDbContextAsync();
        var sub = await db.OrganizationSubscriptions.SingleAsync();
        Assert.Equal(seed.PeriodEnd, sub.CurrentPeriodEnd);            // not advanced — nothing was bought
        Assert.Empty(await db.BillingLedgerEntries.ToListAsync());     // and nothing was recorded
    }
}
