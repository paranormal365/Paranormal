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

    // ── item 184 Phase D: the private-lane consequences of a lapse ───────────

    private static async Task<Guid> SeedPublishedCaseAsync(World w, bool isPrivate)
    {
        await using var db = await w.F.CreateDbContextAsync();
        var id = Guid.NewGuid();
        db.Cases.Add(new Case
        {
            Id = id, OrganizationId = w.OrgId, Status = CaseStatus.Public, IsPublic = true,
            IsPrivateEngagement = isPrivate,
            Title = isPrivate ? "Published home case" : "Published landmark case",
            StreetAddress1 = "1 Main", City = "N", State = "TN", ZipCode = "1", Country = "US",
            DateCaseOpened = DateTime.UtcNow, DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.OwnerId,
        });
        await db.SaveChangesAsync();
        return id;
    }

    [Fact]
    public async Task A_lapse_unpublishes_published_private_cases_and_remembers_the_way_back()
    {
        var w = await SeedAsync(periodEnd: DateTime.UtcNow.AddMinutes(-5));
        // Closed-status published cases too: a finished case is published just as publicly.
        var privateCaseId = await SeedPublishedCaseAsync(w, isPrivate: true);
        var landmarkCaseId = await SeedPublishedCaseAsync(w, isPrivate: false);

        await Job(w.F).RunAsync(default);

        await using var db = await w.F.CreateDbContextAsync();
        var privateCase = await db.Cases.SingleAsync(c => c.Id == privateCaseId);
        var landmark = await db.Cases.SingleAsync(c => c.Id == landmarkCaseId);

        Assert.False(privateCase.IsPublic);
        Assert.True(privateCase.WasPublicBeforeLapse);
        // The internal-landmark pin: free-lane publication is untouched by billing.
        Assert.True(landmark.IsPublic);
        Assert.Null(landmark.WasPublicBeforeLapse);
    }

    [Fact]
    public async Task The_unpublish_survives_repeat_runs_without_touching_a_manual_republish()
    {
        var w = await SeedAsync(periodEnd: DateTime.UtcNow.AddMinutes(-5));
        var privateCaseId = await SeedPublishedCaseAsync(w, isPrivate: true);

        await Job(w.F).RunAsync(default);

        // The org talked to SuperAdmin, republished by hand while still lapsed (their call).
        await using (var db = await w.F.CreateDbContextAsync())
        {
            var c = await db.Cases.SingleAsync(x => x.Id == privateCaseId);
            c.IsPublic = true; c.WasPublicBeforeLapse = null;
            await db.SaveChangesAsync();
        }

        await Job(w.F).RunAsync(default);   // sub is already Lapsed: the pass must not re-enter

        await using (var check = await w.F.CreateDbContextAsync())
            Assert.True((await check.Cases.SingleAsync(x => x.Id == privateCaseId)).IsPublic);
    }

    [Fact]
    public async Task The_approach_warnings_name_the_unpublish_when_private_cases_are_published()
    {
        var w = await SeedAsync(periodEnd: DateTime.UtcNow.AddDays(3));
        await SeedPublishedCaseAsync(w, isPrivate: true);

        await Job(w.F).RunAsync(default);

        await using var db = await w.F.CreateDbContextAsync();
        var bodies = await db.UserMessages.Select(m => m.MessageBody).ToListAsync();
        Assert.Equal(2, bodies.Count);   // two-week and one-week, both inside the window
        Assert.All(bodies, b => Assert.Contains("published private-residence", b));
    }

    [Fact]
    public async Task The_approach_warnings_stay_quiet_about_unpublishing_when_nothing_is_published()
    {
        var w = await SeedAsync(periodEnd: DateTime.UtcNow.AddDays(3));

        await Job(w.F).RunAsync(default);

        await using var db = await w.F.CreateDbContextAsync();
        var bodies = await db.UserMessages.Select(m => m.MessageBody).ToListAsync();
        Assert.All(bodies, b => Assert.DoesNotContain("private-residence", b));
    }

    // ── the stranded-client notice ───────────────────────────────────────────

    private static async Task MakeLapsedAsync(World w, int daysAgo)
    {
        await using var db = await w.F.CreateDbContextAsync();
        var sub = await db.OrganizationSubscriptions.SingleAsync();
        sub.Status = SubscriptionStatus.Lapsed;
        sub.LapsedAtUtc = DateTime.UtcNow.AddDays(-daysAgo);
        var c = await db.Cases.SingleAsync(x => x.Id == w.ActiveCaseId);
        c.StatusBeforePause = c.Status;
        c.Status = CaseStatus.Paused;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Twenty_nine_days_lapsed_is_too_soon_for_the_stranded_notice()
    {
        var w = await SeedAsync(periodEnd: DateTime.UtcNow.AddMonths(2));
        await MakeLapsedAsync(w, daysAgo: 29);

        await Job(w.F).RunAsync(default);

        await using var db = await w.F.CreateDbContextAsync();
        Assert.Equal(0, await db.UserMessageTos.CountAsync(t => t.ToAppUserId == w.ClientId));
        Assert.Null((await db.OrganizationSubscriptions.SingleAsync()).StrandedClientNoticeSentAtUtc);
    }

    [Fact]
    public async Task Thirty_one_days_lapsed_offers_the_move_once_and_the_stamp_re_arms()
    {
        var w = await SeedAsync(periodEnd: DateTime.UtcNow.AddMonths(2));
        await MakeLapsedAsync(w, daysAgo: 31);

        await Job(w.F).RunAsync(default);
        await Job(w.F).RunAsync(default);   // stamped: the second pass is silent

        await using (var db = await w.F.CreateDbContextAsync())
        {
            Assert.Equal(1, await db.UserMessageTos.CountAsync(t => t.ToAppUserId == w.ClientId));
            var body = (await db.UserMessages.SingleAsync()).MessageBody;
            Assert.Contains("move", body);
            Assert.NotNull((await db.OrganizationSubscriptions.SingleAsync()).StrandedClientNoticeSentAtUtc);

            // Reactivation clears the stamp (the controller's half); a NEW lapse then re-arms.
            var sub = await db.OrganizationSubscriptions.SingleAsync();
            sub.StrandedClientNoticeSentAtUtc = null;
            sub.LapsedAtUtc = DateTime.UtcNow.AddDays(-40);
            await db.SaveChangesAsync();
        }

        await Job(w.F).RunAsync(default);

        await using (var check = await w.F.CreateDbContextAsync())
            Assert.Equal(2, await check.UserMessageTos.CountAsync(t => t.ToAppUserId == w.ClientId));
    }
}
