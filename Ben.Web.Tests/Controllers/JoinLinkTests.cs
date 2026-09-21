using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// The way into a group (2026-09-20).
/// </summary>
/// <remarks>
/// <para>Until this existed, a membership was created four ways — founding a group, an accepted
/// application, the solo plan, and the seeders — and applications can only be opened on a paid
/// plan. So the founder of a new group sat on a Members screen with one row, no applications, and
/// nothing at all that added a person, while the hub's own guided tour told them the tab was for
/// inviting people.</para>
///
/// <para>Two things carry weight here. <b>Reading an invitation changes nothing</b>, because a
/// chat app's link preview or a mail scanner opens every URL it sees and must not enrol anybody.
/// And <b>the plan is checked at the moment of joining</b>, through the one method that answers it
/// everywhere, so a link cannot be the route around the rule that the acceptance path enforces —
/// which is exactly what <c>OrganizationMembershipRequestController</c>'s own comment warned about
/// ("an application can arrive by a route that never reads that flag — an invite link") before any
/// such route existed.</para>
/// </remarks>
public sealed class JoinLinkTests
{
    private static readonly Guid Founder = Guid.NewGuid();
    private static readonly Guid Newcomer = Guid.NewGuid();

    private static IDbContextFactory<BenDataContext> CreateFactory()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static PublicOrganizationJoinController Build(
        IDbContextFactory<BenDataContext> f, Guid? signedInAs)
    {
        var identity = signedInAs is { } id
            ? new ClaimsIdentity([new Claim("app_user_id", id.ToString())], "test")
            : new ClaimsIdentity();

        return new PublicOrganizationJoinController(f)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
            },
        };
    }

    /// <summary>A group with a live link, <paramref name="members"/> people, and maybe a plan.</summary>
    private static async Task<(Guid OrgId, string Token)> SeedAsync(
        IDbContextFactory<BenDataContext> f,
        int members = 1,
        SubscriptionStatus? plan = null,
        DateTime? expires = null)
    {
        var orgId = Guid.NewGuid();
        var token = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;

        await using var db = await f.CreateDbContextAsync();

        foreach (var id in new[] { Founder, Newcomer })
        {
            if (!await db.AppUsers.AnyAsync(u => u.Id == id))
            {
                db.AppUsers.Add(new AppUser
                {
                    Id = id, UserName = $"{id}@t.com", Email = $"{id}@t.com",
                    DisplayName = "Person", DateCreated = now,
                });
            }
        }

        db.Organizations.Add(new Organization
        {
            Id = orgId, Name = "Hollow Creek", UrlName = $"g-{orgId:N}",
            JoinToken = token,
            JoinTokenExpiresUtc = expires ?? now.AddDays(14),
            JoinTokenCreatedByAppUserId = Founder,
            DateCreated = now, CreatedByAppUserId = Founder,
        });

        for (var i = 0; i < members; i++)
        {
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = orgId,
                AppUserId = i == 0 ? Founder : Guid.NewGuid(),
                Role = i == 0 ? OrganizationMemberRole.Owner : OrganizationMemberRole.Member,
                IsActive = true, DateCreated = now, CreatedByAppUserId = Founder,
            });
        }

        if (plan is { } status)
        {
            db.OrganizationSubscriptions.Add(new OrganizationSubscription
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, Status = status,
                CurrentPeriodEnd = now.AddDays(30),
                DateCreated = now, CreatedByAppUserId = Founder,
            });
        }

        await db.SaveChangesAsync();
        return (orgId, token);
    }

    private static async Task<int> MemberCountAsync(IDbContextFactory<BenDataContext> f, Guid orgId)
    {
        await using var db = await f.CreateDbContextAsync();
        return await db.OrganizationUserMemberships.CountAsync(m => m.OrganizationId == orgId && m.IsActive);
    }

    // ── reading ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Reading an invitation enrols nobody.
    /// </summary>
    /// <remarks>
    /// The load-bearing one. A chat app's link preview fetcher, a mail scanner and a browser's
    /// prefetch all open every URL they see; if GET admitted people, pasting the link into a
    /// message would add whichever robot read it first.
    /// </remarks>
    [Fact]
    public async Task Reading_an_invitation_adds_nobody()
    {
        var f = CreateFactory();
        var (orgId, token) = await SeedAsync(f, plan: SubscriptionStatus.Active);

        var result = await Build(f, Newcomer).Get(token, default);
        var invite = Assert.IsType<OrganizationJoinInviteRecord>(
            Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Equal("Hollow Creek", invite.OrganizationName);
        Assert.False(invite.AlreadyAMember);
        Assert.Equal(1, await MemberCountAsync(f, orgId));
    }

    [Fact]
    public async Task An_unknown_token_says_nothing_about_the_group()
    {
        var f = CreateFactory();
        await SeedAsync(f);

        Assert.IsType<NotFoundResult>(
            (await Build(f, Newcomer).Get("not-a-real-token", default)).Result);
    }

    /// <summary>
    /// An expired link is indistinguishable from a wrong one, on purpose: neither tells a stranger
    /// holding a stale link whether the group exists.
    /// </summary>
    [Fact]
    public async Task An_expired_link_is_gone()
    {
        var f = CreateFactory();
        var (_, token) = await SeedAsync(f, expires: DateTime.UtcNow.AddMinutes(-1));

        Assert.IsType<NotFoundResult>((await Build(f, Newcomer).Get(token, default)).Result);
        Assert.IsType<NotFoundResult>((await Build(f, Newcomer).Accept(token, default)).Result);
    }

    // ── joining ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Walking_through_the_link_makes_a_member()
    {
        var f = CreateFactory();
        var (orgId, token) = await SeedAsync(f, plan: SubscriptionStatus.Active);

        var result = await Build(f, Newcomer).Accept(token, default);
        Assert.IsType<OkObjectResult>(result.Result);

        await using var db = await f.CreateDbContextAsync();
        var joined = await db.OrganizationUserMemberships
            .FirstOrDefaultAsync(m => m.OrganizationId == orgId && m.AppUserId == Newcomer);

        Assert.NotNull(joined);
        Assert.True(joined!.IsActive);
        // It admits; it does not elevate.
        Assert.Equal(OrganizationMemberRole.Member, joined.Role);
    }

    /// <summary>
    /// The link is one link for a whole group of people, so using it does not spend it — which is
    /// what makes "paste it in the group chat" the thing an organiser actually does.
    /// </summary>
    [Fact]
    public async Task The_link_keeps_working_for_the_next_person()
    {
        var f = CreateFactory();
        var (orgId, token) = await SeedAsync(f, plan: SubscriptionStatus.Active);

        Assert.IsType<OkObjectResult>((await Build(f, Newcomer).Accept(token, default)).Result);

        var second = Guid.NewGuid();
        await using (var db = await f.CreateDbContextAsync())
        {
            db.AppUsers.Add(new AppUser
            {
                Id = second, UserName = "s@t.com", Email = "s@t.com",
                DisplayName = "Second", DateCreated = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        Assert.IsType<OkObjectResult>((await Build(f, second).Accept(token, default)).Result);
        Assert.Equal(3, await MemberCountAsync(f, orgId));
    }

    [Fact]
    public async Task Joining_twice_changes_nothing()
    {
        var f = CreateFactory();
        var (orgId, token) = await SeedAsync(f, plan: SubscriptionStatus.Active);

        await Build(f, Newcomer).Accept(token, default);
        await Build(f, Newcomer).Accept(token, default);

        Assert.Equal(2, await MemberCountAsync(f, orgId));
    }

    [Fact]
    public async Task Somebody_with_no_account_is_not_joined()
    {
        var f = CreateFactory();
        var (orgId, token) = await SeedAsync(f, plan: SubscriptionStatus.Active);

        Assert.IsType<UnauthorizedResult>((await Build(f, signedInAs: null).Accept(token, default)).Result);
        Assert.Equal(1, await MemberCountAsync(f, orgId));
    }

    // ── the plan ─────────────────────────────────────────────────────────────

    /// <summary>
    /// A link is not a way around the price of a second member.
    /// </summary>
    /// <remarks>
    /// The same gate the acceptance path runs, called the same way, so there is one answer on the
    /// site — and the refusal is the plan's own sentence rather than a bare status, because the
    /// page shows it to the person who clicked.
    /// </remarks>
    [Fact]
    public async Task A_free_group_of_one_cannot_take_a_second_person_through_the_link()
    {
        var f = CreateFactory();
        var (orgId, token) = await SeedAsync(f, members: 1);

        var refused = Assert.IsType<ObjectResult>((await Build(f, Newcomer).Accept(token, default)).Result);

        Assert.Equal(StatusCodes.Status402PaymentRequired, refused.StatusCode);
        Assert.Contains("paid plan", Assert.IsType<string>(refused.Value));
        Assert.Equal(1, await MemberCountAsync(f, orgId));
    }

    /// <summary>The first person into an empty group is free, here as everywhere.</summary>
    [Fact]
    public async Task An_empty_group_takes_its_first_person_for_nothing()
    {
        var f = CreateFactory();
        var (orgId, token) = await SeedAsync(f, members: 0);

        Assert.IsType<OkObjectResult>((await Build(f, Newcomer).Accept(token, default)).Result);
        Assert.Equal(1, await MemberCountAsync(f, orgId));
    }

    /// <summary>
    /// A group of one that gains a second person has stopped being one person's workspace, and
    /// should stop being hidden from the places groups are found — the same thing the acceptance
    /// path does.
    /// </summary>
    [Fact]
    public async Task A_personal_group_that_gains_somebody_becomes_a_group()
    {
        var f = CreateFactory();
        var (orgId, token) = await SeedAsync(f, plan: SubscriptionStatus.Active);

        await using (var db = await f.CreateDbContextAsync())
        {
            var org = await db.Organizations.FirstAsync(o => o.Id == orgId);
            org.IsPersonal = true;
            await db.SaveChangesAsync();
        }

        await Build(f, Newcomer).Accept(token, default);

        await using var read = await f.CreateDbContextAsync();
        Assert.False((await read.Organizations.FirstAsync(o => o.Id == orgId)).IsPersonal);
    }
}
