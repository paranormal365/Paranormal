using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers;
using Ben.Data.WebApi.Services.Billing.StripeIntegration;
using Ben.Service.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Redeeming the three-month trial — the ACT, where <see cref="TrialQuoteTests"/> covers the
/// number read beforehand (item 195).
/// </summary>
/// <remarks>
/// <para>Item 195 was verified on 2026-08-26 against the manual admin path. Stripe went live four
/// days later and brought a new door for the same offer, and that door has its own rule: Stripe
/// refuses a zero-amount session, so a 100%-off period must never reach it. That branch — the one
/// every trial group walks through — had no test of any kind.</para>
///
/// <para>The gateway is faked at the interface and asserted NEGATIVELY: the point is not what
/// Stripe was told, it is that Stripe was not called at all. Fulfillment, pricing, coupon math
/// and tax are the real thing over the in-memory database.</para>
/// </remarks>
public sealed class TrialCheckoutTests
{
    /// <summary>Records what it was asked to do, and refuses to pretend a $0 session is possible.</summary>
    private sealed class FakeGateway : IStripeGateway
    {
        public readonly List<StripeCheckoutSpec> Sessions = [];
        public bool IsConfigured => true;

        public Task<StripeCheckoutHandle> CreateCheckoutSessionAsync(StripeCheckoutSpec spec, CancellationToken ct)
        {
            // The real Stripe refuses this, so the fake must too — otherwise a regression that
            // sent a free period to Stripe would pass silently here and fail in production.
            Assert.True(spec.Payable > 0m, "Stripe was asked to collect nothing.");
            Sessions.Add(spec);
            return Task.FromResult(new StripeCheckoutHandle(
                $"https://checkout.stripe.test/{Sessions.Count}", $"cus_fake_{Sessions.Count}"));
        }

        public Task<StripeChargeOutcome> ChargeSavedCardAsync(StripeRenewalCharge charge, CancellationToken ct)
            => throw new NotSupportedException("checkout never charges a saved card");

        public StripeCompletedCheckout? ParseCompletedCheckout(string payload, string signatureHeader)
            => throw new NotSupportedException();
    }

    private static IDbContextFactory<BenDataContext> CreateFactory()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed record World(IDbContextFactory<BenDataContext> F, Guid OrgId, Guid OwnerId, Guid TierId);

