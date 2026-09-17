using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Billing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// The hidden one-person organization behind a solo plan.
/// </summary>
/// <remarks>
/// <para>Everything the solo tier sells is org-scoped — cases, subscriptions, privacy,
/// private-residence work — so a solo subscriber gets an organization rather than a parallel
/// account-level implementation of each. What makes it different is one thing only: it is never
/// presented as a group to be found or joined. A leak there does not merely look wrong, it
/// publishes that a named individual pays for the site.</para>
///
/// <para>The last test here is not about this feature at all: it pins that an ORDINARY group is
/// untouched by the flag's arrival. <c>IsPersonal</c> defaults to false, so every organization
/// that existed before this stays visible — including the group App Review signs into.</para>
/// </remarks>
public sealed class PersonalOrganizationTests
{
    private static IDbContextFactory<BenDataContext> CreateFactory()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<Guid> AddUserAsync(
        IDbContextFactory<BenDataContext> f, string display = "Ada Vance", string? handle = "ada")
    {
        var id = Guid.NewGuid();
        await using var db = await f.CreateDbContextAsync();
        db.AppUsers.Add(new AppUser
        {
            Id = id, UserName = $"{id}@t.com", Email = $"{id}@t.com",
            DisplayName = display, Handle = handle, DateCreated = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static SoloPlanController Build(IDbContextFactory<BenDataContext> f, Guid userId)
        => new(f)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Bearer")),
                },
            },
        };

    private static async Task<SoloPlanController.SoloPlanRecord> StartAsync(
        IDbContextFactory<BenDataContext> f, Guid userId)
    {
        var result = (await Build(f, userId).Start(default)).Result;
        if (result is ObjectResult { Value: not SoloPlanController.SoloPlanRecord } other)
            Assert.Fail($"refused: HTTP {other.StatusCode} — {other.Value}");
        return Assert.IsType<SoloPlanController.SoloPlanRecord>(
            Assert.IsType<OkObjectResult>(result).Value);
    }

    // ── creation ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Starting_a_solo_plan_creates_a_personal_organization_owned_by_the_person()
    {
        var f = CreateFactory();
        var user = await AddUserAsync(f);

        var plan = await StartAsync(f, user);

        await using var db = await f.CreateDbContextAsync();
        var org = await db.Organizations.SingleAsync();
        Assert.Equal(plan.OrganizationId, org.Id);
        Assert.True(org.IsPersonal);
        Assert.Equal("Ada Vance", org.Name);

        var membership = await db.OrganizationUserMemberships.SingleAsync();
        Assert.Equal(user, membership.AppUserId);
        Assert.Equal(OrganizationMemberRole.Owner, membership.Role);
        Assert.True(membership.IsActive);
    }

    /// <summary>
    /// A reduced skeleton would mean a second code path in every feature a personal organization
    /// touches. It is a real organization that happens to have one member.
    /// </summary>
    [Fact]
    public async Task A_personal_organization_gets_the_full_default_skeleton()
    {
        var f = CreateFactory();
        var plan = await StartAsync(f, await AddUserAsync(f));

        await using var db = await f.CreateDbContextAsync();
        Assert.NotEmpty(await db.OrganizationRoles.Where(r => r.OrganizationId == plan.OrganizationId).ToListAsync());
        Assert.NotEmpty(await db.OrganizationMemberLevels.Where(l => l.OrganizationId == plan.OrganizationId).ToListAsync());
    }

    /// <summary>
    /// Two personal organizations for one person would leave an orphan carrying its own cases
    /// that no screen would ever show them.
    /// </summary>
    [Fact]
    public async Task Starting_twice_returns_the_same_organization()
    {
        var f = CreateFactory();
        var user = await AddUserAsync(f);

        var first = await StartAsync(f, user);
        var second = await StartAsync(f, user);

        Assert.Equal(first.OrganizationId, second.OrganizationId);
        await using var db = await f.CreateDbContextAsync();
        Assert.Equal(1, await db.Organizations.CountAsync());
    }

