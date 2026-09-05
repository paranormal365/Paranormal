using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Security.Claims;
using Xunit;
using static Ben.Data.WebApi.Controllers.OrganizationSecurityController;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Every door that adds somebody to a group asks the same question about the plan.
/// </summary>
/// <remarks>
/// <para>Two of the three already did — opening applications, and accepting one. The security
/// controller's membership upsert did not, so whether the rule applied depended on which door
/// somebody walked through. No screen uses that route, which is exactly why it went unnoticed
/// until the 2026-09-04 route sweep.</para>
///
/// <para>What must keep working is as important as what is refused: the rule never removes
/// anybody, so changing an existing member's role, or deactivating one, is not "adding somebody
/// new" and is allowed on any plan.</para>
/// </remarks>
public sealed class MembershipDoorsAgreeTests
{
    private static IDbContextFactory<BenDataContext> CreateFactory()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed record World(
        IDbContextFactory<BenDataContext> F, Guid OrgId, Guid OwnerId, Guid NewcomerId);

    /// <summary>A group of one, with no subscription — the free state.</summary>
    private static async Task<World> SeedAsync(bool paid = false)
    {
        var f = CreateFactory();
        Guid orgId = Guid.NewGuid(), ownerId = Guid.NewGuid(), newcomerId = Guid.NewGuid();

        await using var db = await f.CreateDbContextAsync();
        db.Users.Add(new AppUser { Id = ownerId, UserName = "o@t", DateCreated = DateTime.UtcNow });
        db.Users.Add(new AppUser { Id = newcomerId, UserName = "n@t", DateCreated = DateTime.UtcNow });
        db.Organizations.Add(new Organization
        {
            Id = orgId, Name = "G", UrlName = $"g-{orgId:N}",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
        });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = ownerId,
            Role = OrganizationMemberRole.Owner, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
        });
        if (paid)
            db.OrganizationSubscriptions.Add(new OrganizationSubscription
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, Status = SubscriptionStatus.Active,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
        await db.SaveChangesAsync();
        return new World(f, orgId, ownerId, newcomerId);
    }

    private static OrganizationSecurityController Build(World w)
    {
        var ctrl = new OrganizationSecurityController(
            new Ben.Service.RepositoryService.Services.OrganizationSecurityService(w.F), w.F)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, w.OwnerId.ToString())], "Bearer")),
                },
            },
        };
        return ctrl;
    }

    private static Task<ActionResult<OrganizationUserMembershipResponse>> UpsertAsync(
        World w, Guid targetUserId, OrganizationMemberRole role, bool isActive)
        => Build(w).UpsertMembership(w.OrgId, targetUserId,
            new UpsertOrganizationMembershipRequest { Role = role, IsActive = isActive }, default);

    [Fact]
    public async Task Adding_somebody_new_to_a_free_group_asks_for_a_plan()
    {
        var w = await SeedAsync();

        var result = await UpsertAsync(w, w.NewcomerId, OrganizationMemberRole.Member, isActive: true);

        var refusal = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status402PaymentRequired, refusal.StatusCode);

        await using var db = await w.F.CreateDbContextAsync();
        Assert.False(await db.OrganizationUserMemberships
            .AnyAsync(m => m.OrganizationId == w.OrgId && m.AppUserId == w.NewcomerId));
    }

    [Fact]
    public async Task Adding_somebody_new_to_a_paid_group_goes_through()
    {
        var w = await SeedAsync(paid: true);

        var result = await UpsertAsync(w, w.NewcomerId, OrganizationMemberRole.Member, isActive: true);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    /// <summary>
    /// The rule refuses an addition and touches nothing that exists. A group whose plan lapsed
    /// must still be able to manage the people it already has.
    /// </summary>
    [Fact]
    public async Task Changing_an_existing_members_role_is_not_adding_somebody()
    {
        var w = await SeedAsync();
        await using (var db = await w.F.CreateDbContextAsync())
        {
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = w.OrgId, AppUserId = w.NewcomerId,
                Role = OrganizationMemberRole.Member, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.OwnerId,
            });
            await db.SaveChangesAsync();
        }

        var result = await UpsertAsync(w, w.NewcomerId, OrganizationMemberRole.Manager, isActive: true);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task Deactivating_a_membership_is_never_gated()
    {
        var w = await SeedAsync();
        await using (var db = await w.F.CreateDbContextAsync())
        {
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = w.OrgId, AppUserId = w.NewcomerId,
                Role = OrganizationMemberRole.Member, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = w.OwnerId,
            });
            await db.SaveChangesAsync();
        }

        var result = await UpsertAsync(w, w.NewcomerId, OrganizationMemberRole.Member, isActive: false);

        Assert.IsType<OkObjectResult>(result.Result);
    }
}
