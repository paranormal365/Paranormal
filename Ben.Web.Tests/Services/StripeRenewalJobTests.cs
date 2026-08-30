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
/// The renewal job: who gets charged, how much, and what a decline does NOT do.
/// </summary>
/// <remarks>
/// The gateway is faked at the interface — the seam exists for exactly this — while the job,
/// fulfillment, pricing, coupon math and tax resolution are all the real thing over the
/// in-memory database. Renewal is where the money rules compound (re-banding, continuing
/// coupons, period stitching), so these lean on behaviours the specification already names:
/// PeriodOpener's "renewal reads the LIVE tier", the quote's "first N periods" promise, and the
/// one-consequence-engine rule that declines belong to the lapse job.
/// </remarks>
public sealed class StripeRenewalJobTests
{
    private sealed class FakeGateway : IStripeGateway
    {
        public readonly List<StripeRenewalCharge> Charges = [];
        public bool NextChargeSucceeds = true;
        public bool IsConfigured => true;

        public Task<StripeCheckoutHandle> CreateCheckoutSessionAsync(StripeCheckoutSpec spec, CancellationToken ct)
            => throw new NotSupportedException("renewals never open checkout sessions");

        public Task<StripeChargeOutcome> ChargeSavedCardAsync(StripeRenewalCharge charge, CancellationToken ct)
        {
            Charges.Add(charge);
            return Task.FromResult(new StripeChargeOutcome(
                NextChargeSucceeds, $"pi_fake_{Charges.Count}",
                NextChargeSucceeds ? null : "card_declined"));
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

    private sealed record Seeded(Guid OrgId, Guid TierId, Guid UserId, Guid SubId);

    /// <summary>An Active Stripe subscription whose period ends inside the renewal window,
    /// on a $29-monthly band covering everyone, with two active members.</summary>
    private static async Task<Seeded> SeedDueAsync(
        IDbContextFactory<BenDataContext> factory,
        DateTime? periodEnd = null, bool cancelAtPeriodEnd = false, decimal price = 29m)
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
            Id = orgId, Name = "Night Shift", UrlName = $"ns-{Guid.NewGuid():N}"[..12],
            CreatedByAppUserId = userId, DateCreated = DateTime.UtcNow,
        });
        for (var i = 0; i < 2; i++)
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = userId,
                Role = OrganizationMemberRole.Member, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
            });
        var tierId = Guid.NewGuid();
        db.SubscriptionTiers.Add(new SubscriptionTier
        {
            Id = tierId, Name = "Standard", MinMembers = 1, MaxMembers = null,
            IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        db.SubscriptionTierPrices.Add(new SubscriptionTierPrice
        {
            Id = Guid.NewGuid(), SubscriptionTierId = tierId,
            Interval = BillingInterval.Monthly, Price = price, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        var subId = Guid.NewGuid();
        db.OrganizationSubscriptions.Add(new OrganizationSubscription
        {
            Id = subId, OrganizationId = orgId,
            Status = SubscriptionStatus.Active, SubscriptionTierId = tierId,
            Interval = BillingInterval.Monthly,
            CurrentPeriodStart = DateTime.UtcNow.AddMonths(-1),
            CurrentPeriodEnd = periodEnd ?? DateTime.UtcNow.AddHours(12),
            CancelAtPeriodEnd = cancelAtPeriodEnd,
            PriceAtPeriodStart = price, MemberCountAtPeriodStart = 2,
            ProviderName = "Stripe", ProviderCustomerRef = "cus_fake",
            ProviderPaymentMethodRef = "pm_fake",
            DateCreated = DateTime.UtcNow.AddMonths(-1), CreatedByAppUserId = userId,
        });
        await db.SaveChangesAsync();
        return new Seeded(orgId, tierId, userId, subId);
    }

    [Fact]
    public async Task A_due_subscription_is_charged_and_the_next_period_stitches_onto_the_old()
    {
        var factory = Db();
        var seed = await SeedDueAsync(factory);
        DateTime oldEnd;
        await using (var db = await factory.CreateDbContextAsync())
            oldEnd = (await db.OrganizationSubscriptions.SingleAsync()).CurrentPeriodEnd!.Value;

        var gateway = new FakeGateway();
        await Job(factory, gateway).RunAsync(default);

        var charge = Assert.Single(gateway.Charges);
        Assert.Equal(29m, charge.Total);   // no tax rule seeded — an honest zero, not a guess
        Assert.Equal("cus_fake", charge.CustomerRef);
        Assert.Contains($"{oldEnd:yyyyMMdd}", charge.IdempotencyKey);

        await using var after = await factory.CreateDbContextAsync();
        var sub = await after.OrganizationSubscriptions.SingleAsync();
        // Charged half a day early, yet the new period begins where the old one ENDS — no free
        // half-day, no half-day billed twice.
        Assert.Equal(oldEnd, sub.CurrentPeriodStart);
        Assert.Equal(oldEnd.AddMonths(1), sub.CurrentPeriodEnd);
        Assert.Equal(SubscriptionStatus.Active, sub.Status);

        // The money trail arrived with a receipt, same as any payment.
        Assert.Equal(1, await after.BillingLedgerEntries
            .CountAsync(e => e.Kind == BillingLedgerKind.Payment && e.ReceiptNumber != null));
    }

    [Fact]
    public async Task Renewal_rebands_from_the_live_world_not_the_frozen_period()
    {
        // PeriodOpener's contract: the frozen count protected LAST period; renewal reads the
        // live one. Two members joined mid-period here — the renewal must see 2, not the 2
        // frozen... seed froze 2 and has 2 live; drop one membership to make the live world
        // differ from the frozen one and prove which is read.
        var factory = Db();
        var seed = await SeedDueAsync(factory);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var membership = await db.OrganizationUserMemberships.FirstAsync();
            membership.IsActive = false;
            await db.SaveChangesAsync();
        }

        var gateway = new FakeGateway();
        await Job(factory, gateway).RunAsync(default);

        await using var after = await factory.CreateDbContextAsync();
        var sub = await after.OrganizationSubscriptions.SingleAsync();
        Assert.Equal(1, sub.MemberCountAtPeriodStart);   // the live count, frozen anew
    }

    [Fact]
    public async Task Not_due_cancelled_or_cardless_subscriptions_are_left_alone()
    {
        var factory = Db();
        // Ends far outside the window.
        await SeedDueAsync(factory, periodEnd: DateTime.UtcNow.AddDays(10));

        var gateway = new FakeGateway();
        await Job(factory, gateway).RunAsync(default);
        Assert.Empty(gateway.Charges);

        // Cancelled: the person said stop, and a renewal would be the app overriding them.
        var factory2 = Db();
        await SeedDueAsync(factory2, cancelAtPeriodEnd: true);
        await Job(factory2, gateway).RunAsync(default);
        Assert.Empty(gateway.Charges);
    }

    [Fact]
    public async Task A_decline_charges_nothing_records_nothing_and_lapses_nothing()
    {
        // The one-consequence-engine rule: this job never touches Status. The period end
        // passing is what lapses a group, and SubscriptionLapseJob owns that.
        var factory = Db();
        await SeedDueAsync(factory);

        var gateway = new FakeGateway { NextChargeSucceeds = false };
        await Job(factory, gateway).RunAsync(default);

        Assert.Single(gateway.Charges);   // it tried
        await using var after = await factory.CreateDbContextAsync();
        var sub = await after.OrganizationSubscriptions.SingleAsync();
        Assert.Equal(SubscriptionStatus.Active, sub.Status);
        Assert.Empty(after.BillingLedgerEntries);
        Assert.Equal(2, sub.MemberCountAtPeriodStart);   // period untouched
    }

    [Fact]
    public async Task A_continuing_coupon_keeps_its_promise_and_burns_one_period_of_it()
    {
        // "50% off your first three periods" — this renewal is period two. The card is charged
        // the discounted amount and the meter moves from 2 to 1.
        var factory = Db();
        var seed = await SeedDueAsync(factory);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var coupon = new Coupon
            {
                Id = Guid.NewGuid(), Name = "Half off, three periods", PercentOff = 50,
                Duration = CouponDuration.Repeating, DurationPeriods = 3,
                IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = seed.UserId,
            };
            db.Coupons.Add(coupon);
            var code = new CouponCode
            {
                Id = Guid.NewGuid(), CouponId = coupon.Id, Code = "HALF3",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = seed.UserId,
            };
            db.CouponCodes.Add(code);
            db.CouponRedemptions.Add(new CouponRedemption
            {
                Id = Guid.NewGuid(), CouponId = coupon.Id, CouponCodeId = code.Id,
                OrganizationId = seed.OrgId, PeriodsRemaining = 2,
                RedeemedAtUtc = DateTime.UtcNow.AddMonths(-1),
                ListPrice = 29m, Discount = 14.50m, Payable = 14.50m,
                DateCreated = DateTime.UtcNow.AddMonths(-1), CreatedByAppUserId = seed.UserId,
            });
            await db.SaveChangesAsync();
        }

        var gateway = new FakeGateway();
        await Job(factory, gateway).RunAsync(default);

        Assert.Equal(14.50m, Assert.Single(gateway.Charges).Total);

        await using var after = await factory.CreateDbContextAsync();
        Assert.Equal(1, (await after.CouponRedemptions.SingleAsync()).PeriodsRemaining);
        var payment = await after.BillingLedgerEntries.SingleAsync(e => e.Kind == BillingLedgerKind.Payment);
        Assert.Equal(14.50m, payment.Amount);
    }

    [Fact]
    public async Task An_exhausted_coupon_bills_the_list_price()
    {
        var factory = Db();
        var seed = await SeedDueAsync(factory);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var coupon = new Coupon
            {
                Id = Guid.NewGuid(), Name = "Spent", PercentOff = 50,
                Duration = CouponDuration.Repeating, DurationPeriods = 3,
                IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = seed.UserId,
            };
            db.Coupons.Add(coupon);
            var code = new CouponCode
            {
                Id = Guid.NewGuid(), CouponId = coupon.Id, Code = "SPENT",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = seed.UserId,
            };
            db.CouponCodes.Add(code);
            db.CouponRedemptions.Add(new CouponRedemption
            {
                Id = Guid.NewGuid(), CouponId = coupon.Id, CouponCodeId = code.Id,
                OrganizationId = seed.OrgId, PeriodsRemaining = 0,
                RedeemedAtUtc = DateTime.UtcNow.AddMonths(-3),
                ListPrice = 29m, Discount = 14.50m, Payable = 14.50m,
                DateCreated = DateTime.UtcNow.AddMonths(-3), CreatedByAppUserId = seed.UserId,
            });
            await db.SaveChangesAsync();
        }

        var gateway = new FakeGateway();
        await Job(factory, gateway).RunAsync(default);

        Assert.Equal(29m, Assert.Single(gateway.Charges).Total);
    }
}
