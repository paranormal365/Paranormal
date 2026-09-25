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
        new(f, new PlatformMessageService(f), NullLogger<SubscriptionLapseJob>.Instance, Ben.Web.Tests.TestMailer.Quiet());

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

    // ── the end of a free trial is not a renewal (item 195) ──────────────────

    /// <summary>Puts the org on a tier with a real price, and gives it a coupon redemption.</summary>
    private static async Task GiveTrialAsync(
        World w, decimal payable, int? periodsRemaining, decimal price = 49m)
    {
        await using var db = await w.F.CreateDbContextAsync();

        var tierId = Guid.NewGuid();
        db.SubscriptionTiers.Add(new SubscriptionTier
        {
            Id = tierId, Name = "Standard", MinMembers = 1, SortOrder = 1, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.OwnerId,
        });
        db.SubscriptionTierPrices.Add(new SubscriptionTierPrice
        {
            Id = Guid.NewGuid(), SubscriptionTierId = tierId,
            Interval = BillingInterval.Monthly, Price = price, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.OwnerId,
        });

        var sub = await db.OrganizationSubscriptions.FirstAsync(x => x.OrganizationId == w.OrgId);
        sub.SubscriptionTierId = tierId;
        sub.Interval = BillingInterval.Monthly;

        db.CouponRedemptions.Add(new CouponRedemption
        {
            Id = Guid.NewGuid(), CouponId = Guid.NewGuid(), CouponCodeId = Guid.NewGuid(),
            OrganizationId = w.OrgId, PeriodsRemaining = periodsRemaining,
            RedeemedAtUtc = DateTime.UtcNow.AddMonths(-3),
            ListPrice = price, Discount = price - payable, Payable = payable,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.OwnerId,
        });

        await db.SaveChangesAsync();
    }

    private static async Task<string> LatestNoticeBodyAsync(World w)
    {
        await using var db = await w.F.CreateDbContextAsync();
        return await db.UserMessages.OrderByDescending(m => m.DateCreated)
            .Select(m => m.MessageBody!).FirstAsync();
    }

    private static async Task<List<string>> NoticeSubjectsAsync(World w)
    {
        await using var db = await w.F.CreateDbContextAsync();
        return await db.UserMessages.Select(m => m.MessageSubject!).ToListAsync();
    }

    /// <summary>
    /// A group finishing a free trial is told the trial is ending and what it will cost — never
    /// that renewing "keeps everything exactly as it is".
    /// </summary>
    /// <remarks>
    /// Item 195 called this "the moment the relationship is won or lost". Telling somebody who has
    /// never been charged that nothing will change is not a small inaccuracy: they read
    /// reassurance and then meet a first invoice, which is being misled rather than surprised.
    /// </remarks>
    [Fact]
    public async Task A_trial_ending_says_so_and_names_the_price()
    {
        var w = await SeedAsync(periodEnd: DateTime.UtcNow.AddDays(10));
        await GiveTrialAsync(w, payable: 0m, periodsRemaining: 1, price: 49m);

        await Job(w.F).RunAsync(default);

        var subjects = await NoticeSubjectsAsync(w);
        Assert.Contains(subjects, x => x.Contains("free trial ends"));

        var body = await LatestNoticeBodyAsync(w);
        Assert.Contains("free trial ends", body);
        Assert.Contains("49", body);
        Assert.DoesNotContain("exactly as it is", body);
    }

    /// <summary>An ordinary paid renewal keeps the wording it always had.</summary>
    [Fact]
    public async Task An_ordinary_renewal_is_unchanged()
    {
        var w = await SeedAsync(periodEnd: DateTime.UtcNow.AddDays(10));

        await Job(w.F).RunAsync(default);

        var body = await LatestNoticeBodyAsync(w);
        Assert.Contains("exactly as it is", body);
        Assert.DoesNotContain("free trial", body);
    }

    /// <summary>
    /// A trial with periods still to run is not ending, so it gets the ordinary notice.
    /// </summary>
    /// <remarks>
    /// The distinguishing case. Without it, "is there a coupon?" would be mistaken for "is the
    /// trial over?", and a group two months into three would be told to start paying.
    /// </remarks>
    [Fact]
    public async Task A_trial_with_periods_left_is_not_treated_as_ending()
    {
        var w = await SeedAsync(periodEnd: DateTime.UtcNow.AddDays(10));
        await GiveTrialAsync(w, payable: 0m, periodsRemaining: 2);

        await Job(w.F).RunAsync(default);

        var body = await LatestNoticeBodyAsync(w);
        Assert.DoesNotContain("free trial", body);
    }

    /// <summary>
    /// A discount that still leaves something payable is not a free trial ending.
    /// </summary>
    [Fact]
    public async Task A_partial_discount_is_not_a_trial()
    {
        var w = await SeedAsync(periodEnd: DateTime.UtcNow.AddDays(10));
        await GiveTrialAsync(w, payable: 20m, periodsRemaining: 1);

        await Job(w.F).RunAsync(default);

        var body = await LatestNoticeBodyAsync(w);
        Assert.DoesNotContain("free trial", body);
    }

    /// <summary>
    /// With no resolvable price the notice says so rather than inventing a number.
    /// </summary>
    /// <remarks>
    /// A notice naming the WRONG price is a broken promise; one naming none is a link to click.
    /// </remarks>
    [Fact]
    public async Task An_unresolvable_price_is_described_rather_than_invented()
    {
        var w = await SeedAsync(periodEnd: DateTime.UtcNow.AddDays(10));

        // A redemption, but the subscription is on no tier at all.
        await using (var db = await w.F.CreateDbContextAsync())
        {
            db.CouponRedemptions.Add(new CouponRedemption
            {
                Id = Guid.NewGuid(), CouponId = Guid.NewGuid(), CouponCodeId = Guid.NewGuid(),
                OrganizationId = w.OrgId, PeriodsRemaining = 1,
                RedeemedAtUtc = DateTime.UtcNow.AddMonths(-3),
                ListPrice = 49m, Discount = 49m, Payable = 0m,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.OwnerId,
            });
            await db.SaveChangesAsync();
        }

        await Job(w.F).RunAsync(default);

        var body = await LatestNoticeBodyAsync(w);
        Assert.Contains("free trial ends", body);
        Assert.Contains("what your plan lists", body);
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

    /// <summary>
    /// Attaches a primary client to the active case — the person who submitted the request.
    /// </summary>
    /// <remarks>
    /// Deliberately given NO <c>CaseClientAccess</c> row, because the real primary client has
    /// none: that table holds co-clients, added by invitation. Seeding one would model a world
    /// where the bug cannot happen.
    /// </remarks>
    private static async Task<Guid> AttachPrimaryClientAsync(World w)
    {
        var primaryId = Guid.NewGuid();
        await using var db = await w.F.CreateDbContextAsync();
        db.Users.Add(new AppUser
        {
            Id = primaryId, UserName = "primary@t.com", Email = "primary@t.com",
            DateCreated = DateTime.UtcNow,
        });
        var request = new ClientRequest
        {
            Id = Guid.NewGuid(), AppUserId = primaryId, Status = ClientRequestStatus.Assigned,
            StreetAddress1 = "1 Main", City = "N", State = "TN", ZipCode = "1", Country = "US",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = primaryId,
        };
        db.ClientRequests.Add(request);
        var theCase = await db.Cases.SingleAsync(c => c.Id == w.ActiveCaseId);
        theCase.ClientRequestId = request.Id;
        await db.SaveChangesAsync();
        return primaryId;
    }

    /// <summary>
    /// The client whose case it is hears about the pause — not just invited co-clients.
    /// </summary>
    /// <remarks>
    /// Both notices read <c>CaseClientAccesses</c> alone, so the primary client — the person who
    /// opened the case and whose home is being investigated — was told nothing, and a case with no
    /// co-clients notified nobody while the job reported success. Ben asked whether a lapse still
    /// pauses and notifies; it paused.
    /// </remarks>
    [Fact]
    public async Task The_lapse_messages_the_primary_client_not_only_co_clients()
    {
        var w = await SeedAsync(periodEnd: DateTime.UtcNow.AddMinutes(-5));
        var primaryId = await AttachPrimaryClientAsync(w);

        await Job(w.F).RunAsync(default);

        await using var db = await w.F.CreateDbContextAsync();
        Assert.Equal(1, await db.UserMessageTos.CountAsync(t => t.ToAppUserId == primaryId));
        Assert.Equal(1, await db.UserMessageTos.CountAsync(t => t.ToAppUserId == w.ClientId));
    }

    /// <summary>A case whose only client is the primary one still reaches somebody.</summary>
    [Fact]
    public async Task A_case_with_no_co_clients_still_notifies_its_client()
    {
        var w = await SeedAsync(periodEnd: DateTime.UtcNow.AddMinutes(-5));
        var primaryId = await AttachPrimaryClientAsync(w);

        // Remove the invited co-client: the ordinary shape is one client and no invitations.
        await using (var seed = await w.F.CreateDbContextAsync())
        {
            seed.CaseClientAccesses.RemoveRange(seed.CaseClientAccesses);
            await seed.SaveChangesAsync();
        }

        await Job(w.F).RunAsync(default);

        await using var db = await w.F.CreateDbContextAsync();
        Assert.Equal(1, await db.UserMessageTos.CountAsync(t => t.ToAppUserId == primaryId));
    }

    /// <summary>The thirty-day reassignment offer reaches the primary client too.</summary>
    [Fact]
    public async Task The_stranded_notice_reaches_the_primary_client()
    {
        var w = await SeedAsync(periodEnd: DateTime.UtcNow.AddMinutes(-5));
        var primaryId = await AttachPrimaryClientAsync(w);

        await Job(w.F).RunAsync(default);          // lapse + pause
        await MakeLapsedAsync(w, daysAgo: 31);
        await Job(w.F).RunAsync(default);          // the stranded-client pass

        await using var db = await w.F.CreateDbContextAsync();
        // One for the pause, one for the offer to move.
        Assert.Equal(2, await db.UserMessageTos.CountAsync(t => t.ToAppUserId == primaryId));
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
    // ── overflow seats (2026-09-17 audit) ────────────────────────────────────

    /// <summary>
    /// A seat whose period ended without payment lapses, exactly as an organization's does.
    /// </summary>
    /// <remarks>
    /// Nothing lapsed a seat before this. StripeRenewalJob selects seats on
    /// <c>Status == Active &amp;&amp; CurrentPeriodEnd &lt;= now + window</c>, so a declined seat matched
    /// that window forever — retried every pass, and then back-charged once the card worked,
    /// because fulfilment dates the new period from the stale end. Six missed months became six
    /// charges in six days for one month of service.
    /// </remarks>
    [Fact]
    public async Task A_seat_whose_period_ended_stops_being_billed()
    {
        var w = await SeedAsync(DateTime.UtcNow.AddDays(-2));
        var seatId = Guid.NewGuid();

        await using (var db = await w.F.CreateDbContextAsync())
        {
            db.MemberSeatSubscriptions.Add(new MemberSeatSubscription
            {
                Id = seatId, OrganizationId = w.OrgId, AppUserId = w.ClientId,
                Status = SubscriptionStatus.Active, Interval = BillingInterval.Monthly,
                PriceAtStart = 5m,
                CurrentPeriodStart = DateTime.UtcNow.AddMonths(-1).AddDays(-2),
                CurrentPeriodEnd = DateTime.UtcNow.AddDays(-2),
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.OwnerId,
            });
            await db.SaveChangesAsync();
        }

        await Job(w.F).RunAsync(default);

        await using var after = await w.F.CreateDbContextAsync();
        var seat = await after.MemberSeatSubscriptions.SingleAsync(s => s.Id == seatId);
        Assert.Equal(SubscriptionStatus.Lapsed, seat.Status);
    }

    /// <summary>A seat still inside its period is left alone.</summary>
    [Fact]
    public async Task A_seat_still_inside_its_period_is_untouched()
    {
        var w = await SeedAsync(DateTime.UtcNow.AddDays(-2));
        var seatId = Guid.NewGuid();

        await using (var db = await w.F.CreateDbContextAsync())
        {
            db.MemberSeatSubscriptions.Add(new MemberSeatSubscription
            {
                Id = seatId, OrganizationId = w.OrgId, AppUserId = w.ClientId,
                Status = SubscriptionStatus.Active, Interval = BillingInterval.Monthly,
                PriceAtStart = 5m,
                CurrentPeriodStart = DateTime.UtcNow.AddDays(-2),
                CurrentPeriodEnd = DateTime.UtcNow.AddDays(28),
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.OwnerId,
            });
            await db.SaveChangesAsync();
        }

        await Job(w.F).RunAsync(default);

        await using var after = await w.F.CreateDbContextAsync();
        var seat = await after.MemberSeatSubscriptions.SingleAsync(s => s.Id == seatId);
        Assert.Equal(SubscriptionStatus.Active, seat.Status);
    }

    // ── what the notice says the next bill is (2026-09-17 audit) ─────────────

    /// <summary>Puts the group on a priced band at <paramref name="interval"/>.</summary>
    private static async Task PriceAsync(
        World w, BillingInterval interval, decimal price,
        bool bandedByMembers = true, OrganizationKind kind = OrganizationKind.InvestigationGroup,
        int tours = 0)
    {
        await using var db = await w.F.CreateDbContextAsync();
        var tierId = Guid.NewGuid();
        db.SubscriptionTiers.Add(new SubscriptionTier
        {
            Id = tierId, Name = "The band", MinMembers = 1, MaxMembers = null,
            IsBandedByMembers = bandedByMembers, IsActive = true, SortOrder = 1,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.OwnerId,
        });
        db.SubscriptionTierPrices.Add(new SubscriptionTierPrice
        {
            Id = Guid.NewGuid(), SubscriptionTierId = tierId, Interval = interval,
            Price = price, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.OwnerId,
        });

        var org = await db.Organizations.SingleAsync(o => o.Id == w.OrgId);
        org.Kind = kind;

        var addressId = Guid.NewGuid();
        for (var i = 0; i < tours; i++)
            db.Tours.Add(new Tour
            {
                Id = Guid.NewGuid(), OrganizationId = w.OrgId, Name = $"Walk {i}",
                UrlName = $"walk-{Guid.NewGuid():N}"[..12],
                StartOrganizationAddressId = addressId,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.OwnerId,
            });
        if (tours > 0)
            db.OrganizationAddresses.Add(new OrganizationAddress
            {
                Id = addressId, OrganizationId = w.OrgId, OrganizationAddressTypeId = Guid.NewGuid(),
                StreetAddress1 = "1 Printers Alley", City = "Nashville", State = "TN",
                ZipCode = "37201", Country = "US",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.OwnerId,
            });

        var sub = await db.OrganizationSubscriptions.SingleAsync();
        sub.SubscriptionTierId = tierId;
        sub.Interval = interval;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// A group billed quarterly is told its price per QUARTER.
    /// </summary>
    /// <remarks>
    /// The cadence noun was <c>Interval == Yearly ? "year" : "month"</c>, so quarterly and
    /// six-monthly groups were both told "per month" — beside their quarterly price. That
    /// understates what is about to be taken by three, in the one letter whose whole purpose is
    /// that the charge is expected.
    /// </remarks>
    [Fact]
    public async Task A_quarterly_group_is_told_its_price_per_quarter()
    {
        var w = await SeedAsync(DateTime.UtcNow.AddDays(7));
        await PriceAsync(w, BillingInterval.Quarterly, 40m);
        await TrialEndingAsync(w);

        await Job(w.F).RunAsync(default);

        var body = await LatestNoticeBodyAsync(w);
        Assert.Contains("per quarter", body);
        Assert.DoesNotContain("per month", body);
    }

    /// <summary>
    /// A tour business is told what its tours cost together, not what one costs.
    /// </summary>
    /// <remarks>
    /// An unbanded business tier's price is a UNIT price — "$29 per month is for a single tour no
    /// matter how many times scheduled" — so quoting it raw told a three-tour business $29 when
    /// the invoice is $87. The admin coupon path already carries a fix and a comment for exactly
    /// this; it was never carried across to the letter.
    /// </remarks>
    [Fact]
    public async Task A_tour_business_is_quoted_for_all_of_its_tours()
    {
        var w = await SeedAsync(DateTime.UtcNow.AddDays(7));
        await PriceAsync(w, BillingInterval.Monthly, 29m,
            bandedByMembers: false, kind: OrganizationKind.GhostWalkingTour, tours: 3);
        await TrialEndingAsync(w);

        await Job(w.F).RunAsync(default);

        var body = await LatestNoticeBodyAsync(w);
        Assert.Contains("$87.00", body);
        Assert.DoesNotContain("$29.00", body);
    }

    /// <summary>A monthly banded group reads exactly as it always did.</summary>
    [Fact]
    public async Task A_monthly_group_still_reads_per_month()
    {
        var w = await SeedAsync(DateTime.UtcNow.AddDays(7));
        await PriceAsync(w, BillingInterval.Monthly, 15m);
        await TrialEndingAsync(w);

        await Job(w.F).RunAsync(default);

        var body = await LatestNoticeBodyAsync(w);
        Assert.Contains("$15.00 per month", body);
    }

    /// <summary>The price line only appears on the trial-ending wording, so this arranges one.</summary>
    private static async Task TrialEndingAsync(World w)
    {
        await using var db = await w.F.CreateDbContextAsync();
        db.CouponRedemptions.Add(new CouponRedemption
        {
            Id = Guid.NewGuid(), CouponId = Guid.NewGuid(), CouponCodeId = Guid.NewGuid(),
            OrganizationId = w.OrgId, PeriodsRemaining = 1,
            RedeemedAtUtc = DateTime.UtcNow.AddMonths(-3),
            ListPrice = 49m, Discount = 49m, Payable = 0m,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.OwnerId,
        });
        await db.SaveChangesAsync();
    }

}
