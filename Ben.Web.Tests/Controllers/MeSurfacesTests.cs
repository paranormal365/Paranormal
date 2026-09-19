using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Which parts of the app apply to a person (Ben, 2026-08-31).
/// </summary>
/// <remarks>
/// <para>The case that drives the whole thing is the first one: somebody investigating alone
/// should not be carrying a My Cases tab that can never hold anything. An empty screen is not
/// neutral — it reads as a broken app, or as a feature the person is failing to find.</para>
///
/// <para>The second-most-important case is the client, because it is the one a membership-shaped
/// answer gets wrong: somebody who asked a group to investigate their house belongs to no group
/// at all and must still see their case.</para>
/// </remarks>
public sealed class MeSurfacesTests
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
            DisplayName = "Solo", DateCreated = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<MeSurfaces> AskAsync(
        IDbContextFactory<BenDataContext> f, Guid userId, bool superAdmin = false)
    {
        Claim[] claims = superAdmin
            ? [new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
               new Claim(ClaimTypes.Role, Ben.Data.Common.Constants.RoleNames.SuperAdmin)]
            : [new Claim(ClaimTypes.NameIdentifier, userId.ToString())];

        var ctrl = new MeSurfacesController(f)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        claims, "Bearer", ClaimTypes.NameIdentifier, ClaimTypes.Role)),
                },
            },
        };

        var result = (await ctrl.Get(default)).Result;
        return Assert.IsType<MeSurfaces>(Assert.IsType<OkObjectResult>(result).Value);
    }

    /// <summary>The case the feature exists for.</summary>
    [Fact]
    public async Task Somebody_investigating_alone_is_offered_neither_cases_nor_investigations()
    {
        var f = CreateFactory();

        var surfaces = await AskAsync(f, await AddUserAsync(f));

        Assert.False(surfaces.HasGroups);
        Assert.False(surfaces.HasCases);
        Assert.False(surfaces.HasInvestigations);
        Assert.False(surfaces.AdministersAGroup);
        Assert.False(surfaces.AttendsPublicEvents);
    }

    /// <summary>
    /// A client belongs to no group and must still reach their own case — the answer a
    /// membership-only rule gets wrong.
    /// </summary>
    [Fact]
    public async Task A_client_with_no_group_still_has_cases()
    {
        var f = CreateFactory();
        var user = await AddUserAsync(f);
        var orgId = Guid.NewGuid();

        await using (var db = await f.CreateDbContextAsync())
        {
            db.Organizations.Add(new Organization
            {
                Id = orgId, Name = "A Group", UrlName = $"g-{orgId:N}",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = user,
            });
            var caseId = Guid.NewGuid();
            db.Cases.Add(new Case
            {
                Id = caseId, OrganizationId = orgId, Title = "The house on the hill",
                Status = CaseStatus.Active,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = user,
            });
            db.CaseClientAccesses.Add(new CaseClientAccess
            {
                Id = Guid.NewGuid(), CaseId = caseId, AppUserId = user,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = user,
            });
            await db.SaveChangesAsync();
        }

        var surfaces = await AskAsync(f, user);

        Assert.True(surfaces.HasCases);
        Assert.False(surfaces.HasGroups);      // still not a member of anything
    }

    [Fact]
    public async Task A_ghost_walk_guest_is_offered_events()
    {
        var f = CreateFactory();
        var user = await AddUserAsync(f);
        var orgId = Guid.NewGuid();
        var eventId = Guid.NewGuid();

        await using (var db = await f.CreateDbContextAsync())
        {
            db.Organizations.Add(new Organization
            {
                Id = orgId, Name = "Nightfall Walks", UrlName = $"nw-{orgId:N}",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = user,
            });
            db.OrgCalendarEvents.Add(new OrgCalendarEvent
            {
                Id = eventId, OrganizationId = orgId, Title = "Old Town Walk",
                IsPublic = true, StartDateTime = DateTime.UtcNow.AddDays(-1),
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = user,
            });
            db.EventAttendanceInvites.Add(new EventAttendanceInvite
            {
                Id = Guid.NewGuid(), OrgCalendarEventId = eventId,
                Email = "guest@t.com", ConfirmedByAppUserId = user,
                DateConfirmed = DateTime.UtcNow,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = user,
            });
            await db.SaveChangesAsync();
        }

        var surfaces = await AskAsync(f, user);

        // Past events count: photographs outlive the walk, and the night after is exactly when
        // somebody opens the app.
        Assert.True(surfaces.AttendsPublicEvents);
        Assert.False(surfaces.HasGroups);
    }

    [Fact]
    public async Task An_owner_administers_a_group()
    {
        var f = CreateFactory();
        var user = await AddUserAsync(f);
        var orgId = Guid.NewGuid();

        await using (var db = await f.CreateDbContextAsync())
        {
            db.Organizations.Add(new Organization
            {
                Id = orgId, Name = "A Group", UrlName = $"g-{orgId:N}",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = user,
            });
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = user,
                Role = OrganizationMemberRole.Owner, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = user,
            });
            await db.SaveChangesAsync();
        }

        var surfaces = await AskAsync(f, user);

        Assert.True(surfaces.HasGroups);
        Assert.True(surfaces.AdministersAGroup);
    }

    /// <summary>
    /// An ordinary member is not an administrator. Otherwise every member would be offered the
    /// settings-shaped screens and refused at each one.
    /// </summary>
    [Fact]
    public async Task An_ordinary_member_does_not_administer()
    {
        var f = CreateFactory();
        var user = await AddUserAsync(f);
        var orgId = Guid.NewGuid();

        await using (var db = await f.CreateDbContextAsync())
        {
            db.Organizations.Add(new Organization
            {
                Id = orgId, Name = "A Group", UrlName = $"g-{orgId:N}",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = user,
            });
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = user,
                Role = OrganizationMemberRole.Member, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = user,
            });
            await db.SaveChangesAsync();
        }

        var surfaces = await AskAsync(f, user);

        Assert.True(surfaces.HasGroups);
        Assert.False(surfaces.AdministersAGroup);
    }

    /// <summary>
    /// An app administrator is shown everything: their job is the parts of the product other
    /// people cannot see, and hiding a section because their own account is empty would hide
    /// exactly what they came to look at.
    /// </summary>
    [Fact]
    public async Task A_superadmin_is_offered_everything_even_with_an_empty_account()
    {
        var f = CreateFactory();

        var surfaces = await AskAsync(f, await AddUserAsync(f), superAdmin: true);

        Assert.True(surfaces.HasCases);
        Assert.True(surfaces.HasInvestigations);
        Assert.True(surfaces.AdministersAGroup);
    }
}
