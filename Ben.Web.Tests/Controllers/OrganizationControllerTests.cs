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
    public async Task GetAll_AsSuperAdmin_IncludesMemberCaseInvestigationCounts()
    {
        var factory  = CreateFactory();
        var userId   = Guid.NewGuid();
        var org      = await SeedOrgAsync(factory, "Acme", "acme");
        var otherOrg = await SeedOrgAsync(factory, "Other", "other");

        await using (var db = await factory.CreateDbContextAsync())
        {
            // 2 active + 1 inactive member for org — inactive shouldn't count
            db.OrganizationUserMemberships.AddRange(
                new OrganizationUserMembership { Id = Guid.NewGuid(), OrganizationId = org.Id, AppUserId = Guid.NewGuid(), Role = OrganizationMemberRole.Member, IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId },
                new OrganizationUserMembership { Id = Guid.NewGuid(), OrganizationId = org.Id, AppUserId = Guid.NewGuid(), Role = OrganizationMemberRole.Member, IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId },
                new OrganizationUserMembership { Id = Guid.NewGuid(), OrganizationId = org.Id, AppUserId = Guid.NewGuid(), Role = OrganizationMemberRole.Member, IsActive = false, DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId },
                new OrganizationUserMembership { Id = Guid.NewGuid(), OrganizationId = otherOrg.Id, AppUserId = Guid.NewGuid(), Role = OrganizationMemberRole.Member, IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId });

            var case1 = new Case
            {
                Id = Guid.NewGuid(), OrganizationId = org.Id, Title = "Case 1", CaseYear = 2026, OrgCaseNumber = 1,
                StreetAddress1 = "1 Main St", City = "Nashville", State = "TN", ZipCode = "37201", Country = "US",
                DateCaseOpened = DateTime.UtcNow, DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
            };
            db.Cases.Add(case1);
            db.Cases.Add(new Case
            {
                Id = Guid.NewGuid(), OrganizationId = otherOrg.Id, Title = "Other Case", CaseYear = 2026, OrgCaseNumber = 1,
                StreetAddress1 = "2 Main St", City = "Nashville", State = "TN", ZipCode = "37201", Country = "US",
                DateCaseOpened = DateTime.UtcNow, DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
            });

            db.Investigations.AddRange(
                new Investigation { Id = Guid.NewGuid(), OrganizationId = org.Id, CaseId = case1.Id, Title = "Investigation 1", ScheduledDateTime = DateTime.UtcNow, DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId },
                new Investigation { Id = Guid.NewGuid(), OrganizationId = org.Id, CaseId = case1.Id, Title = "Investigation 2", ScheduledDateTime = DateTime.UtcNow, DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId });

            await db.SaveChangesAsync();
        }

        var securityMock = new Mock<IOrganizationSecurityService>();
        securityMock
            .Setup(s => s.GetOrganizationsForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([org]);

        var controller = BuildController(factory, UserPrincipal(userId, isSuperAdmin: true), securityMock);

        var result = await controller.GetAllWithPermissions(default);

        var ok   = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsAssignableFrom<IEnumerable<OrganizationListItemResponse>>(ok.Value);
        var item = Assert.Single(list);
        Assert.Equal(2, item.MemberCount);
        Assert.Equal(1, item.CaseCount);
        Assert.Equal(2, item.InvestigationCount);
    }

    [Fact]
    public async Task GetAll_AsNonSuperAdmin_CountsAreZero()
    {
        var factory = CreateFactory();
        var userId  = Guid.NewGuid();
        var org     = await SeedOrgAsync(factory);

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership { Id = Guid.NewGuid(), OrganizationId = org.Id, AppUserId = Guid.NewGuid(), Role = OrganizationMemberRole.Member, IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId });
            await db.SaveChangesAsync();
        }

        var securityMock = new Mock<IOrganizationSecurityService>();
        securityMock
            .Setup(s => s.GetOrganizationsForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([org]);
        securityMock
            .Setup(s => s.HasAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<OrganizationSecurityTable>(), It.IsAny<OrganizationSecurityAction>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var controller = BuildController(factory, UserPrincipal(userId), securityMock);

        var result = await controller.GetAllWithPermissions(default);

        var ok   = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsAssignableFrom<IEnumerable<OrganizationListItemResponse>>(ok.Value);
        var item = Assert.Single(list);
        Assert.Equal(0, item.MemberCount);
        Assert.Equal(0, item.CaseCount);
        Assert.Equal(0, item.InvestigationCount);
    }

    [Fact]
    public async Task GetAll_AsMember_WithBothGrantsTrue_ReturnsCorrectFlags()
    {
        var factory = CreateFactory();
        var userId  = Guid.NewGuid();
        var org     = await SeedOrgAsync(factory);

        // GetAllWithPermissions resolves edit/delete flags via a batched query directly against
        // OrganizationAccessGrants (not IOrganizationSecurityService.HasAccessAsync, which would
        // reintroduce the N+1 this endpoint was fixed to avoid). A direct grant only counts for an
        // active member (matching HasAccessAsync's own real behavior), so seed both a non-admin
        // membership and a grant row.
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = org.Id, AppUserId = userId,
                Role = OrganizationMemberRole.Member, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
            });
            db.OrganizationAccessGrants.Add(new OrganizationAccessGrant
            {
                Id = Guid.NewGuid(), OrganizationId = org.Id, AppUserId = userId,
                TableName = OrganizationSecurityTable.Organization,
                Actions = OrganizationSecurityAction.Update | OrganizationSecurityAction.Delete,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
            });
            await db.SaveChangesAsync();
        }

        var securityMock = new Mock<IOrganizationSecurityService>();
        securityMock
            .Setup(s => s.GetOrganizationsForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([org]);

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

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = org.Id, AppUserId = userId,
                Role = OrganizationMemberRole.Member, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
            });
            db.OrganizationAccessGrants.Add(new OrganizationAccessGrant
            {
                Id = Guid.NewGuid(), OrganizationId = org.Id, AppUserId = userId,
                TableName = OrganizationSecurityTable.Organization,
                Actions = OrganizationSecurityAction.Update,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
            });
            await db.SaveChangesAsync();
        }

        var securityMock = new Mock<IOrganizationSecurityService>();
        securityMock
            .Setup(s => s.GetOrganizationsForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([org]);

        var controller = BuildController(factory, UserPrincipal(userId), securityMock);

        var result = await controller.GetAllWithPermissions(default);

        var ok   = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsAssignableFrom<IEnumerable<OrganizationListItemResponse>>(ok.Value);
        var item = Assert.Single(list);
        Assert.True(item.CanEdit);
        Assert.False(item.CanDelete);
    }

    [Fact]
    public async Task GetAll_AsOwnerMembership_ReturnsBothFlagsTrue_WithNoExplicitGrant()
    {
        var factory = CreateFactory();
        var userId  = Guid.NewGuid();
        var org     = await SeedOrgAsync(factory);

        // Owner/Administrator membership implies full access with no OrganizationAccessGrant row
        // at all -- the most common real-world path, distinct from the direct-grant tests above.
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = org.Id, AppUserId = userId,
                Role = OrganizationMemberRole.Owner, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
            });
            await db.SaveChangesAsync();
        }

        var securityMock = new Mock<IOrganizationSecurityService>();
        securityMock
            .Setup(s => s.GetOrganizationsForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([org]);

        var controller = BuildController(factory, UserPrincipal(userId), securityMock);

        var result = await controller.GetAllWithPermissions(default);

        var ok   = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsAssignableFrom<IEnumerable<OrganizationListItemResponse>>(ok.Value);
        var item = Assert.Single(list);
        Assert.True(item.CanEdit);
        Assert.True(item.CanDelete);
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

    [Fact]
    public async Task Update_ConcurrentWithDelete_NeverThrows()
    {
        // Regression: Update fetches "before" (untracked), then re-fetches the tracked row and
        // used to dereference it with `!` — if a concurrent Delete won that race, the second
        // fetch returned null and the unchecked `!` threw an unhandled NullReferenceException
        // (raw 500) instead of a clean NotFound.
        var factory     = CreateFactory();
        var userId      = Guid.NewGuid();
        var org         = await SeedOrgAsync(factory);
        var updateCtrl  = BuildController(factory, UserPrincipal(userId, isSuperAdmin: true));
        var deleteCtrl  = BuildController(factory, UserPrincipal(userId, isSuperAdmin: true));

        var updateTask = updateCtrl.Update(org.Id, new AdminUpdateOrganizationRequest("Renamed", "renamed"), default);
        var deleteTask = deleteCtrl.Delete(org.Id, default);
        var (updateResult, deleteResult) = (await updateTask, await deleteTask);

        Assert.True(updateResult.Result is OkObjectResult or NotFoundResult);
        Assert.True(deleteResult is NoContentResult or NotFoundResult);
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
    public async Task Delete_RemovesTheRowsCreatedWithTheOrganization()
    {
        // Item 148 gave every new group five default calendar event types at birth. Foreign keys
        // onto Organizations are NoAction by convention, so from that moment no newly created
        // group could be deleted at all — the delete threw a FK violation and surfaced as a 500.
        var factory    = CreateFactory();
        var userId     = Guid.NewGuid();
        var org        = await SeedOrgAsync(factory);
        var controller = BuildController(factory, UserPrincipal(userId, isSuperAdmin: true));

        await using (var seed = await factory.CreateDbContextAsync())
        {
            Ben.Data.Source.Services.OrgCalendarDefaults.AddDefaultEventTypes(seed, org.Id, userId);
            seed.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = org.Id, AppUserId = userId,
                Role = OrganizationMemberRole.Owner, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
            });
            await seed.SaveChangesAsync();
        }

        var result = await controller.Delete(org.Id, default);

        Assert.IsType<NoContentResult>(result);

        await using var db = await factory.CreateDbContextAsync();
        Assert.Null(await db.Organizations.FindAsync(org.Id));
        Assert.Empty(await db.OrgCalendarEventTypes.Where(t => t.OrganizationId == org.Id).ToListAsync());
        Assert.Empty(await db.OrganizationUserMemberships.Where(m => m.OrganizationId == org.Id).ToListAsync());
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

        // Every create door stamps the default calendar event types (OrgCalendarDefaults). This
        // door leaves org.Id for EF's client-side Guid generation, so the child rows depend on
        // the Id being real immediately after Add — this asserts that wiring holds.
        var typeCount = await db.OrgCalendarEventTypes.CountAsync(t => t.OrganizationId == created.Id);
        Assert.Equal(5, typeCount);
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

    // ── Public contact fields ─────────────────────────────────────────────────

    [Fact]
    public async Task Update_WithContactFields_PersistsContactFields()
    {
        var factory    = CreateFactory();
        var userId     = Guid.NewGuid();
        var org        = await SeedOrgAsync(factory, "Org", "org");
        var controller = BuildController(factory, UserPrincipal(userId, isSuperAdmin: true));

        var result = await controller.Update(org.Id, new AdminUpdateOrganizationRequest(
            "Org", "org",
            PublicPhone: "(615) 555-0100",
            PublicEmail: "contact@example.com",
            PublicWebsite: "https://example.com"), default);

        Assert.IsType<OkObjectResult>(result.Result);
        await using var db = await factory.CreateDbContextAsync();
        var updated = await db.Organizations.FindAsync(org.Id);
        Assert.Equal("(615) 555-0100",      updated!.PublicPhone);
        Assert.Equal("contact@example.com", updated.PublicEmail);
        Assert.Equal("https://example.com", updated.PublicWebsite);
    }

    [Fact]
    public async Task Update_WithNullContactFields_ClearsContactFields()
    {
        var factory = CreateFactory();
        var userId  = Guid.NewGuid();
        await using var setupDb = await factory.CreateDbContextAsync();
        setupDb.Organizations.Add(new Organization
        {
            Id = Guid.NewGuid(), Name = "Org", UrlName = "org",
            PublicPhone = "(615) 555-0100", PublicEmail = "old@example.com",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId
        });
        await setupDb.SaveChangesAsync();
        await using var db2 = await factory.CreateDbContextAsync();
        var orgId = (await db2.Organizations.FirstAsync(o => o.UrlName == "org")).Id;

        var controller = BuildController(factory, UserPrincipal(userId, isSuperAdmin: true));
        await controller.Update(orgId, new AdminUpdateOrganizationRequest("Org", "org"), default);

        await using var db = await factory.CreateDbContextAsync();
        var updated = await db.Organizations.FindAsync(orgId);
        Assert.Null(updated!.PublicPhone);
        Assert.Null(updated.PublicEmail);
    }

    [Fact]
    public async Task Create_WithContactFields_PersistsContactFields()
    {
        var factory    = CreateFactory();
        var userId     = Guid.NewGuid();
        var controller = BuildController(factory, UserPrincipal(userId, isSuperAdmin: true));

        var result = await controller.Create(new AdminCreateOrganizationRequest(
            "New Org", "new-org",
            PublicPhone: "(800) 555-0199",
            PublicEmail: "info@neworg.com",
            PublicWebsite: "https://neworg.com"), default);

        Assert.IsType<CreatedAtActionResult>(result.Result);
        await using var db = await factory.CreateDbContextAsync();
        var created = await db.Organizations.FirstAsync(o => o.UrlName == "new-org");
        Assert.Equal("(800) 555-0199",  created.PublicPhone);
        Assert.Equal("info@neworg.com", created.PublicEmail);
        Assert.Equal("https://neworg.com", created.PublicWebsite);
    }

    // ── GetUserDirectory (Phase A: replaces GetAllUsersAsync for org-admin CMS surfaces) ──────

    [Fact]
    public async Task GetUserDirectory_ActiveMember_ReturnsOtherActiveMembers()
    {
        var factory = CreateFactory();
        var orgId   = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var otherId  = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.AppUsers.Add(new AppUser { Id = callerId, Email = "caller@test.com", UserName = "caller@test.com", DisplayName = "Caller" });
            db.AppUsers.Add(new AppUser { Id = otherId, Email = "other@test.com", UserName = "other@test.com", DisplayName = "Other Member" });
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = callerId,
                Role = OrganizationMemberRole.Member, IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = callerId
            });
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = otherId,
                Role = OrganizationMemberRole.Member, IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = callerId
            });
            await db.SaveChangesAsync();
        }

        var controller = BuildController(factory, UserPrincipal(callerId));
        var result = await controller.GetUserDirectory(orgId, default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var entries = Assert.IsAssignableFrom<IEnumerable<OrgUserDirectoryEntry>>(ok.Value).ToList();
        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, e => e.Id == callerId && e.DisplayName == "Caller");
        Assert.Contains(entries, e => e.Id == otherId && e.DisplayName == "Other Member");
    }

    [Fact]
    public async Task GetUserDirectory_NotAMember_ReturnsForbid()
    {
        // The actual fix under test: a caller who isn't a member of this org — even if they're
        // an active member of some *other* org — cannot pull this org's member directory.
        var factory = CreateFactory();
        var orgId       = Guid.NewGuid();
        var otherOrgId  = Guid.NewGuid();
        var callerId    = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.AppUsers.Add(new AppUser { Id = callerId, Email = "caller@test.com", UserName = "caller@test.com" });
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = otherOrgId, AppUserId = callerId,
                Role = OrganizationMemberRole.Member, IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = callerId
            });
            await db.SaveChangesAsync();
        }

        var controller = BuildController(factory, UserPrincipal(callerId));
        var result = await controller.GetUserDirectory(orgId, default);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task GetUserDirectory_InactiveMembership_ReturnsForbid()
    {
        var factory = CreateFactory();
        var orgId    = Guid.NewGuid();
        var callerId = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.AppUsers.Add(new AppUser { Id = callerId, Email = "caller@test.com", UserName = "caller@test.com" });
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = callerId,
                Role = OrganizationMemberRole.Member, IsActive = false, DateCreated = DateTime.UtcNow, CreatedByAppUserId = callerId
            });
            await db.SaveChangesAsync();
        }

        var controller = BuildController(factory, UserPrincipal(callerId));
        var result = await controller.GetUserDirectory(orgId, default);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task GetUserDirectory_ExcludesInactiveMembers()
    {
        var factory  = CreateFactory();
        var orgId    = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var formerMemberId = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.AppUsers.Add(new AppUser { Id = callerId, Email = "caller@test.com", UserName = "caller@test.com", DisplayName = "Caller" });
            db.AppUsers.Add(new AppUser { Id = formerMemberId, Email = "former@test.com", UserName = "former@test.com", DisplayName = "Former Member" });
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = callerId,
                Role = OrganizationMemberRole.Member, IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = callerId
            });
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = formerMemberId,
                Role = OrganizationMemberRole.Member, IsActive = false, DateCreated = DateTime.UtcNow, CreatedByAppUserId = callerId
            });
            await db.SaveChangesAsync();
        }

        var controller = BuildController(factory, UserPrincipal(callerId));
        var result = await controller.GetUserDirectory(orgId, default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var entries = Assert.IsAssignableFrom<IEnumerable<OrgUserDirectoryEntry>>(ok.Value).ToList();
        Assert.Single(entries);
        Assert.DoesNotContain(entries, e => e.Id == formerMemberId);
    }
}
