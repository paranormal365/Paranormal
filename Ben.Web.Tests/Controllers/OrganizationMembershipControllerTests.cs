using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers;
using Ben.Data.WebApi.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
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

    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var options = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PooledDbContextFactory<BenDataContext>(options);
    }

    /// <summary>
    /// Builds the controller. <paramref name="allowSelfRegistration"/> is null for "nobody has
    /// ever touched the setting", which must read as allowed — see the endpoint's own remarks.
    /// </summary>
    private static OrganizationMembershipController Build(
        Mock<IOrganizationSecurityService> svc, Guid userId,
        bool? allowSelfRegistration = null, bool isSuperAdmin = false)
    {
        var factory = CreateFactory();
        if (allowSelfRegistration is { } allow)
        {
            using var db = factory.CreateDbContext();
            db.SiteSettings.Add(new SiteSetting
            {
                Id = Guid.NewGuid(),
                Key = SiteSettingKeys.AllowOrganizationSelfRegistration,
                Value = allow.ToString(),
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
            });
            db.SaveChanges();
        }

        var ctrl = new OrganizationMembershipController(svc.Object, new SiteSettingsService(factory));
        List<Claim> claims = [new Claim(ClaimTypes.NameIdentifier, userId.ToString())];
        if (isSuperAdmin) claims.Add(new Claim(ClaimTypes.Role, Ben.Data.Common.Constants.RoleNames.SuperAdmin));
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    claims, "Bearer", ClaimTypes.NameIdentifier, ClaimTypes.Role))
            }
        };
        return ctrl;
    }

    // ── Included areas: a group's plan is its own business ───────────────────
    //
    // It answers with the plan NAME, and it used to answer for any group to anybody signed in —
    // so a stranger could read which plan any group was on, one id at a time (2026-09-04 sweep).

    /// <summary>
    /// The controller resolves its DbContext from the request's services for this endpoint, so a
    /// test that reaches past the guard has to supply them.
    /// </summary>
    private static OrganizationMembershipController WithRequestServices(
        OrganizationMembershipController ctrl, IDbContextFactory<BenDataContext> factory)
    {
        var services = new ServiceCollection();
        services.AddSingleton(factory);
        ctrl.ControllerContext.HttpContext.RequestServices = services.BuildServiceProvider();
        return ctrl;
    }

    [Fact]
    public async Task GetIncludedAreas_RefusesSomebodyWhoDoesNotBelongToTheGroup()
    {
        var userId = Guid.NewGuid();
        var orgId  = Guid.NewGuid();
        var svc    = ServiceMock();
        svc.Setup(x => x.BelongsToAsync(userId, orgId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await Build(svc, userId).GetIncludedAreas(orgId, default);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task GetIncludedAreas_AnswersAMemberOfTheGroup()
    {
        var userId  = Guid.NewGuid();
        var orgId   = Guid.NewGuid();
        var factory = CreateFactory();
        var svc     = ServiceMock();
        svc.Setup(x => x.BelongsToAsync(userId, orgId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await WithRequestServices(Build(svc, userId), factory).GetIncludedAreas(orgId, default);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    /// <summary>A SuperAdmin runs the site, and every admin screen depends on reading this.</summary>
    [Fact]
    public async Task GetIncludedAreas_AnswersASuperAdminWhoBelongsToNothing()
    {
        var userId  = Guid.NewGuid();
        var orgId   = Guid.NewGuid();
        var factory = CreateFactory();
        var svc     = ServiceMock();
        svc.Setup(x => x.BelongsToAsync(userId, orgId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var ctrl = WithRequestServices(Build(svc, userId, isSuperAdmin: true), factory);
        var result = await ctrl.GetIncludedAreas(orgId, default);

        Assert.IsType<OkObjectResult>(result.Result);
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
    public async Task RegisterOrganization_WhenSelfRegistrationIsOff_IsRefused()
    {
        // The setting was declared, shown as a switch, described as "when off, only a SuperAdmin
        // can create one" — and read by nothing at all until 2026-08-22. An administrator could
        // switch it off and every visitor kept founding groups.
        var userId = Guid.NewGuid();
        var svc    = ServiceMock();
        var ctrl   = Build(svc, userId, allowSelfRegistration: false);

        var result = await ctrl.RegisterOrganization(
            new RegisterOrganizationRequest { Name = "Acme", UrlName = "acme" }, default);

        var refused = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, refused.StatusCode);

        // Refused means not created, not "created and then complained about".
        svc.Verify(x => x.RegisterOrganizationAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Ben.Data.Common.Enums.OrganizationKind>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RegisterOrganization_WhenSelfRegistrationIsOff_SuperAdminMayStillCreate()
    {
        var userId = Guid.NewGuid();
        var svc    = ServiceMock();
        svc.Setup(x => x.RegisterOrganizationAsync(userId, It.IsAny<string>(), It.IsAny<string>(), default))
           .ReturnsAsync(new Organization { Id = Guid.NewGuid(), Name = "Acme", UrlName = "acme" });
        var ctrl = Build(svc, userId, allowSelfRegistration: false, isSuperAdmin: true);

        var result = await ctrl.RegisterOrganization(
            new RegisterOrganizationRequest { Name = "Acme", UrlName = "acme" }, default);

        Assert.IsType<CreatedAtActionResult>(result.Result);
    }

    [Fact]
    public async Task RegisterOrganization_WhenTheSettingWasNeverSet_IsAllowed()
    {
        // Introducing the check must not close a door that has been open since launch.
        var userId = Guid.NewGuid();
        var svc    = ServiceMock();
        svc.Setup(x => x.RegisterOrganizationAsync(userId, It.IsAny<string>(), It.IsAny<string>(), default))
           .ReturnsAsync(new Organization { Id = Guid.NewGuid(), Name = "Acme", UrlName = "acme" });
        var ctrl = Build(svc, userId);

        var result = await ctrl.RegisterOrganization(
            new RegisterOrganizationRequest { Name = "Acme", UrlName = "acme" }, default);

        Assert.IsType<CreatedAtActionResult>(result.Result);
    }

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
