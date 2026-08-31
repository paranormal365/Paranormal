using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Billing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Where the free lane ends and a plan begins (Ben, 2026-08-31).
/// </summary>
/// <remarks>
/// <para>Two rules, both chosen to restrict as little as possible while closing the way the
/// product could be gamed. <b>Privacy:</b> publishing stays an act anybody may perform, and what a
/// free account cannot do is take it back — publish-then-hide is the exploit, not declining to
/// publish. <b>People:</b> one person is free, and working with somebody else is the paid part.</para>
///
/// <para>The load-bearing test in here is the last one. Nothing about either rule may remove
/// anybody who is already a member, because a rule that evicted people to make a point would cost
/// far more than it collects — and one of the groups it would hit is the one App Review signs
/// into.</para>
/// </remarks>
public sealed class PaidPlanTests
{
    private static IDbContextFactory<BenDataContext> CreateFactory()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<Guid> AddUserAsync(IDbContextFactory<BenDataContext> f)
    {
        var id = Guid.NewGuid();
        await using var db = await f.CreateDbContextAsync();
        db.AppUsers.Add(new AppUser
        {
            Id = id, UserName = $"{id}@t.com", Email = $"{id}@t.com",
            DisplayName = "Person", DateCreated = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return id;
    }

    /// <summary>A group with <paramref name="members"/> people, optionally on an active plan.</summary>
    private static async Task<Guid> AddGroupAsync(
        IDbContextFactory<BenDataContext> f, int members, SubscriptionStatus? plan = null)
    {
        var orgId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using var db = await f.CreateDbContextAsync();
        db.Organizations.Add(new Organization
        {
            Id = orgId, Name = "Group", UrlName = $"g-{orgId:N}",
            DateCreated = now, CreatedByAppUserId = Guid.NewGuid(),
        });
        for (var i = 0; i < members; i++)
        {
            var userId = Guid.NewGuid();
            db.AppUsers.Add(new AppUser
            {
                Id = userId, UserName = $"{userId}@t.com", Email = $"{userId}@t.com",
                DisplayName = $"Member {i}", DateCreated = now,
            });
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = userId,
                Role = OrganizationMemberRole.Member, IsActive = true,
                DateCreated = now, CreatedByAppUserId = userId,
            });
        }
        if (plan is { } status)
        {
            db.OrganizationSubscriptions.Add(new OrganizationSubscription
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, Status = status,
                Interval = BillingInterval.Monthly,
                DateCreated = now, CreatedByAppUserId = Guid.NewGuid(),
            });
        }
        await db.SaveChangesAsync();
        return orgId;
    }

    // ── privacy ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_free_account_cannot_keep_its_sessions_private()
    {
        var f = CreateFactory();
        var user = await AddUserAsync(f);

        await using var db = await f.CreateDbContextAsync();
        var why = await PaidPlan.WhyCannotKeepPrivateAsync(db, user, default);

        Assert.NotNull(why);
        // The sentence has to sell the plan, not just refuse — it is the paywall's own words.
        Assert.Contains("paid plan", why);
    }

    [Fact]
    public async Task Somebody_on_an_active_plan_may_keep_their_sessions_private()
    {
        var f = CreateFactory();
        var user = await AddUserAsync(f);
        var orgId = await AddGroupAsync(f, members: 0, plan: SubscriptionStatus.Active);

        await using (var db = await f.CreateDbContextAsync())
        {
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = user,
                Role = OrganizationMemberRole.Owner, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = user,
            });
            await db.SaveChangesAsync();
        }

        await using var read = await f.CreateDbContextAsync();
        Assert.Null(await PaidPlan.WhyCannotKeepPrivateAsync(read, user, default));
    }

    /// <summary>
    /// A lapsed plan is not a paid one, or letting a subscription expire would be a way to keep
    /// everything it bought, forever.
    /// </summary>
    [Fact]
    public async Task A_lapsed_plan_does_not_buy_privacy()
    {
        var f = CreateFactory();
        var user = await AddUserAsync(f);
        var orgId = await AddGroupAsync(f, members: 0, plan: SubscriptionStatus.Lapsed);

        await using (var db = await f.CreateDbContextAsync())
        {
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = user,
                Role = OrganizationMemberRole.Owner, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = user,
            });
            await db.SaveChangesAsync();
        }

        await using var read = await f.CreateDbContextAsync();
        Assert.NotNull(await PaidPlan.WhyCannotKeepPrivateAsync(read, user, default));
    }

    // ── people ───────────────────────────────────────────────────────────────

    /// <summary>The first person is free — a group of one costs nothing.</summary>
    [Fact]
    public async Task An_empty_group_may_take_its_first_member()
    {
        var f = CreateFactory();
        var orgId = await AddGroupAsync(f, members: 0);

        await using var db = await f.CreateDbContextAsync();
        Assert.Null(await PaidPlan.WhyCannotAddMemberAsync(db, orgId, default));
    }

    /// <summary>The SECOND person is what asks for a plan.</summary>
    [Fact]
    public async Task A_group_of_one_needs_a_plan_for_its_second_member()
    {
        var f = CreateFactory();
        var orgId = await AddGroupAsync(f, members: 1);

        await using var db = await f.CreateDbContextAsync();
        var why = await PaidPlan.WhyCannotAddMemberAsync(db, orgId, default);

        Assert.NotNull(why);
        // Reassurance is part of the sentence: nobody is being removed.
        Assert.Contains("Everybody already here stays", why);
    }

    [Fact]
    public async Task A_group_on_an_active_plan_may_add_freely()
    {
        var f = CreateFactory();
        var orgId = await AddGroupAsync(f, members: 5, plan: SubscriptionStatus.Active);

        await using var db = await f.CreateDbContextAsync();
        Assert.Null(await PaidPlan.WhyCannotAddMemberAsync(db, orgId, default));
    }

    // ── and nobody is ever removed ───────────────────────────────────────────

    /// <summary>
    /// The rule refuses an ADDITION and touches nothing that exists. A free group that already
    /// has several people keeps every one of them and keeps working — which matters concretely
    /// right now, because App Review signs into exactly such a group.
    /// </summary>
    [Fact]
    public async Task An_existing_free_group_keeps_every_member_it_already_had()
    {
        var f = CreateFactory();
        var orgId = await AddGroupAsync(f, members: 4);

        await using var db = await f.CreateDbContextAsync();

        // Asking the question must not change the answer to any other one.
        Assert.NotNull(await PaidPlan.WhyCannotAddMemberAsync(db, orgId, default));

        var stillActive = await db.OrganizationUserMemberships
            .CountAsync(m => m.OrganizationId == orgId && m.IsActive, default);
        Assert.Equal(4, stillActive);
    }
}
