using Ben.Data.Common;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Promotions;
using Ben.Data.WebApi.Services.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The founding groups promotion (Ben, 11/02/2026): who counts, the week, the free year, growing free, the score,
/// the prizes, and group-to-group referral months. On SQLite, so the one-person-one-group rule is the database's.
/// </summary>
public sealed class FoundingGroupsTests
{
    private static readonly Guid Small = Guid.NewGuid(), Standard = Guid.NewGuid(), Large = Guid.NewGuid(), Enterprise = Guid.NewGuid();
    private static readonly DateTime Start = new(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);

    private sealed class World(SqliteTestDb sqlite) : IAsyncDisposable
    {
        public SqliteTestDb Sqlite { get; } = sqlite;
        public IDbContextFactory<BenDataContext> F => Sqlite.Factory;
        public Task<BenDataContext> Db() => Sqlite.NewContextAsync();
        public ValueTask DisposeAsync() => Sqlite.DisposeAsync();

        public async Task<Guid> PersonAsync(string name, bool confirmed = true)
        {
            var id = Guid.NewGuid();
            await using var db = await Db();
            db.Users.Add(new AppUser
            {
                Id = id, UserName = $"{name}-{id:N}@t.com", Email = $"{name}-{id:N}@t.com", DisplayName = name,
                EmailConfirmed = confirmed, DateCreated = Start,
            });
            await db.SaveChangesAsync();
            return id;
        }

        public async Task<Guid> GroupAsync(string name, Guid owner, OrganizationKind kind = OrganizationKind.InvestigationGroup)
        {
            var id = Guid.NewGuid();
            await using var db = await Db();
            db.Organizations.Add(new Organization { Id = id, Name = name, UrlName = $"g{id:N}", Kind = kind, DateCreated = Start, CreatedByAppUserId = owner });
            await db.SaveChangesAsync();
            await JoinAsync(id, owner, OrganizationMemberRole.Owner);
            return id;
        }