    [Fact]
    public async Task Two_people_with_the_same_handle_shape_get_distinct_url_names()
    {
        var f = CreateFactory();
        var first = await StartAsync(f, await AddUserAsync(f, handle: "ada"));
        var second = await StartAsync(f, await AddUserAsync(f, handle: "ada"));

        await using var db = await f.CreateDbContextAsync();
        var names = await db.Organizations.Select(o => o.UrlName).ToListAsync();
        Assert.Equal(2, names.Distinct().Count());
        Assert.NotEqual(first.OrganizationId, second.OrganizationId);
    }

    [Fact]
    public async Task Somebody_with_no_handle_still_gets_a_url_name()
    {
        var f = CreateFactory();
        await StartAsync(f, await AddUserAsync(f, handle: null));

        await using var db = await f.CreateDbContextAsync();
        Assert.False(string.IsNullOrWhiteSpace((await db.Organizations.SingleAsync()).UrlName));
    }

    // ── never presented as a group ───────────────────────────────────────────

    [Fact]
    public async Task A_personal_organization_is_excluded_from_group_listings()
    {
        var f = CreateFactory();
        await StartAsync(f, await AddUserAsync(f));

        await using var db = await f.CreateDbContextAsync();
        Assert.Empty(await db.Organizations
            .Where(PersonalOrganizations.Discoverable)
            .ToListAsync());
    }

    /// <summary>
    /// The record says what it is, rather than leaving the filter as the only thing that knows.
    /// A personal organization is nobody's service provider.
    /// </summary>
    [Fact]
    public async Task A_personal_organization_does_not_offer_itself_to_clients_or_joiners()
    {
        var f = CreateFactory();
        await StartAsync(f, await AddUserAsync(f));

        await using var db = await f.CreateDbContextAsync();
        var org = await db.Organizations.SingleAsync();
        Assert.False(org.IsAcceptingClients);
        Assert.False(org.IsAcceptingApplications);
        Assert.False(org.RunsPublicTours);
    }

    // ── what a personal organization may do is decided by its plan, not by being personal ──

    /// <summary>
    /// Ben, 2026-09-17: "if paid, they can make their work private. By default we should be able to
    /// collect information as public for unpaid plan."
    /// </summary>
    /// <remarks>
    /// This replaces <c>A_personal_organization_may_not_open_a_case</c>, which asserted the opposite
    /// on the strength of the 2026-08-31 sentence "a solo plan does not take client work". That gate
    /// was keyed on <c>IsPersonal</c> — a fact about the record — and so refused the paid solo
    /// subscriber the solo band is sold to, while letting an unpaid group of one keep everything
    /// private. Opening a case is now nobody's special case; how public it is, is the plan's answer.
    /// </remarks>
    [Fact]
    public async Task A_personal_organization_opens_cases_like_anybody_else()
    {
        var f = CreateFactory();
        await StartAsync(f, await AddUserAsync(f));

        await using var db = await f.CreateDbContextAsync();
        var org = await db.Organizations.SingleAsync();

        // Nothing about being personal refuses anything any more. The only rule left here is
        // whether it may appear where groups are presented, which it may not.
        Assert.True(org.IsPersonal);
        Assert.False(await db.Organizations.Where(PersonalOrganizations.Discoverable)
                                          .AnyAsync(o => o.Id == org.Id));
    }

