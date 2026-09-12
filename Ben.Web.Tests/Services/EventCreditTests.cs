using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// One credit buys one event (item 235).
/// </summary>
/// <remarks>
/// <para>Ben, 2026-09-11: "This is a single event they can schedule or host. They have a year to
/// have hosted the event otherwise they lose the credit. For multiple events they would need
/// multiple credits, one for each event."</para>
///
/// <para>Every rule here is about somebody's ninety-nine dollars, which is why each one is pinned
/// separately rather than in one happy-path test: the ways this can go wrong all cost a real person
/// real money, and none of them would show up as an exception.</para>
/// </remarks>
public sealed class EventCreditTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc);

    private static IDbContextFactory<BenDataContext> CreateFactory()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static EventCredit Credit(
        DateTime purchased, DateTime expires,
        DateTime? spent = null, DateTime? refunded = null)
        => new()
        {
            Id = Guid.NewGuid(),
            OwnerOrganizationId = OrgId,
            PriceAtPurchase = 99m,
            PurchasedUtc = purchased,
            ExpiresUtc = expires,
            SpentUtc = spent,
            RefundedUtc = refunded,
            DateCreated = purchased,
            CreatedByAppUserId = UserId,
        };

    private static HostedEvent Event(DateTime? startsOn = null)
        => new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = OrgId,
            Name = "A weekend",
            UrlName = "a-weekend",
            PlaceId = Guid.NewGuid(),
            StartsOn = startsOn ?? Now.Date.AddDays(30),
            EndsOn = (startsOn ?? Now.Date.AddDays(30)).AddDays(2),
            DateCreated = Now,
            CreatedByAppUserId = UserId,
        };

    private static async Task<IDbContextFactory<BenDataContext>> HoldingAsync(params EventCredit[] credits)
    {
        var f = CreateFactory();
        await using var db = await f.CreateDbContextAsync();
        db.EventCredits.AddRange(credits);
        await db.SaveChangesAsync();
        return f;
    }

    // ── which one gets spent ─────────────────────────────────────────────────

    [Fact]
    public async Task The_oldest_usable_credit_is_the_one_spent()
    {
        // Spending the newest would let the oldest lapse while its owner was actively using the
        // site, which is somebody's money quietly thrown away. This is the whole test.
        var oldest = Credit(Now.AddDays(-300), Now.AddDays(65));
        var newest = Credit(Now.AddDays(-2), Now.AddDays(363));

        var f = await HoldingAsync(newest, oldest);
        await using var db = await f.CreateDbContextAsync();

        var next = await EventCredits.NextToSpendAsync(db, OrgId, null, Now, default);

        Assert.NotNull(next);
        Assert.Equal(oldest.Id, next!.Id);
    }

    [Fact]
    public async Task A_lapsed_credit_is_never_spent_and_never_counted()
    {
        var lapsed = Credit(Now.AddDays(-400), Now.AddDays(-35));
        var f = await HoldingAsync(lapsed);
        await using var db = await f.CreateDbContextAsync();

        Assert.Null(await EventCredits.NextToSpendAsync(db, OrgId, null, Now, default));
        Assert.Equal(0, await EventCredits.SpendableCountAsync(db, OrgId, null, Now, default));
    }

    [Fact]
    public async Task A_spent_credit_and_a_refunded_one_are_both_out_of_play()
    {
        var f = await HoldingAsync(
            Credit(Now.AddDays(-10), Now.AddDays(355), spent: Now.AddDays(-1)),
            Credit(Now.AddDays(-10), Now.AddDays(355), refunded: Now.AddDays(-1)));

        await using var db = await f.CreateDbContextAsync();

        Assert.Null(await EventCredits.NextToSpendAsync(db, OrgId, null, Now, default));
        Assert.Equal(0, await EventCredits.SpendableCountAsync(db, OrgId, null, Now, default));
    }

    [Fact]
    public async Task One_group_never_spends_anothers_credit()
    {
        var mine = Credit(Now.AddDays(-10), Now.AddDays(355));
        var theirs = Credit(Now.AddDays(-20), Now.AddDays(345));
        theirs.OwnerOrganizationId = Guid.NewGuid();

        var f = await HoldingAsync(mine, theirs);
        await using var db = await f.CreateDbContextAsync();

        var next = await EventCredits.NextToSpendAsync(db, OrgId, null, Now, default);
        Assert.Equal(mine.Id, next!.Id);
    }

    // ── the year, checked at both ends ───────────────────────────────────────

    [Fact]
    public void A_credit_cannot_cover_an_event_that_starts_after_it_runs_out()
    {
        // Ben's rule read literally: "a year to have hosted the event". Without this, a credit
        // bought today could be parked for ever by publishing a placeholder dated 2031.
        var credit = Credit(Now.AddDays(-340), Now.AddDays(25));
        var distant = Event(startsOn: Now.Date.AddDays(90));

        var why = EventCredits.WhyItCannotCover(credit, distant, Now);

        Assert.NotNull(why);
        // Both dates, because "that won't work" sends somebody to support and naming the two dates
        // sends them to the right button.
        Assert.Contains(credit.ExpiresUtc.ToString("MM/dd/yyyy"), why);
        Assert.Contains(distant.StartsOn.ToString("MM/dd/yyyy"), why);
    }

    [Fact]
    public void A_credit_covers_an_event_that_starts_inside_its_year()
    {
        var credit = Credit(Now.AddDays(-340), Now.AddDays(25));
        Assert.Null(EventCredits.WhyItCannotCover(credit, Event(Now.Date.AddDays(10)), Now));
    }

    [Fact]
    public void An_already_spent_credit_says_so_rather_than_being_spent_twice()
    {
        var credit = Credit(Now.AddDays(-10), Now.AddDays(355), spent: Now.AddDays(-1));
        Assert.Contains("already been used", EventCredits.WhyItCannotCover(credit, Event(), Now));
    }

    [Fact]
    public void Spending_records_which_event_it_went_on()
    {
        // So "have we paid for this one?" has one answer for ever — which is what makes
        // re-publishing free.
        var credit = Credit(Now.AddDays(-10), Now.AddDays(355));
        var hosted = Event();

        EventCredits.Spend(credit, hosted, UserId, Now);

        Assert.Equal(Now, credit.SpentUtc);
        Assert.Equal(hosted.Id, credit.SpentOnHostedEventId);
        Assert.False(credit.IsSpendable(Now));
    }

    // ── the warning ──────────────────────────────────────────────────────────

    [Fact]
    public async Task A_credit_lapsing_within_thirty_days_is_warned_once()
    {
        // Ben, 2026-09-11: "30-day warning sounds reasonable." It costs nothing to send and saves
        // the support ticket that arrives when somebody finds out a credit expired last week.
        var soon = Credit(Now.AddDays(-345), Now.AddDays(20));
        var later = Credit(Now.AddDays(-10), Now.AddDays(355));
        var alreadyTold = Credit(Now.AddDays(-345), Now.AddDays(15));
        alreadyTold.ExpiryWarningSentUtc = Now.AddDays(-1);

        var f = await HoldingAsync(soon, later, alreadyTold);
        await using var db = await f.CreateDbContextAsync();

        var due = await EventCredits.DueAWarningAsync(db, Now, default);

        Assert.Single(due);
        Assert.Equal(soon.Id, due[0].Id);
    }

    [Fact]
    public async Task A_credit_that_has_already_lapsed_is_not_warned_about()
    {
        // Telling somebody their credit is "about to" expire a week after it did is worse than
        // saying nothing.
        var f = await HoldingAsync(Credit(Now.AddDays(-400), Now.AddDays(-5)));
        await using var db = await f.CreateDbContextAsync();

        Assert.Empty(await EventCredits.DueAWarningAsync(db, Now, default));
    }

    [Fact]
    public void The_price_and_the_year_are_what_Ben_set()
    {
        Assert.Equal(99m, EventCredits.DefaultPriceUsd);
        Assert.Equal(365, EventCredits.Life.TotalDays);
        Assert.Equal(30, EventCredits.WarningLead.TotalDays);
    }
}
