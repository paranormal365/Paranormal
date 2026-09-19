using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Billing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// The per-period allowance — the ladder's first limit that is not a concurrent count.
/// </summary>
/// <remarks>
/// <para>Every other <see cref="SubscriptionLimit"/> asks "how many do you have right now", so
/// closing one makes room immediately. <see cref="SubscriptionLimit.CasesPerPeriod"/> asks "how
/// many did you start since your period began", so closing one makes room for nothing until the
/// period turns over. Written for the solo tier, where what is sold is a rate of work rather than
/// a stock of it — an investigator who could close and reopen freely would have an unlimited plan
/// with extra steps.</para>
///
/// <para>The refusal is tested as carefully as the arithmetic. A person who meets an allowance
/// and is told "you are using all of it" will go and close a case, and it will not help — so the
/// allowance has to say when it resets instead.</para>
/// </remarks>
public sealed class CaseAllowanceTests
{
    private static IDbContextFactory<BenDataContext> CreateFactory()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed record World(
        IDbContextFactory<BenDataContext> F, Guid OrgId, DateTime PeriodStart, DateTime PeriodEnd);

    /// <summary>A subscribed group on a band allowing one new case per period.</summary>
    private static async Task<World> SeedAsync(int? allowance = 1, bool withSubscription = true)
    {
        var f = CreateFactory();
        Guid userId = Guid.NewGuid(), orgId = Guid.NewGuid(), tierId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var periodStart = now.AddDays(-10);
        var periodEnd = now.AddDays(20);

        await using var db = await f.CreateDbContextAsync();
        db.AppUsers.Add(new AppUser
        {
            Id = userId, UserName = "s@t.com", Email = "s@t.com",
            DisplayName = "Solo", DateCreated = now,
        });
        db.Organizations.Add(new Organization
        {
            Id = orgId, Name = "Solo", UrlName = $"s-{orgId:N}",
            DateCreated = now, CreatedByAppUserId = userId,
        });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = userId,
            Role = OrganizationMemberRole.Owner, IsActive = true,
            DateCreated = now, CreatedByAppUserId = userId,
        });
        db.SubscriptionTiers.Add(new SubscriptionTier
        {
            Id = tierId, Name = "Solo investigator", MinMembers = 1, MaxMembers = null,
            IsActive = true, DateCreated = now, CreatedByAppUserId = userId,
        });
        db.SubscriptionTierPrices.Add(new SubscriptionTierPrice
        {
            Id = Guid.NewGuid(), SubscriptionTierId = tierId,
            Interval = BillingInterval.Monthly, Price = 9.99m, IsActive = true,
            DateCreated = now, CreatedByAppUserId = userId,
        });
        if (allowance is { } max)
        {
            db.SubscriptionTierLimits.Add(new SubscriptionTierLimit
            {
                Id = Guid.NewGuid(), SubscriptionTierId = tierId,
                Limit = SubscriptionLimit.CasesPerPeriod, MaxValue = max,
                DateCreated = now, CreatedByAppUserId = userId,
            });
        }
        if (withSubscription)
        {
            db.OrganizationSubscriptions.Add(new OrganizationSubscription
            {
                Id = Guid.NewGuid(), OrganizationId = orgId,
                SubscriptionTierId = tierId, Status = SubscriptionStatus.Active,
                Interval = BillingInterval.Monthly,
                CurrentPeriodStart = periodStart, CurrentPeriodEnd = periodEnd,
                DateCreated = now, CreatedByAppUserId = userId,
            });
        }
        await db.SaveChangesAsync();

        return new World(f, orgId, periodStart, periodEnd);
    }

    private static SubscriptionLimitGuard Guard(World w) => new(w.F);

    private static Task<string?> AskAsync(World w, int startedThisPeriod)
        => Guard(w).WhyNotOneMoreAsync(
            w.OrgId, SubscriptionLimit.CasesPerPeriod, startedThisPeriod, default);

    // ── the window ───────────────────────────────────────────────────────────

    [Fact]
    public async Task The_allowance_window_is_the_billing_period()
    {
        var w = await SeedAsync();

        var window = await Guard(w).AllowanceWindowAsync(w.OrgId, default);

        Assert.NotNull(window);
        Assert.Equal(w.PeriodStart, window!.Value.Start);
        Assert.Equal(w.PeriodEnd, window.Value.End);
    }

    /// <summary>
    /// No subscription means no period, which means nothing to count over. Fail open — the same
    /// rule every other cap here follows, and the reason a free group never meets this.
    /// </summary>
    [Fact]
    public async Task A_group_with_no_subscription_has_no_window_and_is_not_metered()
    {
        var w = await SeedAsync(withSubscription: false);

        Assert.Null(await Guard(w).AllowanceWindowAsync(w.OrgId, default));
        Assert.Null(await AskAsync(w, startedThisPeriod: 99));
    }

    // ── the allowance ────────────────────────────────────────────────────────

    [Fact]
    public async Task The_first_case_of_the_period_is_allowed()
        => Assert.Null(await AskAsync(await SeedAsync(), startedThisPeriod: 0));

    [Fact]
    public async Task A_second_case_in_the_same_period_is_refused()
        => Assert.NotNull(await AskAsync(await SeedAsync(), startedThisPeriod: 1));

    [Fact]
    public async Task A_band_with_no_allowance_row_is_uncapped()
        => Assert.Null(await AskAsync(await SeedAsync(allowance: null), startedThisPeriod: 50));

    // ── the refusal has to be the right advice ───────────────────────────────

    /// <summary>
    /// The whole reason an allowance needs its own sentence. Telling somebody they are "using all
    /// of it" sends them to close a case, which frees nothing — so the words have to name the
    /// reset instead, and say plainly that closing does not help.
    /// </summary>
    [Fact]
    public async Task The_refusal_names_the_reset_date_and_says_closing_will_not_help()
    {
        var w = await SeedAsync();

        var why = await AskAsync(w, startedThisPeriod: 1);

        Assert.NotNull(why);
        Assert.Contains(w.PeriodEnd.ToString("MM/dd/yyyy"), why);
        Assert.Contains("Closing a case does not free one up", why);
        Assert.DoesNotContain("you are using all of it", why);
    }

    /// <summary>A concurrent cap keeps the old wording — closing one genuinely does help there.</summary>
    [Fact]
    public async Task A_concurrent_cap_still_reads_as_a_ceiling()
    {
        var w = await SeedAsync();
        await using (var db = await w.F.CreateDbContextAsync())
        {
            var tier = await db.SubscriptionTiers.SingleAsync();
            db.SubscriptionTierLimits.Add(new SubscriptionTierLimit
            {
                Id = Guid.NewGuid(), SubscriptionTierId = tier.Id,
                Limit = SubscriptionLimit.OpenCases, MaxValue = 2,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid(),
            });
            await db.SaveChangesAsync();
        }

        var why = await Guard(w).WhyNotOneMoreAsync(
            w.OrgId, SubscriptionLimit.OpenCases, currentCount: 2, default);

        Assert.NotNull(why);
        Assert.Contains("using all of it", why);
        Assert.DoesNotContain("resets on", why);
    }

    /// <summary>
    /// One place says which limits are allowances, so a call site cannot count a rate the way it
    /// counts a stock — the loophole that would look correct and silently reset on every close.
    /// </summary>
    [Fact]
    public void Only_the_per_period_limits_are_allowances()
    {
        Assert.True(SubscriptionLimitGuard.IsPerPeriod(SubscriptionLimit.CasesPerPeriod));

        foreach (var limit in Enum.GetValues<SubscriptionLimit>()
                     .Where(l => l != SubscriptionLimit.CasesPerPeriod))
        {
            Assert.False(SubscriptionLimitGuard.IsPerPeriod(limit), $"{limit} is not an allowance");
        }
    }

    /// <summary>
    /// A lapsed subscription outranks the allowance: "you have used this period's case" would
    /// send somebody to wait for a reset that a lapsed plan is never going to deliver.
    /// </summary>
    [Fact]
    public async Task A_lapsed_subscription_is_reported_instead_of_the_allowance()
    {
        var w = await SeedAsync();
        await using (var db = await w.F.CreateDbContextAsync())
        {
            var sub = await db.OrganizationSubscriptions.SingleAsync();
            sub.Status = SubscriptionStatus.Lapsed;
            await db.SaveChangesAsync();
        }

        var why = await AskAsync(w, startedThisPeriod: 1);

        Assert.NotNull(why);
        Assert.Contains("subscription has ended", why);
        Assert.DoesNotContain("resets on", why);
    }
}