    /// <summary>
    /// A personal organization that pays nothing is public by default; one that pays is not. The
    /// question is the subscription, and the answer is the same one storage and the archive use.
    /// </summary>
    [Fact]
    public async Task What_a_personal_organization_may_keep_private_is_its_plans_answer()
    {
        var f = CreateFactory();
        var plan = await StartAsync(f, await AddUserAsync(f));

        await using var db = await f.CreateDbContextAsync();

        Assert.True(await PaidPlan.PublicByDefaultAsync(db, plan.OrganizationId, default));
        Assert.NotNull(await PaidPlan.WhyCannotKeepCasePrivateAsync(db, plan.OrganizationId, default));

        db.OrganizationSubscriptions.Add(new Ben.Data.Source.Entities.OrganizationSubscription
        {
            Id = Guid.NewGuid(),
            OrganizationId = plan.OrganizationId,
            Status = Ben.Data.Common.Enums.SubscriptionStatus.Active,
            DateCreated = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        Assert.False(await PaidPlan.PublicByDefaultAsync(db, plan.OrganizationId, default));
        Assert.Null(await PaidPlan.WhyCannotKeepCasePrivateAsync(db, plan.OrganizationId, default));
    }

    /// <summary>
    /// Adding people is a question about the PLAN, not about being personal — a solo person who
    /// pays may work with somebody. So the rule lives in one place, and it is not this one.
    /// </summary>
    [Fact]
    public async Task Adding_a_member_is_refused_by_the_plan_rather_than_by_being_personal()
    {
        var f = CreateFactory();
        var plan = await StartAsync(f, await AddUserAsync(f));

        await using var db = await f.CreateDbContextAsync();

        var why = await PaidPlan.WhyCannotAddMemberAsync(db, plan.OrganizationId, default);
        Assert.NotNull(why);
        Assert.Contains("part of a paid plan", why);
    }

    /// <summary>
    /// A personal organization's visits at a landmark are public ones — because it pays nothing,
    /// not because it is personal. An unpaid ordinary group gets exactly the same answer, which is
    /// the whole point of moving the rule.
    /// </summary>
    [Fact]
    public async Task An_unpaid_groups_investigations_are_public_ones_personal_or_not()
    {
        var f = CreateFactory();
        var plan = await StartAsync(f, await AddUserAsync(f));

        var ordinaryId = Guid.NewGuid();
        await using (var seed = await f.CreateDbContextAsync())
        {
            seed.Organizations.Add(new Organization
            {
                Id = ordinaryId, Name = "Paranormal365", UrlName = "paranormal365",
                IsPersonal = false, DateCreated = DateTime.UtcNow,
            });
            await seed.SaveChangesAsync();
        }

        await using var db = await f.CreateDbContextAsync();

        foreach (var orgId in new[] { plan.OrganizationId, ordinaryId })
        {
            Assert.NotNull(await PaidPlan.WhyCannotNarrowInvestigationAsync(db, orgId, default));
        }
    }

    // ── unlisted: a real group that has chosen not to be found ───────────────

    /// <summary>
    /// Ben, 2026-08-31: the account App Review signs into exists to get the app approved and has
    /// no business appearing in a directory of real groups.
    /// </summary>
    [Fact]
    public async Task An_unlisted_group_is_excluded_from_directories()
    {
        var f = CreateFactory();
        var owner = await AddUserAsync(f);

        await using (var db = await f.CreateDbContextAsync())
        {
            db.Organizations.Add(new Organization
            {
                Id = Guid.NewGuid(), Name = "Paranormal365", UrlName = "paranormal365",
                IsUnlisted = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = owner,
            });
            await db.SaveChangesAsync();
        }

        await using var read = await f.CreateDbContextAsync();
        Assert.Empty(await read.Organizations.Where(PersonalOrganizations.Discoverable).ToListAsync());
    }

    /// <summary>
    /// Unlisted is about being FOUND, not about being private. The group is still there, its
    /// members are still members, and its page still works for anybody given the link — so a
    /// filter that removed the ROW rather than hiding it from directories would be a different
    /// and much worse feature.
    /// </summary>
    [Fact]
    public async Task An_unlisted_group_still_exists_and_keeps_its_members()
    {
        var f = CreateFactory();
        var owner = await AddUserAsync(f);
        var orgId = Guid.NewGuid();

        await using (var db = await f.CreateDbContextAsync())
        {
            db.Organizations.Add(new Organization
            {
                Id = orgId, Name = "Paranormal365", UrlName = "paranormal365",
                IsUnlisted = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = owner,
            });
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = owner,
                Role = OrganizationMemberRole.Owner, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = owner,
            });
            await db.SaveChangesAsync();
        }

        await using var read = await f.CreateDbContextAsync();
        Assert.Single(await read.Organizations.ToListAsync());
        Assert.Single(await read.OrganizationUserMemberships.Where(m => m.IsActive).ToListAsync());
    }

