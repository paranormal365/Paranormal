using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Security.Claims;
using Xunit;
using static Ben.Data.WebApi.Controllers.OrganizationMembershipController;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Tests for <see cref="OrganizationMembershipController"/>:
/// SearchUsers, GetMyOrganizations, RegisterOrganization.
/// Uses a mocked <see cref="IOrganizationSecurityService"/>.
/// </summary>
public class OrganizationMembershipControllerTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Mock<IOrganizationSecurityService> ServiceMock()
        => new Mock<IOrganizationSecurityService>();

    private static OrganizationMembershipController Build(
        Mock<IOrganizationSecurityService> svc, Guid userId)
    {
        var ctrl = new OrganizationMembershipController(svc.Object);
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim(ClaimTypes.NameIdentifier, userId.ToString())
                ], "Bearer"))
            }
        };
        return ctrl;
    }

    // ── SearchUsers ───────────────────────────────────────────────────────────

    [Fact]
    public async Task SearchUsers_ReturnsEmptyList_WhenServiceReturnsNone()
    {
        var userId = Guid.NewGuid();
        var svc    = ServiceMock();
        svc.Setup(x => x.SearchUsersAsync(userId, null, 0, 25, default))
           .ReturnsAsync([]);
        var ctrl   = Build(svc, userId);

        var result = await ctrl.SearchUsers(null, 0, 25);

        var ok   = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsAssignableFrom<IEnumerable<UserSearchResultResponse>>(ok.Value);
        Assert.Empty(list);
    }

    [Fact]
    public async Task SearchUsers_ProjectsUsersToResponse()
    {
        var userId = Guid.NewGuid();
        var svc    = ServiceMock();
        svc.Setup(x => x.SearchUsersAsync(userId, "alice", 0, 10, default))
           .ReturnsAsync([new AppUser
           {
               Id = Guid.NewGuid(), DisplayName = "Alice", UserName = "alice",
               Email = "alice@example.com", DateCreated = DateTime.UtcNow
           }]);
        var ctrl   = Build(svc, userId);

        var result = await ctrl.SearchUsers("alice", 0, 10);

        var ok   = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsAssignableFrom<IEnumerable<UserSearchResultResponse>>(ok.Value).ToList();
        Assert.Single(list);
        Assert.Equal("Alice",            list[0].DisplayName);
        Assert.Equal("alice@example.com", list[0].Email);
    }

    // ── GetMyOrganizations ────────────────────────────────────────────────────

    [Fact]
    public async Task GetMyOrganizations_ReturnsEmpty_WhenUserHasNoMemberships()
    {
        var userId = Guid.NewGuid();
        var svc    = ServiceMock();
        svc.Setup(x => x.GetOrganizationsForUserAsync(userId, default))
           .ReturnsAsync([]);
        var ctrl = Build(svc, userId);

        var result = await ctrl.GetMyOrganizations(default);

        var ok   = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsAssignableFrom<IEnumerable<OrganizationSummaryResponse>>(ok.Value);
        Assert.Empty(list);
    }

    [Fact]
    public async Task GetMyOrganizations_ProjectsOrgsToResponse()
    {
        var userId = Guid.NewGuid();
        var orgId  = Guid.NewGuid();
        var svc    = ServiceMock();
        svc.Setup(x => x.GetOrganizationsForUserAsync(userId, default))
           .ReturnsAsync([new Organization
           {
               Id = orgId, Name = "BenCo", UrlName = "benco",
               DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId
           }]);
        var ctrl = Build(svc, userId);

        var result = await ctrl.GetMyOrganizations(default);

        var ok   = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsAssignableFrom<IEnumerable<OrganizationSummaryResponse>>(ok.Value).ToList();
        Assert.Single(list);
        Assert.Equal(orgId, list[0].OrganizationId);
        Assert.Equal("BenCo", list[0].Name);
        Assert.Equal("benco", list[0].UrlName);
    }

    // ── RegisterOrganization ──────────────────────────────────────────────────

    [Fact]
    public async Task RegisterOrganization_Returns201_WithNewOrgData()
    {
        var userId = Guid.NewGuid();
        var orgId  = Guid.NewGuid();
        var svc    = ServiceMock();
        svc.Setup(x => x.RegisterOrganizationAsync(userId, "Acme", "acme", default))
           .ReturnsAsync(new Organization
           {
               Id = orgId, Name = "Acme", UrlName = "acme",
               DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId
           });
        var ctrl = Build(svc, userId);

        var result = await ctrl.RegisterOrganization(
            new RegisterOrganizationRequest { Name = "Acme", UrlName = "acme" }, default);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var summary = Assert.IsType<OrganizationSummaryResponse>(created.Value);
        Assert.Equal(orgId,  summary.OrganizationId);
        Assert.Equal("Acme", summary.Name);
        Assert.Equal("acme", summary.UrlName);
    }

    [Fact]
    public async Task RegisterOrganization_DelegatesToService_WithCallerUserId()
    {
        var userId = Guid.NewGuid();
        var svc    = ServiceMock();
        svc.Setup(x => x.RegisterOrganizationAsync(userId, It.IsAny<string>(), It.IsAny<string>(), default))
           .ReturnsAsync(new Organization
           {
               Id = Guid.NewGuid(), Name = "X", UrlName = "x",
               DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId
           });
        var ctrl = Build(svc, userId);

        await ctrl.RegisterOrganization(
            new RegisterOrganizationRequest { Name = "X", UrlName = "x" }, default);

        svc.Verify(x => x.RegisterOrganizationAsync(userId, "X", "x", default), Times.Once);
    }
}
