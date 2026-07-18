using AutoMapper;
using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Cms;
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

public class CmsPagePermissionControllerTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var opts = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new PooledDbContextFactory<BenDataContext>(opts);
    }

    private static Mock<IMapper> CreateMapperMock()
    {
        var m = new Mock<IMapper>();
        m.Setup(x => x.Map<IEnumerable<CmsPagePermissionRecord>>(It.IsAny<object>()))
         .Returns(Array.Empty<CmsPagePermissionRecord>());
        m.Setup(x => x.Map<CmsPagePermissionRecord>(It.IsAny<object>()))
         .Returns<object>(o => o is CmsPagePermission p
             ? new CmsPagePermissionRecord { Id = p.Id, OrganizationPageId = p.OrganizationPageId, Actions = p.Actions }
             : new CmsPagePermissionRecord { Actions = CmsPageAction.View });
        return m;
    }

    private static CmsPagePermissionController Build(
        IDbContextFactory<BenDataContext> factory,
        ClaimsPrincipal? principal = null,
        Mock<IOrganizationSecurityService>? security = null)
    {
        security ??= GrantAll();
        var ctrl = new CmsPagePermissionController(factory, CreateMapperMock().Object, security.Object);
        ctrl.ControllerContext = new ControllerContext
            { HttpContext = new DefaultHttpContext { User = principal ?? Anonymous() } };
        return ctrl;
    }

    private static ClaimsPrincipal Anonymous() => new(new ClaimsIdentity());

    private static ClaimsPrincipal SuperAdmin(Guid id) =>
        new(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, id.ToString()),
            new Claim(ClaimTypes.Role, RoleNames.SuperAdmin)
        ], "Bearer"));

    private static Mock<IOrganizationSecurityService> GrantAll()
    {
        var s = new Mock<IOrganizationSecurityService>();
        s.Setup(x => x.HasAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid>(),
              It.IsAny<OrganizationSecurityTable>(), It.IsAny<OrganizationSecurityAction>(),
              It.IsAny<CancellationToken>()))
         .ReturnsAsync(true);
        return s;
    }

    private static async Task<OrganizationPage> SeedPageAsync(IDbContextFactory<BenDataContext> f, Guid orgId)
    {
        await using var db = await f.CreateDbContextAsync();
        var p = new OrganizationPage
        {
            Id = Guid.NewGuid(), OrganizationId = orgId,
            PageTitle = "P", UrlName = "p", PageHtml = "",
            IsPublished = false, SortOrder = 1,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid()
        };
        db.OrganizationPages.Add(p);
        await db.SaveChangesAsync();
        return p;
    }

    private static async Task<CmsPagePermission> SeedPermissionAsync(
        IDbContextFactory<BenDataContext> f, Guid pageId, Guid userId)
    {
        await using var db = await f.CreateDbContextAsync();
        var perm = new CmsPagePermission
        {
            Id = Guid.NewGuid(), OrganizationPageId = pageId,
            AppUserId = userId, Actions = CmsPageAction.View,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid()
        };
        db.CmsPagePermissions.Add(perm);
        await db.SaveChangesAsync();
        return perm;
    }

    // ── GetAll ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_WhenNoUserId_ReturnsUnauthorized()
    {
        var ctrl   = Build(CreateFactory(), Anonymous());
        var result = await ctrl.GetAll(Guid.NewGuid(), Guid.NewGuid(), default);
        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public async Task GetAll_WhenPageNotFound_ReturnsNotFound()
    {
        var ctrl   = Build(CreateFactory(), SuperAdmin(Guid.NewGuid()));
        var result = await ctrl.GetAll(Guid.NewGuid(), Guid.NewGuid(), default);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetAll_AsSuperAdmin_ReturnsPermissions()
    {
        var factory = CreateFactory();
        var orgId   = Guid.NewGuid();
        var page    = await SeedPageAsync(factory, orgId);
        await SeedPermissionAsync(factory, page.Id, Guid.NewGuid());

        var ctrl   = Build(factory, SuperAdmin(Guid.NewGuid()));
        var result = await ctrl.GetAll(orgId, page.Id, default);
        Assert.IsType<OkObjectResult>(result.Result);
    }

    // ── Create ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_WhenNoUserId_ReturnsUnauthorized()
    {
        var ctrl   = Build(CreateFactory(), Anonymous());
        var result = await ctrl.Create(Guid.NewGuid(), Guid.NewGuid(),
            new CreatePagePermissionRequest(Guid.NewGuid(), null, CmsPageAction.View), default);
        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public async Task Create_WhenNeitherUserNorGroupSpecified_ReturnsBadRequest()
    {
        var factory = CreateFactory();
        var orgId   = Guid.NewGuid();
        var page    = await SeedPageAsync(factory, orgId);
        var ctrl    = Build(factory, SuperAdmin(Guid.NewGuid()));

        var result = await ctrl.Create(orgId, page.Id,
            new CreatePagePermissionRequest(null, null, CmsPageAction.View), default);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("AppUserId or OrgMemberGroupId", bad.Value?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_WhenActionsIsNone_ReturnsBadRequest()
    {
        var factory = CreateFactory();
        var orgId   = Guid.NewGuid();
        var page    = await SeedPageAsync(factory, orgId);
        var ctrl    = Build(factory, SuperAdmin(Guid.NewGuid()));

        var result = await ctrl.Create(orgId, page.Id,
            new CreatePagePermissionRequest(Guid.NewGuid(), null, CmsPageAction.None), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Create_WhenPageNotFound_ReturnsNotFound()
    {
        var ctrl   = Build(CreateFactory(), SuperAdmin(Guid.NewGuid()));
        var result = await ctrl.Create(Guid.NewGuid(), Guid.NewGuid(),
            new CreatePagePermissionRequest(Guid.NewGuid(), null, CmsPageAction.View), default);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Create_ForUser_AsSuperAdmin_CreatesPermission()
    {
        var factory = CreateFactory();
        var orgId   = Guid.NewGuid();
        var page    = await SeedPageAsync(factory, orgId);
        var targetUser = Guid.NewGuid();
        var ctrl    = Build(factory, SuperAdmin(Guid.NewGuid()));

        var result = await ctrl.Create(orgId, page.Id,
            new CreatePagePermissionRequest(targetUser, null, CmsPageAction.View | CmsPageAction.Edit), default);

        Assert.IsType<CreatedAtActionResult>(result.Result);

        await using var db = await factory.CreateDbContextAsync();
        var perm = await db.CmsPagePermissions.FirstOrDefaultAsync(p => p.OrganizationPageId == page.Id);
        Assert.NotNull(perm);
        Assert.Equal(targetUser, perm.AppUserId);
        Assert.True(perm.Actions.HasFlag(CmsPageAction.View));
        Assert.True(perm.Actions.HasFlag(CmsPageAction.Edit));
        Assert.False(perm.Actions.HasFlag(CmsPageAction.Delete));
    }

    [Fact]
    public async Task Create_ForGroup_AsSuperAdmin_CreatesPermission()
    {
        var factory = CreateFactory();
        var orgId   = Guid.NewGuid();
        var page    = await SeedPageAsync(factory, orgId);
        var groupId = Guid.NewGuid();
        var ctrl    = Build(factory, SuperAdmin(Guid.NewGuid()));

        var result = await ctrl.Create(orgId, page.Id,
            new CreatePagePermissionRequest(null, groupId, CmsPageAction.View), default);

        Assert.IsType<CreatedAtActionResult>(result.Result);

        await using var db = await factory.CreateDbContextAsync();
        var perm = await db.CmsPagePermissions.FirstOrDefaultAsync(p => p.OrganizationPageId == page.Id);
        Assert.NotNull(perm);
        Assert.Null(perm.AppUserId);
        Assert.Equal(groupId, perm.OrgMemberGroupId);
    }

    // ── Update ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_WhenActionsIsNone_ReturnsBadRequest()
    {
        var factory = CreateFactory();
        var orgId   = Guid.NewGuid();
        var page    = await SeedPageAsync(factory, orgId);
        var perm    = await SeedPermissionAsync(factory, page.Id, Guid.NewGuid());
        var ctrl    = Build(factory, SuperAdmin(Guid.NewGuid()));

        var result = await ctrl.Update(orgId, page.Id, perm.Id,
            new UpdatePagePermissionRequest(CmsPageAction.None), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Update_AsSuperAdmin_UpdatesActions()
    {
        var factory = CreateFactory();
        var orgId   = Guid.NewGuid();
        var page    = await SeedPageAsync(factory, orgId);
        var perm    = await SeedPermissionAsync(factory, page.Id, Guid.NewGuid());
        var ctrl    = Build(factory, SuperAdmin(Guid.NewGuid()));

        var result = await ctrl.Update(orgId, page.Id, perm.Id,
            new UpdatePagePermissionRequest(CmsPageAction.View | CmsPageAction.Edit | CmsPageAction.Delete), default);

        Assert.IsType<OkObjectResult>(result.Result);

        await using var db = await factory.CreateDbContextAsync();
        var updated = await db.CmsPagePermissions.FindAsync(perm.Id);
        Assert.True(updated!.Actions.HasFlag(CmsPageAction.Delete));
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_WhenNotFound_ReturnsNotFound()
    {
        var factory = CreateFactory();
        var page    = await SeedPageAsync(factory, Guid.NewGuid());
        var ctrl    = Build(factory, SuperAdmin(Guid.NewGuid()));

        var result = await ctrl.Delete(page.OrganizationId, page.Id, Guid.NewGuid(), default);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Delete_AsSuperAdmin_RemovesPermission()
    {
        var factory = CreateFactory();
        var orgId   = Guid.NewGuid();
        var page    = await SeedPageAsync(factory, orgId);
        var perm    = await SeedPermissionAsync(factory, page.Id, Guid.NewGuid());
        var ctrl    = Build(factory, SuperAdmin(Guid.NewGuid()));

        var result = await ctrl.Delete(orgId, page.Id, perm.Id, default);

        Assert.IsType<NoContentResult>(result);

        await using var db = await factory.CreateDbContextAsync();
        Assert.Null(await db.CmsPagePermissions.FindAsync(perm.Id));
    }
}