    /// <summary>
    /// Unlisted and personal are different reasons answered by one predicate. Neither implies the
    /// other: an unlisted group is a real group, and a personal organization was never one.
    /// </summary>
    [Fact]
    public async Task Unlisted_and_personal_are_independent()
    {
        var f = CreateFactory();
        var owner = await AddUserAsync(f);

        await using (var db = await f.CreateDbContextAsync())
        {
            db.Organizations.Add(new Organization
            {
                Id = Guid.NewGuid(), Name = "Unlisted only", UrlName = "u1",
                IsUnlisted = true, IsPersonal = false,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = owner,
            });
            db.Organizations.Add(new Organization
            {
                Id = Guid.NewGuid(), Name = "Listed group", UrlName = "u2",
                IsUnlisted = false, IsPersonal = false,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = owner,
            });
            await db.SaveChangesAsync();
        }

        await using var read = await f.CreateDbContextAsync();
        var visible = await read.Organizations.Where(PersonalOrganizations.Discoverable).ToListAsync();

        Assert.Equal("Listed group", Assert.Single(visible).Name);
    }

    // ── and an ordinary group is untouched ───────────────────────────────────

    /// <summary>
    /// The flag defaults to false, so every organization that existed before it stays a visible
    /// group. This matters concretely right now: App Review signs into a demo account whose group
    /// membership is what they approve the application against, and a filter that swept up
    /// ordinary groups would fail the submission rather than merely look wrong.
    /// </summary>
    [Fact]
    public async Task An_ordinary_group_is_still_discoverable_and_unaffected_by_the_flag()
    {
        var f = CreateFactory();
        var owner = await AddUserAsync(f);
        var orgId = Guid.NewGuid();

        await using (var db = await f.CreateDbContextAsync())
        {
            db.Organizations.Add(new Organization
            {
                Id = orgId, Name = "Paranormal365", UrlName = "paranormal365",
                IsAcceptingClients = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = owner,
            });
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = owner,
                Role = OrganizationMemberRole.Member, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = owner,
            });
            await db.SaveChangesAsync();
        }

        await using var read = await f.CreateDbContextAsync();
        var org = await read.Organizations.SingleAsync();

        Assert.False(org.IsPersonal);                       // the default, never opted into
        Assert.Single(await read.Organizations.Where(PersonalOrganizations.Discoverable).ToListAsync());
        Assert.Single(await read.OrganizationUserMemberships.Where(m => m.IsActive).ToListAsync());
    }

    /// <summary>
    /// Creating a personal organization must not disturb anybody else's group — the filter hides
    /// one row, it does not change what a group is.
    /// </summary>
    [Fact]
    public async Task Creating_a_personal_organization_leaves_existing_groups_listed()
    {
        var f = CreateFactory();
        var owner = await AddUserAsync(f);
        await using (var db = await f.CreateDbContextAsync())
        {
            db.Organizations.Add(new Organization
            {
                Id = Guid.NewGuid(), Name = "Paranormal365", UrlName = "paranormal365",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = owner,
            });
            await db.SaveChangesAsync();
        }

        await StartAsync(f, await AddUserAsync(f, display: "Solo Person", handle: "solo"));

        await using var read = await f.CreateDbContextAsync();
        var visible = await read.Organizations.Where(PersonalOrganizations.Discoverable).ToListAsync();
        Assert.Equal("Paranormal365", Assert.Single(visible).Name);
    }
}
