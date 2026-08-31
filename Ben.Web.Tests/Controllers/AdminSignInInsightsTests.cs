using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Admin;
using Ben.Service.Models.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// The dashboard's sign-in panels (Ben, 2026-08-31).
/// </summary>
/// <remarks>
/// <para>These pin the DESIGN claims, not the arithmetic: that "who has been here" answers a
/// different question from "who signs in most" and cannot be filled by one busy account; that
/// successes and failures are ranked separately; and that a person in two groups counts for
/// both. Each of those is a decision somebody could undo by writing a simpler query, and the
/// simpler query looks correct until you read what the panel claims to say.</para>
///
/// <para>The oddities are asserted by KIND rather than by their sentences, which are written for
/// a reader and will be reworded.</para>
/// </remarks>
public sealed class AdminSignInInsightsTests
{
    private static IDbContextFactory<BenDataContext> CreateFactory()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static AdminStatsController Build(IDbContextFactory<BenDataContext> f) => new(f);

    private static async Task<AdminSignInInsights> InsightsAsync(
        IDbContextFactory<BenDataContext> f, int days = 30)
    {
        var result = (await Build(f).GetSignInInsights(days, default)).Result;
        if (result is ObjectResult { Value: not AdminSignInInsights } other)
            Assert.Fail($"refused: HTTP {other.StatusCode} — {other.Value}");
        return Assert.IsType<AdminSignInInsights>(Assert.IsType<OkObjectResult>(result).Value);
    }

