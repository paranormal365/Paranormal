using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers;
using Ben.Data.WebApi.Services;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using System.Security.Claims;
using Xunit;

using DataAction = Ben.Data.Common.Enums.OrganizationSecurityAction;
using DataTable  = Ben.Data.Common.Enums.OrganizationSecurityTable;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Item 161: the action-needed endpoint counts only what the caller can act on. Membership
/// rows decide which groups are consulted at all; each bucket is counted only behind the same
/// read gate as the tab it links to; groups with nothing waiting are omitted entirely.
/// </summary>
public sealed class ActionNeededEndpointTests
{
    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var opts = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PooledDbContextFactory<BenDataContext>(opts);
    }

    /// <summary>
    /// The caller is a member of "Mine" (2 waiting client requests, 1 pending application,
    /// plus decided rows of both kinds that must not count). "Other" has waiting work too,
    /// but the caller is no member of it and must never hear about it.
    /// </summary>
    private static async Task<(Guid mineId, Guid userId)> SeedAsync(IDbContextFactory<BenDataContext> factory)
    {
        var mineId  = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var userId  = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();
        db.AppUsers.Add(new AppUser { Id = userId, UserName = userId.ToString(), Email = $"{userId}@test.com" });
        db.Organizations.Add(new Organization
        {
            Id = mineId, Name = "Mine", UrlName = $"mine-{mineId:N}",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        db.Organizations.Add(new Organization
        {
            Id = otherId, Name = "Other", UrlName = $"other-{otherId:N}",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = mineId, AppUserId = userId,
            Role = OrganizationMemberRole.Member, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });

        foreach (var orgId in new[] { mineId, otherId })
        {
            // Pending + Viewed count as waiting; Accepted must not.
            foreach (var status in new[]
                     { ClientOrgRequestStatus.Pending, ClientOrgRequestStatus.Viewed, ClientOrgRequestStatus.Accepted })
                db.ClientRequestOrganizations.Add(new ClientRequestOrganization
                {
                    Id = Guid.NewGuid(), ClientRequestId = Guid.NewGuid(), OrganizationId = orgId,
                    Status = status, DateApplied = DateTime.UtcNow,
                    DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
                });

            foreach (var status in new[]
                     { OrganizationMembershipRequestStatus.Pending, OrganizationMembershipRequestStatus.Accepted })
                db.OrganizationMembershipRequests.Add(new OrganizationMembershipRequest
                {
                    Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = Guid.NewGuid(),
                    Status = status, DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
                });
        }
        await db.SaveChangesAsync();
        return (mineId, userId);
    }

    private static OrganizationMembershipController Build(
        IDbContextFactory<BenDataContext> factory, Guid userId,
        bool canOpenRequests, bool canOpenApplications, bool superAdmin = false)
    {
        var security = new Mock<IOrganizationSecurityService>();
        security.Setup(s => s.HasAccessAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), DataTable.Case, DataAction.Read, It.IsAny<CancellationToken>()))
            .ReturnsAsync(canOpenRequests);
        security.Setup(s => s.HasAccessAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), DataTable.MembershipRequests, DataAction.Read, It.IsAny<CancellationToken>()))
            .ReturnsAsync(canOpenApplications);

        var ctrl = new OrganizationMembershipController(security.Object, new SiteSettingsService(factory));

        List<Claim> claims = [new(ClaimTypes.NameIdentifier, userId.ToString())];
        if (superAdmin) claims.Add(new Claim(ClaimTypes.Role, Ben.Data.Common.Constants.RoleNames.SuperAdmin));

        var services = new ServiceCollection();
        services.AddSingleton(factory);
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    claims, "Bearer", ClaimTypes.NameIdentifier, ClaimTypes.Role)),
                RequestServices = services.BuildServiceProvider(),
            },
        };
        return ctrl;
    }

    private static IReadOnlyList<OrganizationMembershipController.OrgActionNeededResponse> Body(
        ActionResult<IEnumerable<OrganizationMembershipController.OrgActionNeededResponse>> result)
        => [.. Assert.IsAssignableFrom<IEnumerable<OrganizationMembershipController.OrgActionNeededResponse>>(
               Assert.IsType<OkObjectResult>(result.Result).Value)];

    [Fact]
    public async Task Both_gates_open_counts_the_waiting_work_and_only_in_the_callers_groups()
    {
        var factory = CreateFactory();
        var (mineId, userId) = await SeedAsync(factory);

        var rows = Body(await Build(factory, userId, canOpenRequests: true, canOpenApplications: true)
            .GetActionNeeded(CancellationToken.None));

        var row = Assert.Single(rows);   // "Other" has identical waiting work and must not appear
        Assert.Equal(mineId, row.OrganizationId);
        Assert.Equal("Mine", row.OrganizationName);
        Assert.Equal(2, row.PendingClientRequests);     // Pending + Viewed; Accepted excluded
        Assert.Equal(1, row.PendingMembershipRequests); // Pending; Accepted excluded
    }

    [Fact]
    public async Task No_gates_means_no_banner_however_much_is_waiting()
    {
        var factory = CreateFactory();
        var (_, userId) = await SeedAsync(factory);

        var rows = Body(await Build(factory, userId, canOpenRequests: false, canOpenApplications: false)
            .GetActionNeeded(CancellationToken.None));

        Assert.Empty(rows);
    }

    [Fact]
    public async Task The_two_gates_are_independent()
    {
        var factory = CreateFactory();
        var (mineId, userId) = await SeedAsync(factory);

        var rows = Body(await Build(factory, userId, canOpenRequests: false, canOpenApplications: true)
            .GetActionNeeded(CancellationToken.None));

        var row = Assert.Single(rows);
        Assert.Equal(mineId, row.OrganizationId);
        Assert.Equal(0, row.PendingClientRequests);
        Assert.Equal(1, row.PendingMembershipRequests);
    }

    [Fact]
    public async Task A_superadmin_hears_about_their_own_groups_not_everyones()
    {
        var factory = CreateFactory();
        var (mineId, userId) = await SeedAsync(factory);

        var rows = Body(await Build(factory, userId,
                canOpenRequests: false, canOpenApplications: false, superAdmin: true)
            .GetActionNeeded(CancellationToken.None));

        // Claims open the gates for HIS groups, but "Other" still never appears — membership
        // rows decide the scope, which is also what keeps impersonation faithful.
        var row = Assert.Single(rows);
        Assert.Equal(mineId, row.OrganizationId);
        Assert.Equal(2, row.PendingClientRequests);
    }

    [Fact]
    public async Task Nothing_waiting_means_an_empty_list()
    {
        var factory = CreateFactory();
        var userId = Guid.NewGuid();
        var orgId  = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.AppUsers.Add(new AppUser { Id = userId, UserName = userId.ToString(), Email = $"{userId}@test.com" });
            db.Organizations.Add(new Organization
            {
                Id = orgId, Name = "Quiet", UrlName = $"quiet-{orgId:N}",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
            });
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = userId,
                Role = OrganizationMemberRole.Member, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
            });
            await db.SaveChangesAsync();
        }

        var rows = Body(await Build(factory, userId, canOpenRequests: true, canOpenApplications: true)
            .GetActionNeeded(CancellationToken.None));

        Assert.Empty(rows);
    }
}
