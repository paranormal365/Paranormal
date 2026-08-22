using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Billing;
using Ben.Data.WebApi.Services.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The wind-down clock: warnings that fire once per period end, a lapse that pauses exactly the
/// open work, and a restore that brings back exactly what the lapse took.
/// </summary>
/// <remarks>
/// Item 84's mechanics. The invariant worth the most here is the round trip: pause then restore
/// must be lossless per case — Active resumes Active, Proposed resumes Proposed — because "the
/// group renews and everything is back as it was" is the promise every notice makes.
/// </remarks>
public sealed class SubscriptionLapseJobTests
{
    private sealed class SimpleFactory(DbContextOptions<BenDataContext> options) : IDbContextFactory<BenDataContext>
    {
        public BenDataContext CreateDbContext() => new(options);
        public Task<BenDataContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(new BenDataContext(options));
    }

    private static IDbContextFactory<BenDataContext> Factory() =>
        new SimpleFactory(new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static SubscriptionLapseJob Job(IDbContextFactory<BenDataContext> f) =>
        new(f, new PlatformMessageService(f), NullLogger<SubscriptionLapseJob>.Instance);

    private sealed record World(IDbContextFactory<BenDataContext> F, Guid OrgId, Guid OwnerId, Guid ClientId, Guid ActiveCaseId, Guid ClosedCaseId);

    private static async Task<World> SeedAsync(DateTime periodEnd)
    {
        var f = Factory();
        var orgId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var activeCase = Guid.NewGuid();
        var closedCase = Guid.NewGuid();

        await using var db = await f.CreateDbContextAsync();
        db.Users.Add(new AppUser { Id = ownerId, UserName = "o@t.com", Email = "o@t.com", DateCreated = DateTime.UtcNow });
        db.Users.Add(new AppUser { Id = clientId, UserName = "c@t.com", Email = "c@t.com", DateCreated = DateTime.UtcNow });
        db.Organizations.Add(new Organization { Id = orgId, Name = "Org", UrlName = "org", DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId });
        db.OrganizationSubscriptions.Add(new OrganizationSubscription
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, Status = SubscriptionStatus.Active,
            CurrentPeriodStart = periodEnd.AddMonths(-1), CurrentPeriodEnd = periodEnd,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
        });
        db.Cases.Add(new Case
        {
            Id = activeCase, OrganizationId = orgId, Status = CaseStatus.Active,
            Title = "Live one", StreetAddress1 = "1 Main", City = "N", State = "TN", ZipCode = "1",
            Country = "US", DateCaseOpened = DateTime.UtcNow, DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
        });
        db.Cases.Add(new Case
        {
            Id = closedCase, OrganizationId = orgId, Status = CaseStatus.Closed,
            Title = "Done one", StreetAddress1 = "1 Main", City = "N", State = "TN", ZipCode = "1",
            Country = "US", DateCaseOpened = DateTime.UtcNow, DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
        });
        db.CaseClientAccesses.Add(new CaseClientAccess
        {
            Id = Guid.NewGuid(), CaseId = activeCase, AppUserId = clientId,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
        });
        await db.SaveChangesAsync();

        return new World(f, orgId, ownerId, clientId, activeCase, closedCase);
    }

    // ── the lapse ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_expired_period_lapses_and_pauses_open_work_only()
    {
        var w = await SeedAsync(periodEnd: DateTime.UtcNow.AddMinutes(-5));

        await Job(w.F).RunAsync(default);

        await using var db = await w.F.CreateDbContextAsync();
        var sub    = await db.OrganizationSubscriptions.SingleAsync();
        var live   = await db.Cases.SingleAsync(c => c.Id == w.ActiveCaseId);
        var closed = await db.Cases.SingleAsync(c => c.Id == w.ClosedCaseId);

        Assert.Equal(SubscriptionStatus.Lapsed, sub.Status);
        Assert.NotNull(sub.LapsedAtUtc);
        Assert.Equal(CaseStatus.Paused, live.Status);
        Assert.Equal(CaseStatus.Active, live.StatusBeforePause);   // the way back, recorded
        Assert.Equal(CaseStatus.Closed, closed.Status);            // finished work untouched
        Assert.Null(closed.StatusBeforePause);
    }

    [Fact]
    public async Task The_lapse_messages_the_paused_cases_clients()
    {
        var w = await SeedAsync(periodEnd: DateTime.UtcNow.AddMinutes(-5));

        await Job(w.F).RunAsync(default);

        await using var db = await w.F.CreateDbContextAsync();
        var toClient = await db.UserMessageTos.CountAsync(t => t.ToAppUserId == w.ClientId);
        Assert.Equal(1, toClient);
    }