    private static async Task<Guid> AddUserAsync(
        IDbContextFactory<BenDataContext> f, string name, DateTime? created = null)
    {
        var id = Guid.NewGuid();
        await using var db = await f.CreateDbContextAsync();
        db.AppUsers.Add(new AppUser
        {
            Id = id, UserName = $"{name}@t.com", Email = $"{name}@t.com",
            DisplayName = name, DateCreated = created ?? DateTime.UtcNow.AddMonths(-6),
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task SignInAsync(
        IDbContextFactory<BenDataContext> f, Guid userId, DateTime utc,
        bool succeeded = true, string method = "password")
    {
        await using var db = await f.CreateDbContextAsync();
        db.SignInEvents.Add(new SignInEvent
        {
            Id = Guid.NewGuid(), AppUserId = userId, Utc = utc,
            Succeeded = succeeded, Method = method,
        });
        await db.SaveChangesAsync();
    }

    // ── the list is of PEOPLE, not events ────────────────────────────────────

    /// <summary>
    /// The whole reason "who has been here" is grouped before it is taken: without that, one
    /// person signing in eleven times IS the list, and the panel silently becomes a duplicate of
    /// "signing in most".
    /// </summary>
    [Fact]
    public async Task One_busy_account_cannot_fill_the_recent_list()
    {
        var f = CreateFactory();
        var busy = await AddUserAsync(f, "Busy");
        var quiet = await AddUserAsync(f, "Quiet");

        var now = DateTime.UtcNow;
        for (var i = 0; i < 15; i++) await SignInAsync(f, busy, now.AddMinutes(-i));
        await SignInAsync(f, quiet, now.AddHours(-1));

        var insights = await InsightsAsync(f);

        Assert.Equal(2, insights.Recent.Count);
        Assert.Equal(new[] { "Busy", "Quiet" }, insights.Recent.Select(r => r.Name).ToArray());
        Assert.Single(insights.Recent, r => r.Name == "Busy");
    }

    [Fact]
    public async Task The_recent_list_shows_each_account_at_its_latest_sign_in()
    {
        var f = CreateFactory();
        var user = await AddUserAsync(f, "Returning");
        var now = DateTime.UtcNow;

        await SignInAsync(f, user, now.AddDays(-3));
        await SignInAsync(f, user, now.AddMinutes(-5));

        var row = Assert.Single((await InsightsAsync(f)).Recent);
        Assert.Equal(now.AddMinutes(-5), row.Utc, TimeSpan.FromSeconds(1));
    }

    // ── successes and failures are different questions ───────────────────────

    [Fact]
    public async Task Failures_never_count_towards_signing_in_most()
    {
        var f = CreateFactory();
        var user = await AddUserAsync(f, "Fumbling");
        var now = DateTime.UtcNow;

        await SignInAsync(f, user, now.AddHours(-2), succeeded: true);
        for (var i = 0; i < 9; i++) await SignInAsync(f, user, now.AddMinutes(-i), succeeded: false);

        var insights = await InsightsAsync(f);

        Assert.Equal(1, Assert.Single(insights.TopPeople).Count);
        Assert.Equal(9, Assert.Single(insights.MostFailures).Count);
    }

    // ── groups ───────────────────────────────────────────────────────────────

    /// <summary>
    /// A person in two groups counts for both. The alternative — dividing a sign-in between
    /// their groups — answers a question nobody asked and makes every number a fraction.
    /// </summary>
    [Fact]
    public async Task A_member_of_two_groups_counts_for_both()
    {
        var f = CreateFactory();
        var user = await AddUserAsync(f, "Joiner");
        Guid orgA = Guid.NewGuid(), orgB = Guid.NewGuid();

        await using (var db = await f.CreateDbContextAsync())
        {
            db.Organizations.Add(new Organization
            {
                Id = orgA, Name = "Group A", UrlName = $"a-{orgA:N}",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = user,
            });
            db.Organizations.Add(new Organization
            {
                Id = orgB, Name = "Group B", UrlName = $"b-{orgB:N}",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = user,
            });
            foreach (var org in new[] { orgA, orgB })
                db.OrganizationUserMemberships.Add(new OrganizationUserMembership
                {
                    Id = Guid.NewGuid(), OrganizationId = org, AppUserId = user,
                    Role = OrganizationMemberRole.Member, IsActive = true,
                    DateCreated = DateTime.UtcNow, CreatedByAppUserId = user,
                });
            await db.SaveChangesAsync();
        }

        for (var i = 0; i < 4; i++) await SignInAsync(f, user, DateTime.UtcNow.AddMinutes(-i));

        var groups = (await InsightsAsync(f)).TopGroups;

        Assert.Equal(2, groups.Count);
        Assert.All(groups, g => Assert.Equal(4, g.Count));
    }

    [Fact]
    public async Task An_inactive_membership_does_not_count_towards_its_group()
    {
        var f = CreateFactory();
        var user = await AddUserAsync(f, "Departed");
        var orgId = Guid.NewGuid();

        await using (var db = await f.CreateDbContextAsync())
        {
            db.Organizations.Add(new Organization
            {
                Id = orgId, Name = "Group", UrlName = $"g-{orgId:N}",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = user,
            });
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = user,
                Role = OrganizationMemberRole.Member, IsActive = false,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = user,
            });
            await db.SaveChangesAsync();
        }

        await SignInAsync(f, user, DateTime.UtcNow.AddMinutes(-1));

        Assert.Empty((await InsightsAsync(f)).TopGroups);
    }

    // ── oddities ─────────────────────────────────────────────────────────────

    /// <summary>Locked out and being guessed at look identical from here; both are worth a look.</summary>
    [Fact]
    public async Task An_account_failing_more_than_it_succeeds_is_flagged()
    {
        var f = CreateFactory();
        var user = await AddUserAsync(f, "Probed");
        var now = DateTime.UtcNow;

        for (var i = 0; i < 8; i++) await SignInAsync(f, user, now.AddMinutes(-i), succeeded: false);

        var odd = Assert.Single((await InsightsAsync(f)).Oddities, o => o.Kind == "failures");
        Assert.Contains("Probed", odd.Headline);
        Assert.Equal(user, odd.AppUserId);
    }

    /// <summary>
    /// Below the floor it is somebody mistyping their own password, and a dashboard that cries
    /// about that teaches its reader to ignore it.
    /// </summary>
    [Fact]
    public async Task A_couple_of_mistyped_passwords_are_not_flagged()
    {
        var f = CreateFactory();
        var user = await AddUserAsync(f, "Human");
        var now = DateTime.UtcNow;

        for (var i = 0; i < 3; i++) await SignInAsync(f, user, now.AddMinutes(-i), succeeded: false);

        Assert.DoesNotContain((await InsightsAsync(f)).Oddities, o => o.Kind == "failures");
    }

    [Fact]
    public async Task A_long_dormant_account_that_returns_is_flagged()
    {
        var f = CreateFactory();
        var user = await AddUserAsync(f, "Rip");
        var now = DateTime.UtcNow;

        await SignInAsync(f, user, now.AddDays(-200));
        await SignInAsync(f, user, now.AddHours(-1));

        var odd = Assert.Single((await InsightsAsync(f)).Oddities, o => o.Kind == "woke");
        Assert.Equal(user, odd.AppUserId);
    }

    [Fact]
    public async Task An_account_that_signed_in_last_month_is_not_called_dormant()
    {
        var f = CreateFactory();
        var user = await AddUserAsync(f, "Regular");
        var now = DateTime.UtcNow;

        await SignInAsync(f, user, now.AddDays(-45));
        await SignInAsync(f, user, now.AddHours(-1));

        Assert.DoesNotContain((await InsightsAsync(f)).Oddities, o => o.Kind == "woke");
    }

    /// <summary>The funnel's quietest failure: not signing in badly — not signing in.</summary>
    [Fact]
    public async Task Accounts_that_registered_and_never_arrived_are_counted()
    {
        var f = CreateFactory();
        await AddUserAsync(f, "Ghost", created: DateTime.UtcNow.AddDays(-30));
        await AddUserAsync(f, "AlsoGhost", created: DateTime.UtcNow.AddDays(-20));

        var odd = Assert.Single((await InsightsAsync(f)).Oddities, o => o.Kind == "never");
        Assert.Contains("2", odd.Headline);
        Assert.Null(odd.AppUserId);
    }

    [Fact]
    public async Task Somebody_who_registered_yesterday_is_given_time_to_arrive()
    {
        var f = CreateFactory();
        await AddUserAsync(f, "JustJoined", created: DateTime.UtcNow.AddDays(-1));

        Assert.DoesNotContain((await InsightsAsync(f)).Oddities, o => o.Kind == "never");
    }

    // ── the blind spot is declared ───────────────────────────────────────────

    /// <summary>
    /// A reader must be able to tell "no Apple sign-ins happened" from "Apple sign-ins are not
    /// counted here", because the two look identical in a donut chart.
    /// </summary>
    [Fact]
    public async Task Apple_coverage_is_reported_honestly()
    {
        var f = CreateFactory();
        var user = await AddUserAsync(f, "Phone");
        await SignInAsync(f, user, DateTime.UtcNow.AddMinutes(-2));

        Assert.False((await InsightsAsync(f)).CoversAppleSignIns);

        await SignInAsync(f, user, DateTime.UtcNow.AddMinutes(-1), method: "apple");

        var after = await InsightsAsync(f);
        Assert.True(after.CoversAppleSignIns);
        Assert.Contains(after.ByMethod, m => m.Label == "Apple" && m.Count == 1);
        Assert.Contains(after.ByMethod, m => m.Label == "Password" && m.Count == 1);
    }

    [Fact]
    public async Task The_hour_histogram_always_has_twenty_four_buckets()
    {
        var f = CreateFactory();
        var user = await AddUserAsync(f, "Owl");
        await SignInAsync(f, user, DateTime.UtcNow.Date.AddHours(3));

        var hours = (await InsightsAsync(f)).ByHourUtc;

        Assert.Equal(24, hours.Count);
        Assert.Equal("00:00", hours[0].Label);
        Assert.Equal("23:00", hours[23].Label);
        Assert.Equal(1, hours[3].Count);
    }

    /// <summary>
    /// Sign-in rows outlive the account: SignInEvent has no foreign key to AppUser precisely so
    /// the history survives a deletion, and a blank cell would read as a bug.
    /// </summary>
    [Fact]
    public async Task A_sign_in_by_a_deleted_account_still_reads()
    {
        var f = CreateFactory();
        await SignInAsync(f, Guid.NewGuid(), DateTime.UtcNow.AddMinutes(-1));

        Assert.Equal("(deleted account)", Assert.Single((await InsightsAsync(f)).Recent).Name);
    }
}
