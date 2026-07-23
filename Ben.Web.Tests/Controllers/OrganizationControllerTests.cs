using AutoMapper;
using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Admin;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Service.Models.Admin;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Unit tests for OrganizationController — permission-aware CRUD endpoints.
/// Covers GetAllWithPermissions, GetByIdWithPermissions, Update, Delete, and Create.
/// </summary>
public class OrganizationControllerTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var options = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PooledDbContextFactory<BenDataContext>(options);
    }

    private static Mock<IMapper> CreateMapperMock()
    {
        var mock = new Mock<IMapper>();
        mock.Setup(m => m.Map<OrganizationAdminRecord>(It.IsAny<object>()))
            .Returns<object>(o => o is Organization org
                ? new OrganizationAdminRecord
                {
                    Id      = org.Id,
                    Name    = org.Name,
                    UrlName = org.UrlName,
                    DateCreated = org.DateCreated,
                    CreatedByAppUserId = org.CreatedByAppUserId
                }
                : new OrganizationAdminRecord { Name = string.Empty, UrlName = string.Empty });
        mock.Setup(m => m.Map<OrganizationRecord>(It.IsAny<object>()))
            .Returns(new OrganizationRecord { Name = string.Empty, UrlName = string.Empty });
        return mock;
    }

    private static OrganizationController BuildController(
        IDbContextFactory<BenDataContext> factory,
        ClaimsPrincipal? principal = null,
        Mock<IOrganizationSecurityService>? securityMock = null,
        Mock<IMapper>? mapperMock = null)
    {
        securityMock ??= new Mock<IOrganizationSecurityService>();
        mapperMock   ??= CreateMapperMock();

        var controller = new OrganizationController(factory, mapperMock.Object, securityMock.Object, new Mock<IAuditLogService>().Object);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = principal ?? AnonymousPrincipal() }
        };
        return controller;
    }

    /// <summary>An unauthenticated principal with no claims.</summary>
    private static ClaimsPrincipal AnonymousPrincipal() =>
        new(new ClaimsIdentity());

    /// <summary>
    /// A principal with a NameIdentifier (local-auth) claim, optionally in the SuperAdmin role.
    /// </summary>
    private static ClaimsPrincipal UserPrincipal(Guid userId, bool isSuperAdmin = false)
    {
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString())
        };
        if (isSuperAdmin)
            claims.Add(new Claim(ClaimTypes.Role, RoleNames.SuperAdmin));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
    }

    private static async Task<Organization> SeedOrgAsync(
        IDbContextFactory<BenDataContext> factory,
        string name = "Test Org",
        string urlName = "test-org")
    {
        await using var db = await factory.CreateDbContextAsync();
        var org = new Organization
        {
            Id                 = Guid.NewGuid(),
            Name               = name,
            UrlName            = urlName,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = Guid.NewGuid()
        };
        db.Organizations.Add(org);
        await db.SaveChangesAsync();
        return org;
    }

    // ── GetAllWithPermissions ─────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_WhenNoUserId_ReturnsUnauthorized()
    {
        var factory    = CreateFactory();
        var controller = BuildController(factory, AnonymousPrincipal());

        var result = await controller.GetAllWithPermissions(default);

        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public async Task GetAll_AsSuperAdmin_ReturnsOrgsWithCanEditAndCanDeleteTrue()
    {
        var factory  = CreateFactory();
        var userId   = Guid.NewGuid();
        var org      = await SeedOrgAsync(factory, "Acme", "acme");

        var securityMock = new Mock<IOrganizationSecurityService>();
        securityMock
            .Setup(s => s.GetOrganizationsForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([org]);

        var controller = BuildController(factory, UserPrincipal(userId, isSuperAdmin: true), securityMock);

        var result = await controller.GetAllWithPermissions(default);

        var ok   = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsAssignableFrom<IEnumerable<OrganizationListItemResponse>>(ok.Value);
        var item = Assert.Single(list);
        Assert.Equal(org.Id, item.Id);
        Assert.True(item.CanEdit);
        Assert.True(item.CanDelete);
        // SuperAdmin bypasses per-org permission checks
        securityMock.Verify(
            s => s.HasAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<OrganizationSecurityTable>(), It.IsAny<OrganizationSecurityAction>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetAll_AsMember_WithBothGrantsTrue_ReturnsCorrectFlags()
    {
        var factory = CreateFactory();
        var userId  = Guid.NewGuid();
        var org     = await SeedOrgAsync(factory);

        var securityMock = new Mock<IOrganizationSecurityService>();
        securityMock
            .Setup(s => s.GetOrganizationsForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([org]);
        securityMock
            .Setup(s => s.HasAccessAsync(userId, org.Id, OrganizationSecurityTable.Organization,
                OrganizationSecurityAction.Update, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        securityMock
            .Setup(s => s.HasAccessAsync(userId, org.Id, OrganizationSecurityTable.Organization,
                OrganizationSecurityAction.Delete, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var controller = BuildController(factory, UserPrincipal(userId), securityMock);

        var result = await controller.GetAllWithPermissions(default);

        var ok   = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsAssignableFrom<IEnumerable<OrganizationListItemResponse>>(ok.Value);
        var item = Assert.Single(list);
        Assert.True(item.CanEdit);
        Assert.True(item.CanDelete);
    }

    [Fact]
    public async Task GetAll_AsMember_WithEditGrantOnly_ReturnsCanEditTrueCanDeleteFalse()
    {
        var factory = CreateFactory();
        var userId  = Guid.NewGuid();
        var org     = await SeedOrgAsync(factory);

        var securityMock = new Mock<IOrganizationSecurityService>();
        securityMock
            .Setup(s => s.GetOrganizationsForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([org]);
        securityMock
            .Setup(s => s.HasAccessAsync(userId, org.Id, OrganizationSecurityTable.Organization,
                OrganizationSecurityAction.Update, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        securityMock
            .Setup(s => s.HasAccessAsync(userId, org.Id, OrganizationSecurityTable.Organization,
                OrganizationSecurityAction.Delete, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var controller = BuildController(factory, UserPrincipal(userId), securityMock);

        var result = await controller.GetAllWithPermissions(default);

        var ok   = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsAssignableFrom<IEnumerable<OrganizationListItemResponse>>(ok.Value);
        var item = Assert.Single(list);
        Assert.True(item.CanEdit);
        Assert.False(item.CanDelete);
    }

    [Fact]
    public async Task GetAll_AsMember_WithNoGrants_ReturnsBothFlagsFalse()
    {
        var factory = CreateFactory();
        var userId  = Guid.NewGuid();
        var org     = await SeedOrgAsync(factory);

        var securityMock = new Mock<IOrganizationSecurityService>();
        securityMock
            .Setup(s => s.GetOrganizationsForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([org]);
        securityMock
            .Setup(s => s.HasAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<OrganizationSecurityTable>(), It.IsAny<OrganizationSecurityAction>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var controller = BuildController(factory, UserPrincipal(userId), securityMock);

        var result = await controller.GetAllWithPermissions(default);

        var ok   = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsAssignableFrom<IEnumerable<OrganizationListItemResponse>>(ok.Value);
        var item = Assert.Single(list);
        Assert.False(item.CanEdit);
        Assert.False(item.CanDelete);
    }

    // ── GetByIdWithPermissions ────────────────────────────────────────────────

    [Fact]
    public async Task GetById_WhenNoUserId_ReturnsUnauthorized()
    {
        var factory    = CreateFactory();
        var controller = BuildController(factory, AnonymousPrincipal());

        var result = await controller.GetByIdWithPermissions(Guid.NewGuid(), default);

        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public async Task GetById_AsSuperAdmin_ReturnsOrg()
    {
        var factory    = CreateFactory();
        var userId     = Guid.NewGuid();
        var org        = await SeedOrgAsync(factory, "Acme", "acme");
        var controller = BuildController(factory, UserPrincipal(userId, isSuperAdmin: true));

        var result = await controller.GetByIdWithPermissions(org.Id, default);

        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var record = Assert.IsType<OrganizationAdminRecord>(ok.Value);
        Assert.Equal(org.Id, record.Id);
        Assert.Equal("Acme", record.Name);
    }

    [Fact]
    public async Task GetById_AsMember_WithReadAccess_ReturnsOrg()
    {
        var factory  = CreateFactory();
        var userId   = Guid.NewGuid();
        var org      = await SeedOrgAsync(factory);

        var securityMock = new Mock<IOrganizationSecurityService>();
        securityMock
            .Setup(s => s.HasAccessAsync(userId, org.Id, OrganizationSecurityTable.Organization,
                OrganizationSecurityAction.Read, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var controller = BuildController(factory, UserPrincipal(userId), securityMock);

        var result = await controller.GetByIdWithPermissions(org.Id, default);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetById_AsMember_WithoutReadAccess_ReturnsForbid()
    {
        var factory  = CreateFactory();
        var userId   = Guid.NewGuid();
        var org      = await SeedOrgAsync(factory);

        var securityMock = new Mock<IOrganizationSecurityService>();
        securityMock
            .Setup(s => s.HasAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<OrganizationSecurityTable>(), It.IsAny<OrganizationSecurityAction>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var controller = BuildController(factory, UserPrincipal(userId), securityMock);

        var result = await controller.GetByIdWithPermissions(org.Id, default);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task GetById_WhenOrgNotFound_ReturnsNotFound()
    {
        var factory    = CreateFactory();
        var userId     = Guid.NewGuid();
        var controller = BuildController(factory, UserPrincipal(userId, isSuperAdmin: true));

        var result = await controller.GetByIdWithPermissions(Guid.NewGuid(), default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    // ── Update ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_WhenNoUserId_ReturnsUnauthorized()
    {
        var factory    = CreateFactory();
        var controller = BuildController(factory, AnonymousPrincipal());

        var result = await controller.Update(Guid.NewGuid(), new AdminUpdateOrganizationRequest("New", "new"), default);

        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public async Task Update_AsSuperAdmin_PersistsChangesAndReturnsOk()
    {
        var factory    = CreateFactory();
        var userId     = Guid.NewGuid();
        var org        = await SeedOrgAsync(factory, "Old Name", "old-name");
        var controller = BuildController(factory, UserPrincipal(userId, isSuperAdmin: true));

        var result = await controller.Update(org.Id, new AdminUpdateOrganizationRequest("New Name", "New-URL"), default);

        Assert.IsType<OkObjectResult>(result.Result);

        await using var db = await factory.CreateDbContextAsync();
        var updated = await db.Organizations.FindAsync(org.Id);
        Assert.Equal("New Name", updated!.Name);
        Assert.Equal("new-url", updated.UrlName); // normalised to lowercase
        Assert.Equal(userId, updated.UpdatedByAppUserId);
    }

    [Fact]
    public async Task Update_AsMember_WithUpdateAccess_PersistsChanges()
    {
        var factory  = CreateFactory();
        var userId   = Guid.NewGuid();
        var org      = await SeedOrgAsync(factory, "Original", "original");

        var securityMock = new Mock<IOrganizationSecurityService>();
        securityMock
            .Setup(s => s.HasAccessAsync(userId, org.Id, OrganizationSecurityTable.Organization,
                OrganizationSecurityAction.Update, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var controller = BuildController(factory, UserPrincipal(userId), securityMock);

        var result = await controller.Update(org.Id, new AdminUpdateOrganizationRequest("Changed", "changed"), default);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task Update_AsMember_WithoutUpdateAccess_ReturnsForbid()
    {
        var factory  = CreateFactory();
        var userId   = Guid.NewGuid();
        var org      = await SeedOrgAsync(factory);

        var securityMock = new Mock<IOrganizationSecurityService>();
        securityMock
            .Setup(s => s.HasAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<OrganizationSecurityTable>(), It.IsAny<OrganizationSecurityAction>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var controller = BuildController(factory, UserPrincipal(userId), securityMock);

        var result = await controller.Update(org.Id, new AdminUpdateOrganizationRequest("X", "x"), default);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task Update_WithBlankName_ReturnsBadRequest()
    {
        var factory    = CreateFactory();
        var userId     = Guid.NewGuid();
        var org        = await SeedOrgAsync(factory);
        var controller = BuildController(factory, UserPrincipal(userId, isSuperAdmin: true));

        var result = await controller.Update(org.Id, new AdminUpdateOrganizationRequest("   ", "url"), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Update_WithBlankUrlName_ReturnsBadRequest()
    {
        var factory    = CreateFactory();
        var userId     = Guid.NewGuid();
        var org        = await SeedOrgAsync(factory);
        var controller = BuildController(factory, UserPrincipal(userId, isSuperAdmin: true));

        var result = await controller.Update(org.Id, new AdminUpdateOrganizationRequest("Name", "  "), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Update_WhenOrgNotFound_ReturnsNotFound()
    {
        var factory    = CreateFactory();
        var userId     = Guid.NewGuid();
        var controller = BuildController(factory, UserPrincipal(userId, isSuperAdmin: true));

        var result = await controller.Update(Guid.NewGuid(), new AdminUpdateOrganizationRequest("Name", "name"), default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_WhenNoUserId_ReturnsUnauthorized()
    {
        var factory    = CreateFactory();
        var controller = BuildController(factory, AnonymousPrincipal());

        var result = await controller.Delete(Guid.NewGuid(), default);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task Delete_AsSuperAdmin_RemovesOrgAndReturnsNoContent()
    {
        var factory    = CreateFactory();
        var userId     = Guid.NewGuid();
        var org        = await SeedOrgAsync(factory);
        var controller = BuildController(factory, UserPrincipal(userId, isSuperAdmin: true));

        var result = await controller.Delete(org.Id, default);

        Assert.IsType<NoContentResult>(result);

        await using var db = await factory.CreateDbContextAsync();
        Assert.Null(await db.Organizations.FindAsync(org.Id));
    }

    [Fact]
    public async Task Delete_AsMember_WithDeleteAccess_RemovesOrg()
    {
        var factory  = CreateFactory();
        var userId   = Guid.NewGuid();
        var org      = await SeedOrgAsync(factory);

        var securityMock = new Mock<IOrganizationSecurityService>();
        securityMock
            .Setup(s => s.HasAccessAsync(userId, org.Id, OrganizationSecurityTable.Organization,
                OrganizationSecurityAction.Delete, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var controller = BuildController(factory, UserPrincipal(userId), securityMock);

        var result = await controller.Delete(org.Id, default);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task Delete_AsMember_WithoutDeleteAccess_ReturnsForbid()
    {
        var factory  = CreateFactory();
        var userId   = Guid.NewGuid();
        var org      = await SeedOrgAsync(factory);

        var securityMock = new Mock<IOrganizationSecurityService>();
        securityMock
            .Setup(s => s.HasAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<OrganizationSecurityTable>(), It.IsAny<OrganizationSecurityAction>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var controller = BuildController(factory, UserPrincipal(userId), securityMock);

        var result = await controller.Delete(org.Id, default);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task Delete_WhenOrgNotFound_ReturnsNotFound()
    {
        var factory    = CreateFactory();
        var userId     = Guid.NewGuid();
        var controller = BuildController(factory, UserPrincipal(userId, isSuperAdmin: true));

        var result = await controller.Delete(Guid.NewGuid(), default);

        Assert.IsType<NotFoundResult>(result);
    }

    // ── Create ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_WhenNoUserId_ReturnsUnauthorized()
    {
        var factory    = CreateFactory();
        var controller = BuildController(factory, AnonymousPrincipal());

        var result = await controller.Create(new AdminCreateOrganizationRequest("New Org", "new-org"), default);

        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public async Task Create_AsSuperAdmin_CreatesOrgAndReturnsCreated()
    {
        var factory    = CreateFactory();
        var userId     = Guid.NewGuid();
        var controller = BuildController(factory, UserPrincipal(userId, isSuperAdmin: true));

        var result = await controller.Create(new AdminCreateOrganizationRequest("New Org", "new-org"), default);

        Assert.IsType<CreatedAtActionResult>(result.Result);

        await using var db = await factory.CreateDbContextAsync();
        var created = await db.Organizations.FirstOrDefaultAsync(o => o.UrlName == "new-org");
        Assert.NotNull(created);
        Assert.Equal("New Org", created.Name);
        Assert.Equal(userId, created.CreatedByAppUserId);
    }

    [Fact]
    public async Task Create_AsNonSuperAdmin_ReturnsForbid()
    {
        var factory    = CreateFactory();
        var userId     = Guid.NewGuid();
        var controller = BuildController(factory, UserPrincipal(userId, isSuperAdmin: false));

        var result = await controller.Create(new AdminCreateOrganizationRequest("New Org", "new-org"), default);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task Create_WithDuplicateUrlName_ReturnsBadRequest()
    {
        var factory    = CreateFactory();
        var userId     = Guid.NewGuid();
        await SeedOrgAsync(factory, "Existing", "existing-org");
        var controller = BuildController(factory, UserPrincipal(userId, isSuperAdmin: true));

        var result = await controller.Create(new AdminCreateOrganizationRequest("Another", "existing-org"), default);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("existing-org", bad.Value?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_WithBlankName_ReturnsBadRequest()
    {
        var factory    = CreateFactory();
        var userId     = Guid.NewGuid();
        var controller = BuildController(factory, UserPrincipal(userId, isSuperAdmin: true));

        var result = await controller.Create(new AdminCreateOrganizationRequest("", "some-url"), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Create_UrlNameIsNormalisedToLowercase()
    {
        var factory    = CreateFactory();
        var userId     = Guid.NewGuid();
        var controller = BuildController(factory, UserPrincipal(userId, isSuperAdmin: true));

        await controller.Create(new AdminCreateOrganizationRequest("MyOrg", "MyOrg-URL"), default);

        await using var db = await factory.CreateDbContextAsync();
        Assert.True(await db.Organizations.AnyAsync(o => o.UrlName == "myorg-url"));
    }
}
