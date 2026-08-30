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
/// "Stripe says they paid" becomes exactly what the manual admin path would have done.
/// </summary>
/// <remarks>
/// The webhook can be delivered twice, arrive for a session that is not ours, or name a tier an
/// admin has since deleted — and money is the one domain where each of those must land somewhere
/// deliberate. The manual path is the specification; where these tests assert a behaviour, it is
/// the behaviour AdminOrganizationSubscriptionController.Set already has.
/// </remarks>
public sealed class StripeFulfillmentTests
{
    private static IDbContextFactory<BenDataContext> Db()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static StripeFulfillmentService Service(IDbContextFactory<BenDataContext> db)
        => new(db, NullLogger<StripeFulfillmentService>.Instance);

    private sealed record Seeded(Guid OrgId, Guid TierId, Guid UserId);

    private static async Task<Seeded> SeedAsync(IDbContextFactory<BenDataContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        var userId = Guid.NewGuid();
        db.AppUsers.Add(new AppUser
        {
            Id = userId, UserName = "owner@test.com", NormalizedUserName = "OWNER@TEST.COM",
            Email = "owner@test.com", DisplayName = "Owner", DateCreated = DateTime.UtcNow,
        });
        var orgId = Guid.NewGuid();
        db.Organizations.Add(new Organization
        {
            Id = orgId, Name = "Paranormal 365", UrlName = $"p365-{Guid.NewGuid():N}"[..14],
            CreatedByAppUserId = userId, DateCreated = DateTime.UtcNow,
        });
        var tierId = Guid.NewGuid();
        db.SubscriptionTiers.Add(new SubscriptionTier
        {
            Id = tierId, Name = "Up to 10", MinMembers = 1, MaxMembers = 10,
            IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        db.SubscriptionTierPrices.Add(new SubscriptionTierPrice
        {
            Id = Guid.NewGuid(), SubscriptionTierId = tierId,
            Interval = BillingInterval.Monthly, Price = 29m, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        await db.SaveChangesAsync();
        return new Seeded(orgId, tierId, userId);
    }

    private static Facts FactsFor(Seeded seed,
        decimal payable = 29m, decimal taxRate = 9.75m, decimal tax = 2.83m,
        string? coupon = null, decimal? list = null, decimal discount = 0m)
        => new(seed.OrgId, seed.TierId, BillingInterval.Monthly, 7,
               payable, taxRate, tax, seed.UserId, coupon, list ?? payable, discount);

    private static StripeCompletedCheckout Completed(Facts facts,
        string session = "cs_test_1", string? intent = "pi_test_1")
        => new(session, intent, "cus_test_1", "pm_test_1", facts.ToMetadata());

    // ── the paid path ─────────────────────────────────────────────────────────

    [Fact]
    public async Task A_paid_checkout_opens_the_period_and_writes_the_whole_money_trail()
    {
        var factory = Db();
        var seed = await SeedAsync(factory);

        await Service(factory).FulfillAsync(Completed(FactsFor(seed)));

        await using var db = await factory.CreateDbContextAsync();
        var sub = await db.OrganizationSubscriptions.SingleAsync();
        Assert.Equal(SubscriptionStatus.Active, sub.Status);
        Assert.Equal(seed.TierId, sub.SubscriptionTierId);
        Assert.Equal(29m, sub.PriceAtPeriodStart);
        Assert.Equal(7, sub.MemberCountAtPeriodStart);
        // The interval enum IS the month count — one month here, and the same arithmetic
        // yearly would give twelve.
        Assert.Equal(sub.CurrentPeriodStart!.Value.AddMonths(1), sub.CurrentPeriodEnd);
        Assert.Equal("Stripe", sub.ProviderName);
        Assert.Equal("cus_test_1", sub.ProviderCustomerRef);
        Assert.Equal("pm_test_1", sub.ProviderPaymentMethodRef);

        var charge = await db.BillingLedgerEntries.SingleAsync(e => e.Kind == BillingLedgerKind.Charge);
        var payment = await db.BillingLedgerEntries.SingleAsync(e => e.Kind == BillingLedgerKind.Payment);

        // The frozen-tax rule: rate and dollars from checkout creation, never recomputed.
        Assert.Equal(9.75m, charge.TaxRatePercent);
        Assert.Equal(2.83m, charge.TaxAmount);
        Assert.Equal("pi_test_1", payment.PaymentReference);
        Assert.NotNull(payment.ReceiptNumber);   // a payment without a receipt is unanswerable later

        // A contract snapshot exists and records what was actually charged.
        var snapshot = await db.SubscriptionContractTerms.SingleAsync();
        Assert.Equal(29m, snapshot.Price);
    }

    [Fact]
    public async Task Delivered_twice_is_fulfilled_once()
    {
        // Stripe retries until acknowledged, and acknowledgement can be lost AFTER the work is
        // done — so the second delivery must find the first, not repeat it. One period, one
        // receipt, however many deliveries.
        var factory = Db();
        var seed = await SeedAsync(factory);
        var checkout = Completed(FactsFor(seed));

        await Service(factory).FulfillAsync(checkout);
        await Service(factory).FulfillAsync(checkout);

        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(1, await db.BillingLedgerEntries.CountAsync(e => e.Kind == BillingLedgerKind.Payment));
        Assert.Equal(1, await db.BillingLedgerEntries.CountAsync(e => e.Kind == BillingLedgerKind.Charge));
        Assert.Equal(1, await db.SubscriptionContractTerms.CountAsync());
    }

    [Fact]
    public async Task A_session_that_is_not_ours_is_ignored_not_guessed_at()
    {
        var factory = Db();
        await SeedAsync(factory);

        await Service(factory).FulfillAsync(new StripeCompletedCheckout(
            "cs_stranger", "pi_stranger", null, null,
            new Dictionary<string, string> { ["something"] = "else" }));

        await using var db = await factory.CreateDbContextAsync();
        Assert.Empty(db.OrganizationSubscriptions);
        Assert.Empty(db.BillingLedgerEntries);
    }

    [Fact]
    public async Task Reactivating_a_lapsed_group_rearms_the_stranded_client_notice()
    {
        var factory = Db();
        var seed = await SeedAsync(factory);
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.OrganizationSubscriptions.Add(new OrganizationSubscription
            {
                Id = Guid.NewGuid(), OrganizationId = seed.OrgId,
                Status = SubscriptionStatus.Lapsed, LapsedAtUtc = DateTime.UtcNow.AddDays(-10),
                StrandedClientNoticeSentAtUtc = DateTime.UtcNow.AddDays(-3),
                DateCreated = DateTime.UtcNow.AddMonths(-2), CreatedByAppUserId = seed.UserId,
            });
            await db.SaveChangesAsync();
        }

        await Service(factory).FulfillAsync(Completed(FactsFor(seed)));

        await using var after = await factory.CreateDbContextAsync();
        var sub = await after.OrganizationSubscriptions.SingleAsync();
        Assert.Equal(SubscriptionStatus.Active, sub.Status);
        // Re-armed so a FUTURE lapse warns the clients again — the item 184 Phase D promise.
        Assert.Null(sub.StrandedClientNoticeSentAtUtc);
    }

    // ── the free-coupon path ──────────────────────────────────────────────────

    [Fact]
    public async Task A_free_period_gets_its_charge_row_and_no_receipt_because_nothing_was_paid()
    {
        // Item 195's rule: a 100%-off period still appears in the ledger — a zero charge naming
        // its coupon — or September becomes a hole nobody can explain. But nothing was PAID, so
        // no payment row and no receipt number.
        var factory = Db();
        var seed = await SeedAsync(factory);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var coupon = new Coupon
            {
                Id = Guid.NewGuid(), Name = "Launch trial", PercentOff = 100,
                IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = seed.UserId,
            };
            db.Coupons.Add(coupon);
            db.CouponCodes.Add(new CouponCode
            {
                Id = Guid.NewGuid(), CouponId = coupon.Id, Code = "LAUNCH100",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = seed.UserId,
            });
            await db.SaveChangesAsync();
        }

        await Service(factory).FulfillAsync(new StripeCompletedCheckout(
            $"free-{Guid.NewGuid():N}", null, null, null,
            FactsFor(seed, payable: 0m, tax: 0m, coupon: "LAUNCH100", list: 29m, discount: 29m)
                .ToMetadata()));

        await using var check = await factory.CreateDbContextAsync();
        var charge = await check.BillingLedgerEntries.SingleAsync();
        Assert.Equal(BillingLedgerKind.Charge, charge.Kind);
        Assert.Equal(0m, charge.Amount);
        Assert.Contains("LAUNCH100", charge.Description);
        Assert.Null(charge.ReceiptNumber);

        var sub = await check.OrganizationSubscriptions.SingleAsync();
        Assert.Equal(SubscriptionStatus.Active, sub.Status);
        Assert.Equal(0m, sub.PriceAtPeriodStart);

        // The redemption froze the real economics — reimbursement math survives price edits.
        var redemption = await check.CouponRedemptions.SingleAsync();
        Assert.Equal(29m, redemption.ListPrice);
        Assert.Equal(29m, redemption.Discount);
        Assert.Equal(0m, redemption.Payable);
    }

    // ── the metadata contract ─────────────────────────────────────────────────

    [Fact]
    public void The_facts_survive_the_round_trip_through_stripe_metadata()
    {
        var facts = new Facts(Guid.NewGuid(), Guid.NewGuid(), BillingInterval.Yearly, 23,
            Payable: 261.25m, TaxRatePercent: 9.75m, TaxAmount: 25.47m,
            Guid.NewGuid(), "HALFOFF", ListPrice: 522.50m, Discount: 261.25m);

        Assert.Equal(facts, Facts.FromMetadata(facts.ToMetadata()));
    }

    [Fact]
    public void Torn_metadata_reads_as_null_never_as_a_guess()
    {
        var metadata = FactsForTorn().ToMetadata();
        metadata.Remove(Facts.Keys.Payable);
        Assert.Null(Facts.FromMetadata(metadata));

        static Facts FactsForTorn() => new(Guid.NewGuid(), Guid.NewGuid(),
            BillingInterval.Monthly, 1, 29m, 0m, 0m, Guid.NewGuid(), null, 29m, 0m);
    }
}