        public async Task JoinAsync(Guid org, Guid person, OrganizationMemberRole role = OrganizationMemberRole.Member)
        {
            await using var db = await Db();
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = org, AppUserId = person, Role = role, IsActive = true,
                DateCreated = Start, CreatedByAppUserId = person,
            });
            await db.SaveChangesAsync();
        }

        public async Task<Promotion> OpenAsync()
        {
            await using var db = await Db();
            var p = await FoundingGroups.GetAsync(db, default);
            p.IsOpen = true;
            p.OpenedUtc = Start;
            await db.SaveChangesAsync();
            return p;
        }

        public async Task<Guid> ClaimAsync(Guid org, Guid by, DateTime? at = null)
        {
            await using var db = await Db();
            var p = await FoundingGroups.GetAsync(db, default);
            Assert.Null(await FoundingGroups.WhyCantClaimAsync(db, p, org, by, at ?? Start, default));
            var entry = FoundingGroups.Claim(db, p, org, by, at ?? Start);
            await db.SaveChangesAsync();
            return entry.Id;
        }

        public FoundingGroupsJob Job() => new(F, new PlatformMessageService(F),
            Options.Create(new SiteIdentity { BaseUrl = "https://ishaunted.test" }), NullLogger<FoundingGroupsJob>.Instance);

        public async Task<PromotionEntry> EntryAsync(Guid id)
        {
            await using var db = await Db();
            return await db.PromotionEntries.AsNoTracking().SingleAsync(e => e.Id == id);
        }

        public async Task<OrganizationSubscription?> PlanAsync(Guid org)
        {
            await using var db = await Db();
            return await db.OrganizationSubscriptions.AsNoTracking().SingleOrDefaultAsync(s => s.OrganizationId == org);
        }
    }

    private static async Task<World> CreateAsync()
    {
        var w = new World(await SqliteTestDb.CreateAsync());
        var by = await w.PersonAsync("Site");
        await using var db = await w.Db();
        foreach (var (id, name, min, max, sort) in new[]
                 {
                     (Small, "Small Group", 1, (int?)3, 1), (Standard, "Standard Group", 4, 10, 2),
                     (Large, "Large Group", 11, 25, 3), (Enterprise, "Enterprise", 26, null, 4),
                 })
        {
            db.SubscriptionTiers.Add(new SubscriptionTier
            {
                Id = id, Name = name, MinMembers = min, MaxMembers = max, SortOrder = sort, IsActive = true,
                DateCreated = Start, CreatedByAppUserId = by,
            });
            db.SubscriptionTierPrices.Add(new SubscriptionTierPrice
            {
                Id = Guid.NewGuid(), SubscriptionTierId = id, Interval = BillingInterval.Monthly, Price = 20m * sort,
                IsActive = true, DateCreated = Start, CreatedByAppUserId = by,
            });
        }
        await db.SaveChangesAsync();
        return w;
    }

    /// <summary>A group with an owner and <paramref name="others"/> more confirmed members, its spot claimed.</summary>
    private static async Task<(Guid Org, Guid Owner, Guid Entry)> FoundingGroupAsync(World w, int others, string name = "Lantern Society")
    {
        var owner = await w.PersonAsync($"{name} owner");
        var org = await w.GroupAsync(name, owner);
        for (var i = 0; i < others; i++) await w.JoinAsync(org, await w.PersonAsync($"{name} {i}"));
        return (org, owner, await w.ClaimAsync(org, owner));
    }

    // ── claiming ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Nothing_can_be_claimed_until_the_promotion_is_opened()
    {
        await using var w = await CreateAsync();
        var owner = await w.PersonAsync("o");
        var org = await w.GroupAsync("g", owner);
        await using var db = await w.Db();
        var p = await FoundingGroups.GetAsync(db, default);

        Assert.Equal("The founding groups promotion isn't open.", await FoundingGroups.WhyCantClaimAsync(db, p, org, owner, Start, default));
    }

    [Theory]
    [InlineData(OrganizationKind.GhostWalkingTour)]
    [InlineData(OrganizationKind.PublicEventProvider)]
    [InlineData(OrganizationKind.HauntedProperty)]
    public async Task Only_an_investigation_group_can_claim_a_spot(OrganizationKind kind)
    {
        await using var w = await CreateAsync();
        var p = await w.OpenAsync();
        var owner = await w.PersonAsync("o");
        var business = await w.GroupAsync("b", owner, kind);
        await using var db = await w.Db();

        Assert.StartsWith("Only paranormal investigation groups can take part.",
            await FoundingGroups.WhyCantClaimAsync(db, p, business, owner, Start, default));

        // An investigation group in the same promotion still can.
        var founder = await w.PersonAsync("f");
        await w.ClaimAsync(await w.GroupAsync("i", founder), founder);
    }

    [Fact]
    public async Task Claims_stop_when_the_spots_are_taken_and_when_the_window_closes()
    {
        await using var w = await CreateAsync();
        await w.OpenAsync();
        await using (var db = await w.Db())
        {
            (await db.Promotions.SingleAsync()).Spots = 1;
            await db.SaveChangesAsync();
        }
        await FoundingGroupAsync(w, 1, "First");
        var owner = await w.PersonAsync("o2");
        var org = await w.GroupAsync("Second", owner);

        await using var check = await w.Db();
        var p = await FoundingGroups.GetAsync(check, default);
        Assert.Equal("All the founding group spots have been taken.", await FoundingGroups.WhyCantClaimAsync(check, p, org, owner, Start, default));

        p.Spots = 10;
        Assert.Equal("Claims for the founding groups have closed.",
            await FoundingGroups.WhyCantClaimAsync(check, p, org, owner, Start.AddDays(p.ClaimWindowDays), default));
    }

    // ── who counts ────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_member_counts_once_confirmed_and_in_no_other_group_and_is_told_why_not()
    {
        await using var w = await CreateAsync();
        await w.OpenAsync();
        var (org, _, entryId) = await FoundingGroupAsync(w, 0);
        var unconfirmed = await w.PersonAsync("Una", confirmed: false);
        var elsewhere = await w.PersonAsync("Elsie");
        await w.GroupAsync("Elsie's other group", elsewhere);
        await w.JoinAsync(org, unconfirmed);
        await w.JoinAsync(org, elsewhere);

        await using var db = await w.Db();
        var entry = await db.PromotionEntries.SingleAsync(e => e.Id == entryId);
        Assert.Equal(1, await FoundingGroups.CountMembersAsync(db, entry, Start.AddHours(1), default));

        var standing = await FoundingGroups.MembersAsync(db, entry, default);
        Assert.Equal("hasn't confirmed their email address yet", standing.Single(m => m.AppUserId == unconfirmed).WhyNot);
        Assert.Equal("is also a member of another group", standing.Single(m => m.AppUserId == elsewhere).WhyNot);
    }

    [Fact]
    public async Task Nobody_is_counted_for_two_groups_and_somebody_counted_cant_start_a_second_free_group()
    {
        await using var w = await CreateAsync();
        await w.OpenAsync();
        var (orgA, ownerA, entryA) = await FoundingGroupAsync(w, 1, "A");
        await using (var db = await w.Db())
            await FoundingGroups.CountMembersAsync(db, await db.PromotionEntries.SingleAsync(e => e.Id == entryA), Start.AddHours(1), default);

        // The owner of A starts a second group: not while they're counted for A.
        var second = await w.GroupAsync("B", ownerA);
        await using var check = await w.Db();
        var p = await FoundingGroups.GetAsync(check, default);
        Assert.StartsWith("You're already counted for A", await FoundingGroups.WhyCantClaimAsync(check, p, second, ownerA, Start, default));

        // And the database itself refuses a second count.
        check.PromotionMembers.Add(new PromotionMember { Id = Guid.NewGuid(), PromotionId = p.Id, PromotionEntryId = entryA, AppUserId = ownerA, CountedUtc = Start });
        await Assert.ThrowsAsync<DbUpdateException>(() => check.SaveChangesAsync());
    }

    // ── the week ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(1, "Small Group")]
    [InlineData(2, "Small Group")]
    [InlineData(3, "Standard Group")]
    [InlineData(9, "Standard Group")]
    [InlineData(10, "Large Group")]
    [InlineData(24, "Large Group")]
    [InlineData(25, "Enterprise")]
    public async Task A_group_that_gathers_enough_members_gets_the_plan_its_size_earns_free_for_a_year(int others, string plan)
    {
        await using var w = await CreateAsync();
        await w.OpenAsync();
        var (org, _, entryId) = await FoundingGroupAsync(w, others);

        await w.Job().RunAsync(Start.AddHours(1), default);
        await w.Job().RunAsync(Start.AddDays(7).AddMinutes(1), default);

        var entry = await w.EntryAsync(entryId);
        Assert.Equal(PromotionEntryState.Qualified, entry.State);
        Assert.Equal(others + 1, entry.MembersAtQualifying);
        var sub = await w.PlanAsync(org);
        await using var db = await w.Db();
        Assert.Equal(plan, (await db.SubscriptionTiers.SingleAsync(t => t.Id == sub!.SubscriptionTierId)).Name);
        Assert.Equal(SubscriptionStatus.Active, sub!.Status);
        Assert.Equal(0m, sub.PriceAtPeriodStart);
        Assert.Equal(entry.QualifiedUtc!.Value.AddMonths(12), sub.CurrentPeriodEnd);
    }

    [Fact]
    public async Task A_group_of_one_loses_its_spot_and_its_founder_is_free_to_try_again()
    {
        await using var w = await CreateAsync();
        await w.OpenAsync();
        var (org, owner, entryId) = await FoundingGroupAsync(w, 0);

        await w.Job().RunAsync(Start.AddHours(1), default);
        await w.Job().RunAsync(Start.AddDays(7).AddMinutes(1), default);

        Assert.Equal(PromotionEntryState.Released, (await w.EntryAsync(entryId)).State);
        Assert.Null(await w.PlanAsync(org));
        await using var db = await w.Db();
        Assert.False(await db.PromotionMembers.AnyAsync(m => m.AppUserId == owner));
    }

    [Fact]
    public async Task A_member_who_leaves_during_the_week_stops_counting()
    {
        await using var w = await CreateAsync();
        await w.OpenAsync();
        var (org, owner, entryId) = await FoundingGroupAsync(w, 1);
        await w.Job().RunAsync(Start.AddHours(1), default);
        await using (var db = await w.Db())
        {
            foreach (var m in db.OrganizationUserMemberships.Where(m => m.OrganizationId == org && m.AppUserId != owner)) m.IsActive = false;
            await db.SaveChangesAsync();
        }

        await w.Job().RunAsync(Start.AddDays(7).AddMinutes(1), default);

        Assert.Equal(PromotionEntryState.Released, (await w.EntryAsync(entryId)).State);
    }

    // ── growing free ──────────────────────────────────────────────────────────

    [Fact]
    public async Task A_founding_group_that_grows_moves_up_a_plan_and_still_pays_nothing()
    {
        await using var w = await CreateAsync();
        await w.OpenAsync();
        var (org, _, entryId) = await FoundingGroupAsync(w, 1);
        await w.Job().RunAsync(Start.AddHours(1), default);
        await w.Job().RunAsync(Start.AddDays(7).AddMinutes(1), default);
        Assert.Equal(Small, (await w.PlanAsync(org))!.SubscriptionTierId);
        var end = (await w.PlanAsync(org))!.CurrentPeriodEnd;

        for (var i = 0; i < 3; i++) await w.JoinAsync(org, await w.PersonAsync($"late {i}"));
        await w.Job().RunAsync(Start.AddDays(40), default);

        var sub = (await w.PlanAsync(org))!;
        Assert.Equal(Standard, sub.SubscriptionTierId);
        Assert.Equal(0m, sub.PriceAtPeriodStart);
        Assert.Equal(end, sub.CurrentPeriodEnd);
    }

    // ── the score ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_score_is_per_member_so_a_small_group_isnt_outnumbered()
    {
        await using var w = await CreateAsync();
        var owner = await w.PersonAsync("o");
        var org = await w.GroupAsync("Two of us", owner);
        var other = await w.PersonAsync("p");
        await w.JoinAsync(org, other);
        await using var db = await w.Db();
        var inv = new Investigation
        {
            Id = Guid.NewGuid(), OrganizationId = org, Title = "The old mill", ScheduledDateTime = Start.AddDays(3),
            Status = InvestigationStatus.Completed, DateCreated = Start, CreatedByAppUserId = owner,
        };
        db.Investigations.Add(inv);
        foreach (var who in new[] { owner, other })
            db.InvestigationAttendees.Add(new InvestigationAttendee
            {
                Id = Guid.NewGuid(), InvestigationId = inv.Id, AppUserId = who, DidAttend = true,
                DateCreated = Start, CreatedByAppUserId = owner,
            });
        await db.SaveChangesAsync();
        var p = await FoundingGroups.GetAsync(db, default);

        var score = await FoundingGroupsScore.ScoreAsync(db, p, org, Start, Start.AddDays(30), default);

        Assert.Equal(2 * p.PointsPerInvestigation, score.Total);
        Assert.Equal(p.PointsPerInvestigation, score.PerMember);
        Assert.All(score.People, x => Assert.Equal(1, x.Investigations));
    }

    // ── free months ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Months_are_given_once_and_wait_until_a_plan_is_running()
    {
        await using var w = await CreateAsync();
        var owner = await w.PersonAsync("o");
        var org = await w.GroupAsync("g", owner);
        await using (var db = await w.Db())
        {
            Assert.True(await FreeMonths.GiveAsync(db, org, FreeMonthReason.Given, 2, "test:1", Start, owner, default));
            Assert.False(await FreeMonths.GiveAsync(db, org, FreeMonthReason.Given, 2, "test:1", Start, owner, default));
            await db.SaveChangesAsync();
            Assert.Equal(0, await FreeMonths.ApplyWaitingAsync(db, Start, default));
        }

        await using (var db = await w.Db())
        {
            db.OrganizationSubscriptions.Add(new OrganizationSubscription
            {
                Id = Guid.NewGuid(), OrganizationId = org, Status = SubscriptionStatus.Active, SubscriptionTierId = Small,
                CurrentPeriodStart = Start, CurrentPeriodEnd = Start.AddMonths(1), PriceAtPeriodStart = 20m,
                DateCreated = Start, CreatedByAppUserId = owner,
            });
            await db.SaveChangesAsync();
            Assert.Equal(1, await FreeMonths.ApplyWaitingAsync(db, Start, default));
        }

        Assert.Equal(Start.AddMonths(3), (await w.PlanAsync(org))!.CurrentPeriodEnd);
    }

    // ── referrals ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_group_cant_name_itself_or_a_group_with_the_same_leaders()
    {
        await using var w = await CreateAsync();
        var owner = await w.PersonAsync("o");
        var a = await w.GroupAsync("A", owner);
        var b = await w.GroupAsync("B", owner);
        var c = await w.GroupAsync("C", await w.PersonAsync("someone else"));
        await using var db = await w.Db();

        Assert.Equal("A group can't refer itself.", await FreeMonths.WhyCantNameReferrerAsync(db, a, a, default));
        Assert.StartsWith("That group shares an owner", await FreeMonths.WhyCantNameReferrerAsync(db, a, b, default));
        Assert.Null(await FreeMonths.WhyCantNameReferrerAsync(db, a, c, default));
    }

    [Fact]
    public async Task A_referral_earns_one_month_once_the_referred_groups_first_payment_has_stood_two_weeks()
    {
        await using var w = await CreateAsync();
        var referrerOwner = await w.PersonAsync("r");
        var referrer = await w.GroupAsync("Referrer", referrerOwner);
        var newOwner = await w.PersonAsync("n");
        var referred = await w.GroupAsync("Newcomers", newOwner);
        await using (var db = await w.Db())
        {
            var o = await db.Organizations.SingleAsync(x => x.Id == referred);
            o.ReferredByOrganizationId = referrer;
            o.ReferredAtUtc = Start;
            await db.SaveChangesAsync();
        }

        // A free plan isn't a payment: nothing yet.
        await w.Job().RunAsync(Start.AddDays(20), default);
        await using (var db = await w.Db()) Assert.False(await db.OrganizationFreeMonths.AnyAsync());

        await using (var db = await w.Db())
        {
            db.BillingLedgerEntries.Add(new BillingLedgerEntry
            {
                Id = Guid.NewGuid(), Kind = BillingLedgerKind.Payment, OrganizationId = referred, Amount = 20m,
                Description = "Small Group — monthly", PeriodStart = Start.AddDays(20), PeriodEnd = Start.AddDays(50),
                DateCreated = Start.AddDays(20), CreatedByAppUserId = newOwner,
            });
            await db.SaveChangesAsync();
        }

        await w.Job().RunAsync(Start.AddDays(30), default);
        await using (var db = await w.Db()) Assert.False(await db.OrganizationFreeMonths.AnyAsync());

        await w.Job().RunAsync(Start.AddDays(35), default);
        await w.Job().RunAsync(Start.AddDays(36), default);
        await using (var db = await w.Db())
        {
            var month = await db.OrganizationFreeMonths.SingleAsync();
            Assert.Equal((referrer, FreeMonthReason.Referral, 1, referred), (month.OrganizationId, month.Reason, month.Months, month.SourceOrganizationId));
            // Fixed once it has paid.
            Assert.StartsWith("This group has already paid", await FreeMonths.WhyCantNameReferrerAsync(db, referred, referrer, default));
        }
    }

    // ── the prizes ────────────────────────────────────────────────────────────

    private static async Task AttendAsync(World w, Guid org, DateTime when, params Guid[] who)
    {
        await using var db = await w.Db();
        var inv = new Investigation
        {
            Id = Guid.NewGuid(), OrganizationId = org, Title = "A night", ScheduledDateTime = when,
            Status = InvestigationStatus.Completed, DateCreated = when, CreatedByAppUserId = who[0],
        };
        db.Investigations.Add(inv);
        foreach (var person in who)
            db.InvestigationAttendees.Add(new InvestigationAttendee
            {
                Id = Guid.NewGuid(), InvestigationId = inv.Id, AppUserId = person, DidAttend = true,
                DateCreated = when, CreatedByAppUserId = who[0],
            });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task The_group_of_the_month_gets_a_free_month_but_not_two_months_running()
    {
        await using var w = await CreateAsync();
        await w.OpenAsync();
        var (busy, busyOwner, _) = await FoundingGroupAsync(w, 1, "Busy");
        var (quiet, quietOwner, _) = await FoundingGroupAsync(w, 1, "Quiet");
        await w.Job().RunAsync(Start.AddHours(1), default);
        await w.Job().RunAsync(Start.AddDays(7).AddMinutes(1), default);

        // December: Busy does the most; Quiet does a little. January: the same.
        foreach (var month in new[] { new DateTime(2026, 12, 10, 0, 0, 0, DateTimeKind.Utc), new DateTime(2027, 1, 10, 0, 0, 0, DateTimeKind.Utc) })
        {
            await AttendAsync(w, busy, month, busyOwner);
            await AttendAsync(w, busy, month.AddDays(1), busyOwner);
            await AttendAsync(w, quiet, month, quietOwner);
        }
        await w.Job().RunAsync(new DateTime(2027, 2, 1, 1, 0, 0, DateTimeKind.Utc), default);

        await using var db = await w.Db();
        var p = await FoundingGroups.GetAsync(db, default);
        var won = await db.OrganizationFreeMonths.Where(m => m.Reason == FreeMonthReason.GroupOfTheMonth).ToListAsync();
        Assert.Equal(busy, won.Single(m => m.Key == FoundingGroupsJob.MonthKey(p, new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc))).OrganizationId);
        Assert.Equal(quiet, won.Single(m => m.Key == FoundingGroupsJob.MonthKey(p, new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc))).OrganizationId);
        Assert.Equal(new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), p.GroupOfTheMonthDecidedThroughUtc);
    }

    [Fact]
    public async Task Once_every_group_has_finished_its_year_the_top_three_win_a_second_year()
    {
        await using var w = await CreateAsync();
        await w.OpenAsync();
        var groups = new List<(Guid Org, Guid Owner)>();
        for (var i = 0; i < 4; i++)
        {
            var (org, owner, _) = await FoundingGroupAsync(w, 1, $"Group {i}");
            groups.Add((org, owner));
        }
        await w.Job().RunAsync(Start.AddHours(1), default);
        await w.Job().RunAsync(Start.AddDays(7).AddMinutes(1), default);

        // Group i attends i investigations, so group 3 is first and group 0 last.
        for (var i = 0; i < groups.Count; i++)
            for (var n = 0; n < i; n++)
                await AttendAsync(w, groups[i].Org, Start.AddDays(30 + n), groups[i].Owner);

        // Claims close at 90 days; every year ends a year after the week.
        await w.Job().RunAsync(Start.AddDays(7).AddMonths(12).AddDays(1), default);

        await using var db = await w.Db();
        var second = await db.OrganizationFreeMonths.Where(m => m.Reason == FreeMonthReason.SecondYear).Select(m => m.OrganizationId).ToListAsync();
        Assert.Equal(new[] { groups[1].Org, groups[2].Org, groups[3].Org }.OrderBy(x => x), second.OrderBy(x => x));
        Assert.NotNull((await FoundingGroups.GetAsync(db, default)).DecidedUtc);
        Assert.Equal(1, (await db.PromotionEntries.SingleAsync(e => e.OrganizationId == groups[3].Org)).FinalRank);
    }
}
