using AutoMapper;
using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers;
using Ben.Data.WebApi.Controllers.Admin;
using Ben.Data.WebApi.Services;
using Ben.Service.Models.Admin;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// The two screens that make an event credit visible: a group's billing card, and the SuperAdmin
/// refund (item 235, phase 1B.4 and 1B.5).
/// </summary>
/// <remarks>
/// <para>Both exist because the alternative is a rule only the database can see.
/// <c>RefundedUtc</c> was a column nothing could write, and a credit somebody had bought was
/// invisible everywhere on the site until it was spent — so "have we paid for October?" had no
/// answer and "we bought two by mistake" had no remedy.</para>
/// </remarks>
public sealed class EventCreditEndpointTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid AdminId = Guid.NewGuid();
    private static readonly Guid OrgId   = Guid.NewGuid();

    private static IDbContextFactory<BenDataContext> CreateFactory()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<IDbContextFactory<BenDataContext>> SeedAsync(params EventCredit[] credits)
    {
        var f = CreateFactory();
        await using var db = await f.CreateDbContextAsync();

        db.Users.Add(new AppUser
        {
            Id = OwnerId, UserName = "owner@test.com", NormalizedUserName = "OWNER@TEST.COM",
            Email = "owner@test.com", DisplayName = "Pat Owner", DateCreated = DateTime.UtcNow,
        });
        db.Users.Add(new AppUser
        {
            Id = AdminId, UserName = "admin@test.com", NormalizedUserName = "ADMIN@TEST.COM",
            Email = "admin@test.com", DisplayName = "The Admin", DateCreated = DateTime.UtcNow,
        });
        db.Organizations.Add(new Organization
        {
            Id = OrgId, Name = "Thomas House", UrlName = "thomas-house",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
        });
        db.EventCredits.AddRange(credits);
        await db.SaveChangesAsync();
        return f;
    }

    private static EventCredit Credit(
        DateTime? expires = null, DateTime? spent = null, DateTime? refunded = null,
        Guid? spentOn = null)
        => new()
        {
            Id = Guid.NewGuid(),
            OwnerOrganizationId = OrgId,
            PriceAtPurchase = 99m,
            Currency = "USD",
            PurchasedUtc = DateTime.UtcNow.AddDays(-1),
            ExpiresUtc = expires ?? DateTime.UtcNow.AddMonths(11),
            SpentUtc = spent,
            SpentOnHostedEventId = spentOn,
            RefundedUtc = refunded,
            DateCreated = DateTime.UtcNow.AddDays(-1),
            CreatedByAppUserId = OwnerId,
        };

    // ── The group's card ──────────────────────────────────────────────────────

    private static OrganizationBillingController BuildBilling(
        IDbContextFactory<BenDataContext> f, bool mayRead = true)
    {
        var security = new Mock<IOrganizationSecurityService>();
        security.Setup(s => s.HasAccessAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<OrganizationSecurityTable>(),
                It.IsAny<OrganizationSecurityAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mayRead);

        var services = new ServiceCollection();
        services.AddSingleton(f);
        services.AddSingleton(new SiteSettingsService(f));

        return new OrganizationBillingController(f, new Mock<IMapper>().Object, security.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, OwnerId.ToString())],
                        "Bearer", ClaimTypes.NameIdentifier, ClaimTypes.Role)),
                    RequestServices = services.BuildServiceProvider(),
                },
            },
        };
    }

    private static OrgEventCreditsView View(ActionResult<OrgEventCreditsView> result)
        => Assert.IsType<OrgEventCreditsView>(Assert.IsType<OkObjectResult>(result.Result).Value);

    [Fact]
    public async Task The_card_counts_only_the_credits_that_can_actually_be_spent()
    {
        // Four credits, one usable. A card that counted rows would tell somebody they hold four
        // events' worth of credit and then refuse to publish one.
        var f = await SeedAsync(
            Credit(),
            Credit(spent: DateTime.UtcNow.AddDays(-2), spentOn: null),
            Credit(refunded: DateTime.UtcNow.AddDays(-2)),
            Credit(expires: DateTime.UtcNow.AddDays(-1)));

        var view = View(await BuildBilling(f).GetEventCredits(OrgId, default));

        Assert.Equal(1, view.Spendable);
        // And all four are still listed: "did we pay for that weekend?" is asked about spent ones.
        Assert.Equal(4, view.Credits.Count);
    }

    [Fact]
    public async Task The_card_carries_the_price_so_the_buy_control_is_not_a_guess()
    {
        var f = await SeedAsync();

        var view = View(await BuildBilling(f).GetEventCredits(OrgId, default));

        Assert.True(view.OnSale);
        Assert.Equal(99m, view.UnitPrice);
    }

    [Fact]
    public async Task Turning_credits_off_closes_the_buy_control_and_leaves_the_held_ones_alone()
    {
        // The switch a SuperAdmin flips when credits come off sale. What a group already bought
        // still works — the card says so — because withdrawing a product must not confiscate it.
        var f = await SeedAsync(Credit());
        await using (var db = await f.CreateDbContextAsync())
        {
            db.SiteSettings.Add(new SiteSetting
            {
                Id = Guid.NewGuid(), Key = SiteSettingKeys.EventCreditsEnabled, Value = "false",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
            });
            await db.SaveChangesAsync();
        }

        var view = View(await BuildBilling(f).GetEventCredits(OrgId, default));

        Assert.False(view.OnSale);
        Assert.Equal(1, view.Spendable);
    }

    [Fact]
    public async Task Somebody_without_the_settings_key_is_refused_rather_than_shown_nothing()
    {
        // A refusal and an empty list look identical on a card, and only one of them is somebody's
        // money. The client renders the refusal; it can only do that if the server sends one.
        var f = await SeedAsync(Credit());

        var result = await BuildBilling(f, mayRead: false).GetEventCredits(OrgId, default);

        Assert.IsType<ForbidResult>(result.Result);
    }

    // ── The SuperAdmin refund ─────────────────────────────────────────────────

    private static AdminBillingController BuildAdmin(IDbContextFactory<BenDataContext> f)
        => new(f)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, AdminId.ToString()),
                         new Claim(ClaimTypes.Role, RoleNames.SuperAdmin)],
                        "Bearer", ClaimTypes.NameIdentifier, ClaimTypes.Role)),
                },
            },
        };

    [Fact]
    public async Task Granted_credits_land_in_the_group_s_hands_and_cost_nothing()
    {
        var f = await SeedAsync();

        var result = await BuildAdmin(f).GrantEventCredits(
            OrgId, new GrantEventCreditsRequest(3, "Payment taken but never landed"), default);

        var rows = Assert.IsAssignableFrom<IEnumerable<AdminEventCreditRecord>>(
            Assert.IsType<OkObjectResult>(result.Result).Value).ToList();
        Assert.Equal(3, rows.Count);
        Assert.All(rows, r => Assert.Equal("Payment taken but never landed", r.GrantedReason));
        Assert.All(rows, r => Assert.Equal(0m, r.PriceAtPurchase));

        await using var db = await f.CreateDbContextAsync();
        // Ordinary in every other way: a year to use, and spendable right now.
        var held = await db.EventCredits.ToListAsync();
        Assert.Equal(3, held.Count(c => c.IsSpendable(DateTime.UtcNow)));
        Assert.All(held, c => Assert.True(c.ExpiresUtc > DateTime.UtcNow.AddDays(360)));
    }

    [Fact]
    public async Task A_grant_writes_nothing_to_the_ledger()
    {
        // A $0 charge and payment pair would put a sale that never happened into the money trail,
        // and a receipt would say somebody paid nothing. The reason and the admin are the record.
        var f = await SeedAsync();

        await BuildAdmin(f).GrantEventCredits(
            OrgId, new GrantEventCreditsRequest(2, "Apology"), default);

        await using var db = await f.CreateDbContextAsync();
        Assert.Equal(0, await db.BillingLedgerEntries.CountAsync());
        Assert.All(await db.EventCredits.ToListAsync(), c => Assert.Null(c.ReceiptNumber));
        Assert.All(await db.EventCredits.ToListAsync(), c => Assert.Null(c.ProviderPaymentRef));
    }

    [Fact]
    public async Task A_grant_with_no_reason_is_refused()
    {
        var f = await SeedAsync();

        var result = await BuildAdmin(f).GrantEventCredits(
            OrgId, new GrantEventCreditsRequest(1, "  "), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);

        await using var db = await f.CreateDbContextAsync();
        Assert.Equal(0, await db.EventCredits.CountAsync());
    }

    [Fact]
    public async Task A_grant_is_clamped_rather_than_refused()
    {
        // A typed 500 is a slip, not an instruction. The server clamps to the same ceiling the
        // purchase uses so an admin never hands out twenty-five times what they meant to.
        var f = await SeedAsync();

        var result = await BuildAdmin(f).GrantEventCredits(
            OrgId, new GrantEventCreditsRequest(500, "Fat fingers"), default);

        var rows = Assert.IsAssignableFrom<IEnumerable<AdminEventCreditRecord>>(
            Assert.IsType<OkObjectResult>(result.Result).Value).ToList();
        Assert.Equal(Ben.Data.WebApi.Services.Events.EventCredits.MaximumPerPurchase, rows.Count);
    }

    [Fact]
    public async Task Refunding_a_granted_credit_revokes_it_without_a_ledger_row()
    {
        // Refunding something that cost nothing means revoking it. A $0 adjustment would only add
        // a line to the money trail saying no money moved.
        var f = await SeedAsync();
        var granted = await BuildAdmin(f).GrantEventCredits(
            OrgId, new GrantEventCreditsRequest(1, "Sent to the wrong group"), default);
        var creditId = Assert.IsAssignableFrom<IEnumerable<AdminEventCreditRecord>>(
            Assert.IsType<OkObjectResult>(granted.Result).Value).Single().Id;

        var result = await BuildAdmin(f).RefundEventCredit(
            creditId, new RefundEventCreditRequest("Granted in error"), default);

        Assert.IsType<OkObjectResult>(result.Result);

        await using var db = await f.CreateDbContextAsync();
        Assert.False((await db.EventCredits.SingleAsync()).IsSpendable(DateTime.UtcNow));
        Assert.Equal(0, await db.BillingLedgerEntries.CountAsync());
    }

    [Fact]
    public async Task Refunding_a_credit_stops_it_ever_being_spent()
    {
        var credit = Credit();
        var f = await SeedAsync(credit);

        var result = await BuildAdmin(f).RefundEventCredit(
            credit.Id, new RefundEventCreditRequest("Bought twice by mistake"), default);

        var record = Assert.IsType<AdminEventCreditRecord>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.NotNull(record.RefundedUtc);

        await using var db = await f.CreateDbContextAsync();
        var row = await db.EventCredits.SingleAsync(c => c.Id == credit.Id);
        Assert.False(row.IsSpendable(DateTime.UtcNow));
        Assert.Equal("Bought twice by mistake", row.RefundedReason);
        Assert.Equal(AdminId, row.RefundedByAppUserId);
    }

    [Fact]
    public async Task A_refund_writes_the_credit_adjustment_on_the_ledger()
    {
        // A refund nobody recorded is a hole in the money trail — the ledger is append-only, so
        // the correction is a row, not an edit.
        var credit = Credit();
        var f = await SeedAsync(credit);

        await BuildAdmin(f).RefundEventCredit(
            credit.Id, new RefundEventCreditRequest("Support ticket 412"), default);

        await using var db = await f.CreateDbContextAsync();
        var entry = await db.BillingLedgerEntries.SingleAsync();
        Assert.Equal(BillingLedgerKind.Adjustment, entry.Kind);
        Assert.True(entry.AdjustmentIsCredit);
        Assert.Equal(99m, entry.Amount);
        Assert.Contains("Support ticket 412", entry.Description);
    }

    [Fact]
    public async Task A_refund_put_through_Stripe_can_skip_the_ledger_row()
    {
        var credit = Credit();
        var f = await SeedAsync(credit);

        await BuildAdmin(f).RefundEventCredit(
            credit.Id, new RefundEventCreditRequest("Refunded in Stripe", RecordAdjustment: false), default);

        await using var db = await f.CreateDbContextAsync();
        Assert.Equal(0, await db.BillingLedgerEntries.CountAsync());
        Assert.NotNull(await db.EventCredits.Select(c => c.RefundedUtc).SingleAsync());
    }

    [Fact]
    public async Task A_spent_credit_is_refused_and_the_refusal_names_the_event()
    {
        // One event, one credit, for the life of that event. Handing back the credit for a live
        // event would make its own payment record untrue, and re-publishing it would then never
        // charge again.
        var eventId = Guid.NewGuid();
        var credit = Credit(spent: DateTime.UtcNow.AddDays(-2), spentOn: eventId);
        var f = await SeedAsync(credit);
        await using (var db = await f.CreateDbContextAsync())
        {
            db.HostedEvents.Add(new HostedEvent
            {
                Id = eventId, OrganizationId = OrgId, Name = "Thomas House Weekend",
                UrlName = "thomas-house-weekend", PlaceId = Guid.NewGuid(),
                StartsOn = DateTime.UtcNow.AddDays(30), EndsOn = DateTime.UtcNow.AddDays(32),
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = OwnerId,
            });
            await db.SaveChangesAsync();
        }

        var result = await BuildAdmin(f).RefundEventCredit(
            credit.Id, new RefundEventCreditRequest("They asked"), default);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("Thomas House Weekend", Assert.IsType<string>(bad.Value));
    }

    [Fact]
    public async Task Refunding_twice_is_refused()
    {
        var credit = Credit(refunded: DateTime.UtcNow.AddDays(-1));
        var f = await SeedAsync(credit);

        var result = await BuildAdmin(f).RefundEventCredit(
            credit.Id, new RefundEventCreditRequest("Again"), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task A_refund_with_no_reason_is_refused()
    {
        // The reason IS the record. A refunded row that does not say why is one nobody can answer
        // for later, which is the same rule the ledger's adjustments already keep.
        var credit = Credit();
        var f = await SeedAsync(credit);

        var result = await BuildAdmin(f).RefundEventCredit(
            credit.Id, new RefundEventCreditRequest("   "), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task An_expired_credit_may_still_be_refunded()
    {
        // The goodwill case, and the one most likely to arrive: somebody paid, never used it, and
        // is asking. Marking the row keeps that decision beside the money instead of in an email.
        var credit = Credit(expires: DateTime.UtcNow.AddDays(-5));
        var f = await SeedAsync(credit);

        var result = await BuildAdmin(f).RefundEventCredit(
            credit.Id, new RefundEventCreditRequest("Never got to use it"), default);

        Assert.IsType<OkObjectResult>(result.Result);
    }
}