    /// <summary>Running the job twice cannot lapse twice — the second pass finds nothing Active.</summary>
    [Fact]
    public async Task The_lapse_is_idempotent()
    {
        var w = await SeedAsync(periodEnd: DateTime.UtcNow.AddMinutes(-5));

        await Job(w.F).RunAsync(default);
        await Job(w.F).RunAsync(default);

        await using var db = await w.F.CreateDbContextAsync();
        Assert.Equal(1, await db.UserMessageTos.CountAsync(t => t.ToAppUserId == w.ClientId));
    }

    // ── the warnings ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Approaching_period_end_warns_once_however_many_passes_run()
    {
        var w = await SeedAsync(periodEnd: DateTime.UtcNow.AddDays(10));

        await Job(w.F).RunAsync(default);
        await Job(w.F).RunAsync(default);
        await Job(w.F).RunAsync(default);

        await using var db = await w.F.CreateDbContextAsync();
        // Ten days out: inside the two-week window, outside the one-week one — exactly one message.
        Assert.Equal(1, await db.UserMessageTos.CountAsync(t => t.ToAppUserId == w.OwnerId));

        var sub = await db.OrganizationSubscriptions.SingleAsync();
        Assert.Equal(sub.CurrentPeriodEnd, sub.TwoWeekNoticeSentForPeriodEnd);
        Assert.Null(sub.OneWeekNoticeSentForPeriodEnd);
    }

    [Fact]
    public async Task Inside_one_week_both_warnings_have_gone_out()
    {
        var w = await SeedAsync(periodEnd: DateTime.UtcNow.AddDays(3));

        await Job(w.F).RunAsync(default);

        await using var db = await w.F.CreateDbContextAsync();
        Assert.Equal(2, await db.UserMessageTos.CountAsync(t => t.ToAppUserId == w.OwnerId));
    }

    /// <summary>
    /// A renewal re-arms the warnings with no clearing code: a new period end simply does not
    /// match the stored one.
    /// </summary>
    [Fact]
    public async Task A_renewal_re_arms_the_warnings()
    {
        var w = await SeedAsync(periodEnd: DateTime.UtcNow.AddDays(10));

        await Job(w.F).RunAsync(default);

        await using (var db = await w.F.CreateDbContextAsync())
        {
            var sub = await db.OrganizationSubscriptions.SingleAsync();
            sub.CurrentPeriodEnd = DateTime.UtcNow.AddDays(40);    // renewed
            await db.SaveChangesAsync();
        }

        await Job(w.F).RunAsync(default);                          // outside any window: silent

        await using (var db = await w.F.CreateDbContextAsync())
        {
            Assert.Equal(1, await db.UserMessageTos.CountAsync(t => t.ToAppUserId == w.OwnerId));

            var sub = await db.OrganizationSubscriptions.SingleAsync();
            sub.CurrentPeriodEnd = DateTime.UtcNow.AddDays(10);    // approaching again
            await db.SaveChangesAsync();
        }

        await Job(w.F).RunAsync(default);

        await using (var db2 = await w.F.CreateDbContextAsync())
            Assert.Equal(2, await db2.UserMessageTos.CountAsync(t => t.ToAppUserId == w.OwnerId));
    }

    // ── the round trip ────────────────────────────────────────────────────────

    [Fact]
    public async Task Restore_brings_back_exactly_what_the_lapse_took_and_nothing_else()
    {
        var w = await SeedAsync(periodEnd: DateTime.UtcNow.AddMinutes(-5));

        // One case was paused by hand before the lapse — no StatusBeforePause marker.
        Guid handPaused = Guid.NewGuid();
        await using (var db = await w.F.CreateDbContextAsync())
        {
            db.Cases.Add(new Case
            {
                Id = handPaused, OrganizationId = w.OrgId, Status = CaseStatus.Paused,
                Title = "Hand-paused", StreetAddress1 = "1", City = "N", State = "TN", ZipCode = "1",
                Country = "US", DateCaseOpened = DateTime.UtcNow, DateCreated = DateTime.UtcNow,
                CreatedByAppUserId = w.OwnerId,
            });
            await db.SaveChangesAsync();
        }

        await Job(w.F).RunAsync(default);

        await using (var db = await w.F.CreateDbContextAsync())
        {
            var restored = await PeriodOpener.RestorePausedCasesAsync(db, w.OrgId, DateTime.UtcNow, default);
            await db.SaveChangesAsync();
            Assert.Equal(1, restored);
        }

        await using (var check = await w.F.CreateDbContextAsync())
        {
            Assert.Equal(CaseStatus.Active, (await check.Cases.SingleAsync(c => c.Id == w.ActiveCaseId)).Status);
            Assert.Equal(CaseStatus.Paused, (await check.Cases.SingleAsync(c => c.Id == handPaused)).Status);
        }
    }
}