    /// <summary>A $15-monthly band covering everyone, and an owner who may spend the group's money.</summary>
    private static async Task<World> SeedAsync()
    {
        var f = CreateFactory();
        Guid orgId = Guid.NewGuid(), ownerId = Guid.NewGuid(), tierId = Guid.NewGuid();

        await using var db = await f.CreateDbContextAsync();
        db.Users.Add(new AppUser { Id = ownerId, UserName = "o@t.com", DateCreated = DateTime.UtcNow });
        db.Organizations.Add(new Organization
        {
            Id = orgId, Name = "G", UrlName = $"g-{orgId:N}",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
        });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = ownerId,
            Role = OrganizationMemberRole.Owner, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
        });
        db.SubscriptionTiers.Add(new SubscriptionTier
        {
            // MinMembers 1, or the resolver refuses the whole ladder with a 503 — the trap
            // item 195 recorded after seeding the quote tests.
            Id = tierId, Name = "Small group", MinMembers = 1, MaxMembers = null,
            SortOrder = 1, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
        });
        db.SubscriptionTierPrices.Add(new SubscriptionTierPrice
        {
            Id = Guid.NewGuid(), SubscriptionTierId = tierId,
            Interval = BillingInterval.Monthly, Price = 15.00m, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
        });
        await db.SaveChangesAsync();
        return new World(f, orgId, ownerId, tierId);
    }

    /// <summary>The trial as Ben described it: 100% off, three periods, monthly only.</summary>
    private static async Task<string> SeedTrialCouponAsync(World w, int percentOff = 100, int periods = 3)
    {
        const string code = "TRIAL3";
        await using var db = await w.F.CreateDbContextAsync();
        var couponId = Guid.NewGuid();
        db.Coupons.Add(new Coupon
        {
            Id = couponId, Name = "Three months free", Kind = CouponKind.Shared,
            PercentOff = percentOff,
            Duration = CouponDuration.Repeating, DurationPeriods = periods,
            AppliesToInterval = BillingInterval.Monthly, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.OwnerId,
        });
        db.CouponCodes.Add(new CouponCode
        {
            Id = Guid.NewGuid(), CouponId = couponId, Code = code, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.OwnerId,
        });
        await db.SaveChangesAsync();
        return code;
    }

    private static OrganizationCheckoutController Build(World w, FakeGateway gateway)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AppBaseUrl"] = "https://ishaunted.test" })
            .Build();
        var ctrl = new OrganizationCheckoutController(
            w.F, new Mock<IMapper>().Object,
            new Ben.Service.RepositoryService.Services.OrganizationSecurityService(w.F),
            gateway,
            new StripeFulfillmentService(w.F, NullLogger<StripeFulfillmentService>.Instance),
            config);
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, w.OwnerId.ToString())], "Bearer")),
            },
        };
        return ctrl;
    }

    private static async Task<StartCheckoutResponse> StartAsync(World w, FakeGateway gateway, string? code)
    {
        var result = (await Build(w, gateway).Start(w.OrgId,
            new StartCheckoutRequest(BillingInterval.Monthly, code), default)).Result;

        // Reported rather than asserted blindly: a refusal here is a 400 or a 503 carrying the
        // reason, and "expected OkObjectResult" tells nobody which.
        if (result is ObjectResult { Value: not StartCheckoutResponse } other)
            Assert.Fail($"checkout refused: HTTP {other.StatusCode} — {other.Value}");

        return Assert.IsType<StartCheckoutResponse>(Assert.IsType<OkObjectResult>(result).Value);
    }

    // ── the free period never reaches Stripe ─────────────────────────────────

    [Fact]
    public async Task A_hundred_percent_off_period_never_opens_a_stripe_session()
    {
        var w = await SeedAsync();
        var code = await SeedTrialCouponAsync(w);
        var gateway = new FakeGateway();

        var response = await StartAsync(w, gateway, code);

        Assert.True(response.PaidWithoutCharge);
        Assert.Empty(gateway.Sessions);                       // the whole point
        Assert.Contains("checkout=free", response.RedirectUrl);
        Assert.DoesNotContain("stripe", response.RedirectUrl, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_free_checkout_opens_a_real_subscription_period()
    {
        var w = await SeedAsync();
        var code = await SeedTrialCouponAsync(w);

        await StartAsync(w, gateway: new FakeGateway(), code);

        await using var db = await w.F.CreateDbContextAsync();
        var sub = await db.OrganizationSubscriptions.SingleAsync();
        Assert.Equal(SubscriptionStatus.Active, sub.Status);
        Assert.Equal(w.TierId, sub.SubscriptionTierId);
        Assert.Equal(0m, sub.PriceAtPeriodStart);             // free, and recorded as free
        Assert.NotNull(sub.CurrentPeriodEnd);
        Assert.Equal(sub.CurrentPeriodStart!.Value.AddMonths(1), sub.CurrentPeriodEnd!.Value);
    }

    /// <summary>
    /// A free period still appears in the money trail — a three-month hole in a group's billing
    /// history is exactly what item 195 was written to prevent.
    /// </summary>
    [Fact]
    public async Task The_free_period_is_recorded_as_a_zero_charge_naming_the_coupon()
    {
        var w = await SeedAsync();
        var code = await SeedTrialCouponAsync(w);

        await StartAsync(w, gateway: new FakeGateway(), code);

        await using var db = await w.F.CreateDbContextAsync();
        var entry = Assert.Single(await db.BillingLedgerEntries.ToListAsync());
        Assert.Equal(BillingLedgerKind.Charge, entry.Kind);
        Assert.Equal(0m, entry.Amount);
        Assert.Equal(0m, entry.TaxAmount);                    // nothing owed, nothing taxed
        Assert.Contains(code, entry.Description);
        Assert.Null(entry.ReceiptNumber);                     // nothing was paid, so nothing is receipted
    }

    /// <summary>
    /// "Your first three months are free" is a promise about months two and three, and the
    /// redemption is where that promise is kept.
    /// </summary>
    [Fact]
    public async Task The_redemption_reserves_the_remaining_free_periods()
    {
        var w = await SeedAsync();
        var code = await SeedTrialCouponAsync(w, periods: 3);

        await StartAsync(w, gateway: new FakeGateway(), code);

        await using var db = await w.F.CreateDbContextAsync();
        var redemption = Assert.Single(await db.CouponRedemptions.ToListAsync());
        Assert.Equal(2, redemption.PeriodsRemaining);         // this month spent, two still owed
        Assert.Equal(15.00m, redemption.ListPrice);           // frozen, so a later price edit cannot rewrite it
        Assert.Equal(15.00m, redemption.Discount);
        Assert.Equal(0m, redemption.Payable);
        Assert.Equal(1, (await db.CouponCodes.SingleAsync()).RedemptionCount);
    }

    // ── and the free branch does not swallow paid checkouts ──────────────────

    [Fact]
    public async Task A_partial_discount_still_goes_to_stripe()
    {
        var w = await SeedAsync();
        var code = await SeedTrialCouponAsync(w, percentOff: 50);
        var gateway = new FakeGateway();

        var response = await StartAsync(w, gateway, code);

        Assert.False(response.PaidWithoutCharge);
        var session = Assert.Single(gateway.Sessions);
        Assert.Equal(7.50m, session.Payable);
        Assert.StartsWith("https://checkout.stripe.test/", response.RedirectUrl);

        // Nothing is fulfilled yet — the webhook does that when the card actually clears.
        await using var db = await w.F.CreateDbContextAsync();
        Assert.Empty(await db.BillingLedgerEntries.ToListAsync());
    }

    [Fact]
    public async Task No_coupon_at_all_still_goes_to_stripe_at_the_full_price()
    {
        var w = await SeedAsync();
        var gateway = new FakeGateway();

        var response = await StartAsync(w, gateway, code: null);

        Assert.False(response.PaidWithoutCharge);
        Assert.Equal(15.00m, Assert.Single(gateway.Sessions).Payable);
    }
}
