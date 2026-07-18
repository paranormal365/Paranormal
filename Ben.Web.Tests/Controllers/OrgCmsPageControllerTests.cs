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

/// <summary>Tests for OrgCmsPageController — permission-aware CMS page CRUD.</summary>
public class OrgCmsPageControllerTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var options = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new PooledDbContextFactory<BenDataContext>(options);
    }

    private static Mock<IMapper> CreateMapperMock()
    {
        var m = new Mock<IMapper>();
        m.Setup(x => x.Map<IReadOnlyList<CmsSectionRecord>>(It.IsAny<object>()))
         .Returns(Array.Empty<CmsSectionRecord>());
        return m;
    }

    private static OrgCmsPageController BuildController(
        IDbContextFactory<BenDataContext> factory,
        ClaimsPrincipal? principal = null,
        Mock<IOrganizationSecurityService>? security = null,
        Mock<IMapper>? mapper = null)
    {
        security ??= new Mock<IOrganizationSecurityService>();
        mapper   ??= CreateMapperMock();
        var ctrl = new OrgCmsPageController(factory, mapper.Object, security.Object);
        ctrl.ControllerContext = new ControllerContext
            { HttpContext = new DefaultHttpContext { User = principal ?? Anonymous() } };
        return ctrl;
    }

    private static ClaimsPrincipal Anonymous() => new(new ClaimsIdentity());

    private static ClaimsPrincipal SuperAdmin(Guid userId) =>
        new(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, RoleNames.SuperAdmin)
        ], "Bearer"));

    private static ClaimsPrincipal User(Guid userId) =>
        new(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, userId.ToString())
        ], "Bearer"));

    private static Mock<IOrganizationSecurityService> GrantAll()
    {
        var s = new Mock<IOrganizationSecurityService>();
        s.Setup(x => x.HasAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid>(),
              It.IsAny<OrganizationSecurityTable>(), It.IsAny<OrganizationSecurityAction>(),
              It.IsAny<CancellationToken>()))
         .ReturnsAsync(true);
        s.Setup(x => x.GetOrganizationsForUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
         .ReturnsAsync([]);
        return s;
    }

    private static Mock<IOrganizationSecurityService> DenyAll()
    {
        var s = new Mock<IOrganizationSecurityService>();
        s.Setup(x => x.HasAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid>(),
              It.IsAny<OrganizationSecurityTable>(), It.IsAny<OrganizationSecurityAction>(),
              It.IsAny<CancellationToken>()))
         .ReturnsAsync(false);
        return s;
    }

    private static async Task<OrganizationPage> SeedPageAsync(
        IDbContextFactory<BenDataContext> f, Guid orgId, string urlName = "test")
    {
        await using var db = await f.CreateDbContextAsync();
        var page = new OrganizationPage
        {
            Id = Guid.NewGuid(), OrganizationId = orgId,
            PageTitle = "Test", UrlName = urlName, PageHtml = "",
            IsPublished = false, IsPublic = false, SortOrder = 1,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid()
        };
        db.OrganizationPages.Add(page);
        await db.SaveChangesAsync();
        return page;
    }

    // ── GetAll ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_WhenNoUserId_ReturnsUnauthorized()
    {
        var f = CreateFactory();
        var ctrl = BuildController(f, Anonymous());
        var result = await ctrl.GetAll(Guid.NewGuid(), default);
        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public async Task GetAll_WhenForbidden_ReturnsForbid()
    {
        var f = CreateFactory();
        var ctrl = BuildController(f, User(Guid.NewGuid()), DenyAll());
        var result = await ctrl.GetAll(Guid.NewGuid(), default);
        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task GetAll_AsSuperAdmin_ReturnsPageList()
    {
        var factory = CreateFactory();
        var orgId   = Guid.NewGuid();
        var userId  = Guid.NewGuid();
        await SeedPageAsync(factory, orgId, "home");
        await SeedPageAsync(factory, orgId, "about");

        var security = new Mock<IOrganizationSecurityService>();
        security.Setup(x => x.GetOrganizationsForUserAsync(userId, It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);

        var ctrl = BuildController(factory, SuperAdmin(userId), security);
        var result = await ctrl.GetAll(orgId, default);

        var ok   = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsAssignableFrom<IEnumerable<CmsPageListItemResponse>>(ok.Value);
        Assert.Equal(2, list.Count());
    }

    // ── GetById ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetById_WhenNoUserId_ReturnsUnauthorized()
    {
        var f    = CreateFactory();
        var ctrl = BuildController(f, Anonymous());
        var result = await ctrl.GetById(Guid.NewGuid(), Guid.NewGuid(), default);
        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public async Task GetById_WhenPageNotFound_ReturnsNotFound()
    {
        var f    = CreateFactory();
        var ctrl = BuildController(f, SuperAdmin(Guid.NewGuid()));
        var result = await ctrl.GetById(Guid.NewGuid(), Guid.NewGuid(), default);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetById_WhenForbidden_ReturnsForbid()
    {
        var factory = CreateFactory();
        var orgId   = Guid.NewGuid();
        var page    = await SeedPageAsync(factory, orgId);
        var ctrl    = BuildController(factory, User(Guid.NewGuid()), DenyAll());
        var result  = await ctrl.GetById(orgId, page.Id, default);
        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task GetById_AsSuperAdmin_ReturnsPageDetail()
    {
        var factory = CreateFactory();
        var orgId   = Guid.NewGuid();
        var page    = await SeedPageAsync(factory, orgId, "home");
        var ctrl    = BuildController(factory, SuperAdmin(Guid.NewGuid()));

        var result  = await ctrl.GetById(orgId, page.Id, default);

        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var detail = Assert.IsType<CmsPageDetailResponse>(ok.Value);
        Assert.Equal(page.Id, detail.Id);
        Assert.Equal("home", detail.UrlName);
    }

    // ── Create ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_WhenNoUserId_ReturnsUnauthorized()
    {
        var f    = CreateFactory();
        var ctrl = BuildController(f, Anonymous());
        var result = await ctrl.Create(Guid.NewGuid(), new CreateCmsPageRequest("T", "t", null, false, null, 1), default);
        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public async Task Create_WithBlankTitle_ReturnsBadRequest()
    {
        var f    = CreateFactory();
        var ctrl = BuildController(f, SuperAdmin(Guid.NewGuid()));
        var result = await ctrl.Create(Guid.NewGuid(), new CreateCmsPageRequest("", "url", null, false, null, 1), default);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Create_WithDuplicateUrlName_ReturnsBadRequest()
    {
        var factory = CreateFactory();
        var orgId   = Guid.NewGuid();
        await SeedPageAsync(factory, orgId, "existing");
        var ctrl = BuildController(factory, SuperAdmin(Guid.NewGuid()));

        var result = await ctrl.Create(orgId, new CreateCmsPageRequest("Another", "existing", null, false, null, 1), default);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("existing", bad.Value?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_AsSuperAdmin_CreatesPage()
    {
        var factory = CreateFactory();
        var orgId   = Guid.NewGuid();
        var userId  = Guid.NewGuid();
        var ctrl    = BuildController(factory, SuperAdmin(userId));

        var result = await ctrl.Create(orgId, new CreateCmsPageRequest("My Page", "My-Page", null, true, null, 1), default);

        Assert.IsType<CreatedAtActionResult>(result.Result);

        await using var db = await factory.CreateDbContextAsync();
        var p = await db.OrganizationPages.FirstOrDefaultAsync(x => x.UrlName == "my-page");
        Assert.NotNull(p);
        Assert.True(p.IsPublic);
        Assert.Equal(userId, p.CreatedByAppUserId);
    }

    // ── Update ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_WhenSelfParent_ReturnsBadRequest()
    {
        var factory = CreateFactory();
        var orgId   = Guid.NewGuid();
        var page    = await SeedPageAsync(factory, orgId);
        var ctrl    = BuildController(factory, SuperAdmin(Guid.NewGuid()));

        var result = await ctrl.Update(orgId, page.Id,
            new UpdateCmsPageRequest("T", "t", null, false, false, page.Id, 1), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Update_AsSuperAdmin_PersistsChanges()
    {
        var factory = CreateFactory();
        var orgId   = Guid.NewGuid();
        var page    = await SeedPageAsync(factory, orgId, "old-url");
        var ctrl    = BuildController(factory, SuperAdmin(Guid.NewGuid()));

        var result = await ctrl.Update(orgId, page.Id,
            new UpdateCmsPageRequest("New Title", "new-url", "<p>intro</p>", true, true, null, 2), default);

        Assert.IsType<OkObjectResult>(result.Result);

        await using var db = await factory.CreateDbContextAsync();
        var updated = await db.OrganizationPages.FindAsync(page.Id);
        Assert.Equal("New Title", updated!.PageTitle);
        Assert.Equal("new-url", updated.UrlName);
        Assert.True(updated.IsPublished);
        Assert.True(updated.IsPublic);
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_AsSuperAdmin_ReparentsChildrenToGrandparent()
    {
        var factory = CreateFactory();
        var orgId   = Guid.NewGuid();
        var parent  = await SeedPageAsync(factory, orgId, "parent");

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.OrganizationPages.Add(new OrganizationPage
            {
                Id = Guid.NewGuid(), OrganizationId = orgId,
                PageTitle = "Child", UrlName = "child", PageHtml = "",
                ParentPageId = parent.Id,
                IsPublished = false, SortOrder = 1,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid()
            });
            await db.SaveChangesAsync();
        }

        var ctrl = BuildController(factory, SuperAdmin(Guid.NewGuid()));
        var result = await ctrl.Delete(orgId, parent.Id, default);

        Assert.IsType<NoContentResult>(result);

        await using var db2 = await factory.CreateDbContextAsync();
        // Parent deleted
        Assert.Null(await db2.OrganizationPages.FindAsync(parent.Id));
        // Child re-parented to null (parent had no parent)
        var child = await db2.OrganizationPages.FirstAsync(p => p.UrlName == "child");
        Assert.Null(child.ParentPageId);
    }
}
