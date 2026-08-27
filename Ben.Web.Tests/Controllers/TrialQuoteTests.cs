using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers;
using Ben.Service.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// What a group is shown BEFORE it subscribes to the three-month trial (item 195).
/// </summary>
/// <remarks>
/// <para>Item 195 asked for the 100%-off trial to be proven rather than built, and the ledger half
/// was: a zero-value period could not be recorded at all until it was fixed. This is the half in
/// front of it — the quote, which is the number a group actually reads before deciding. It had no
/// tests of any kind, and "your first three months are free" is the one figure that must not be
/// wrong the week it goes on sale.</para>
///
/// <para>Quoting deliberately mutates nothing, so these seed a coupon and ask; nothing here
/// redeems.</para>
/// </remarks>
public class TrialQuoteTests
{
    private static IDbContextFactory<BenDataContext> CreateFactory()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed record World(IDbContextFactory<BenDataContext> F, Guid OrgId, Guid OwnerId, Guid TierId);

    /// <summary>A paid tier at 15/month, and an owner who may ask for a quote.</summary>
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
            // Resolved by MEMBER COUNT, not passed in — the quote asks the ladder, so the band
            // has to cover the one owner this org has. MinMembers is 1 because the resolver
            // refuses a ladder whose lowest band does not start there (a 503, not a bad request).
            Id = tierId, Name = "Small group", MinMembers = 1, MaxMembers = null,
            SortOrder = 1, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
        });
        db.SubscriptionTierPrices.Add(new SubscriptionTierPrice
        {
            Id = Guid.NewGuid(), SubscriptionTierId = tierId,
            Interval = BillingInterval.Monthly, Price = 15.00m,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
        });
        await db.SaveChangesAsync();
        return new World(f, orgId, ownerId, tierId);
    }

    /// <summary>The trial as Ben described it: 100% off, three periods, monthly only.</summary>
    private static async Task<string> SeedTrialCouponAsync(
        World w, int percentOff = 100, int periods = 3,
        DateTime? validFrom = null, DateTime? redeemBy = null, bool isActive = true)
    {
        const string code = "TRIAL3";
        await using var db = await w.F.CreateDbContextAsync();
        var couponId = Guid.NewGuid();
        db.Coupons.Add(new Coupon
        {
            Id = couponId, Name = "Three months free", Kind = CouponKind.Shared,
            PercentOff = percentOff,
            Duration = CouponDuration.Repeating, DurationPeriods = periods,
            AppliesToInterval = BillingInterval.Monthly,
            ValidFromUtc = validFrom, RedeemByUtc = redeemBy, IsActive = isActive,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.OwnerId,
        });
        db.CouponCodes.Add(new CouponCode
        {
            Id = Guid.NewGuid(), CouponId = couponId, Code = code,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.OwnerId,
        });
        await db.SaveChangesAsync();
        return code;
    }

    private static OrganizationSubscriptionController Build(World w)
    {
        var ctrl = new OrganizationSubscriptionController(
            w.F, new Mock<IMapper>().Object,
            new Ben.Service.RepositoryService.Services.OrganizationSecurityService(w.F));
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

    private static async Task<SubscriptionQuoteResponse> QuoteAsync(World w, string? code)
    {
        var result = (await Build(w).Quote(w.OrgId,
            new SubscriptionQuoteRequest(BillingInterval.Monthly, code), default)).Result;

        // Reported rather than asserted blindly: a refusal here is a 503 or a 400 carrying the
        // reason, and "expected OkObjectResult" tells nobody which.
        if (result is ObjectResult { Value: not SubscriptionQuoteResponse } other)
            Assert.Fail($"quote refused: HTTP {other.StatusCode} — {other.Value}");

        return Assert.IsType<SubscriptionQuoteResponse>(Assert.IsType<OkObjectResult>(result).Value);
    }

    // ── the number the group reads ───────────────────────────────────────────

    [Fact]
    public async Task A_hundred_percent_off_quotes_nothing_payable_and_no_tax()
    {
        var w = await SeedAsync();
        var code = await SeedTrialCouponAsync(w);

        var quote = await QuoteAsync(w, code);

        Assert.Equal(15.00m, quote.ListPrice);
        Assert.Equal(15.00m, quote.Discount);
        Assert.Equal(0m, quote.Payable);
        Assert.Equal(0m, quote.Tax);      // nothing owed, nothing taxed
        Assert.Null(quote.CouponRefusedBecause);
    }

    /// <summary>Three periods, said out loud — this is "your first three months".</summary>
    [Fact]
    public async Task The_quote_says_how_many_periods_the_trial_covers()
    {
        var w = await SeedAsync();
        var code = await SeedTrialCouponAsync(w, periods: 3);

        Assert.Equal(3, (await QuoteAsync(w, code)).CouponAppliesForPeriods);
    }

    [Fact]
    public async Task Without_a_code_the_full_price_is_quoted()
    {
        var w = await SeedAsync();
        await SeedTrialCouponAsync(w);

        var quote = await QuoteAsync(w, null);

        Assert.Equal(15.00m, quote.Payable);
        Assert.Equal(0m, quote.Discount);
        Assert.Null(quote.CouponAppliesForPeriods);
    }

    // ── the window Ben sets when the offer opens and closes ──────────────────

    /// <summary>
    /// Before 1 September the code is not live yet, and the quote says so rather than discounting.
    /// </summary>
    /// <remarks>
    /// The reason ValidFromUtc exists: a campaign can be created early and go live on its own,
    /// with nobody remembering to flip a switch at the right hour. Worth proving, because a
    /// coupon that discounted a day early would be found by a customer, not by us.
    /// </remarks>
    [Fact]
    public async Task A_trial_that_has_not_opened_yet_does_not_discount()
    {
        var w = await SeedAsync();
        var code = await SeedTrialCouponAsync(w, validFrom: DateTime.UtcNow.AddDays(5));

        var quote = await QuoteAsync(w, code);

        Assert.Equal(15.00m, quote.Payable);
        Assert.NotNull(quote.CouponRefusedBecause);
    }

    /// <summary>And after the window closes it stops, without anyone switching it off.</summary>
    [Fact]
    public async Task A_trial_past_its_redeem_by_date_does_not_discount()
    {
        var w = await SeedAsync();
        var code = await SeedTrialCouponAsync(w, redeemBy: DateTime.UtcNow.AddDays(-1));

        var quote = await QuoteAsync(w, code);

        Assert.Equal(15.00m, quote.Payable);
        Assert.NotNull(quote.CouponRefusedBecause);
    }

    /// <summary>
    /// A code that does not exist and one that was withdrawn read identically.
    /// </summary>
    /// <remarks>
    /// Deliberate: distinguishing them would let anybody probe which strings are real codes.
    /// </remarks>
    [Fact]
    public async Task A_wrong_code_and_a_withdrawn_code_say_the_same_thing()
    {
        var w = await SeedAsync();
        var live = await SeedTrialCouponAsync(w, isActive: false);

        var wrong = await QuoteAsync(w, "NOSUCHCODE");
        var dead  = await QuoteAsync(w, live);

        Assert.NotNull(wrong.CouponRefusedBecause);
        Assert.NotNull(dead.CouponRefusedBecause);
        Assert.Equal(wrong.CouponRefusedBecause, dead.CouponRefusedBecause);
        Assert.Equal(15.00m, wrong.Payable);
        Assert.Equal(15.00m, dead.Payable);
    }
}
