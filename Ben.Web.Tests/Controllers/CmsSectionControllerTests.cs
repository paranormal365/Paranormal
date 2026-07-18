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

public class CmsSectionControllerTests
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
        m.Setup(x => x.Map<IEnumerable<CmsSectionRecord>>(It.IsAny<object>()))
         .Returns(Array.Empty<CmsSectionRecord>());
        m.Setup(x => x.Map<CmsSectionRecord>(It.IsAny<object>()))
         .Returns<object>(o => o is CmsSection s
             ? new CmsSectionRecord { Id = s.Id, OrganizationPageId = s.OrganizationPageId, ContentJson = s.ContentJson, SortOrder = s.SortOrder }
             : new CmsSectionRecord { ContentJson = "{}", SortOrder = 1 });
        return m;
    }

    private static CmsSectionController Build(
        IDbContextFactory<BenDataContext> factory,
        ClaimsPrincipal? principal = null,
        Mock<IOrganizationSecurityService>? security = null)
    {
        security ??= GrantAll();
        var ctrl = new CmsSectionController(factory, CreateMapperMock().Object, security.Object);
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

    private static Mock<IOrganizationSecurityService> DenyAll()
    {
        var s = new Mock<IOrganizationSecurityService>();
        s.Setup(x => x.HasAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid>(),
              It.IsAny<OrganizationSecurityTable>(), It.IsAny<OrganizationSecurityAction>(),
              It.IsAny<CancellationToken>()))
         .ReturnsAsync(false);
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

    private static async Task<CmsSection> SeedSectionAsync(
        IDbContextFactory<BenDataContext> f, Guid pageId, int sortOrder = 1)
    {
        await using var db = await f.CreateDbContextAsync();
        var s = new CmsSection
        {
            Id = Guid.NewGuid(), OrganizationPageId = pageId,
            SectionType = CmsSectionType.RichText, ContentJson = "{\"html\":\"<p>Hi</p>\"}",
            SortOrder = sortOrder, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid()
        };
        db.CmsSections.Add(s);
        await db.SaveChangesAsync();
        return s;
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
    public async Task GetAll_WhenForbidden_ReturnsForbid()
    {
        var factory = CreateFactory();
        var page    = await SeedPageAsync(factory, Guid.NewGuid());
        var ctrl    = Build(factory, new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())
        ], "Bearer")), DenyAll());

        var result = await ctrl.GetAll(page.OrganizationId, page.Id, default);
        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task GetAll_AsSuperAdmin_ReturnsSections()
    {
        var factory = CreateFactory();
        var orgId   = Guid.NewGuid();
        var page    = await SeedPageAsync(factory, orgId);
        await SeedSectionAsync(factory, page.Id, 1);
        await SeedSectionAsync(factory, page.Id, 2);

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
            new CreateCmsSectionRequest(CmsSectionType.RichText, null, "{}", 1, true), default);
        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public async Task Create_WhenPageNotFound_ReturnsNotFound()
    {
        var ctrl   = Build(CreateFactory(), SuperAdmin(Guid.NewGuid()));
        var result = await ctrl.Create(Guid.NewGuid(), Guid.NewGuid(),
            new CreateCmsSectionRequest(CmsSectionType.RichText, null, "{}", 1, true), default);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Create_AsSuperAdmin_CreatesSection()
    {
        var factory = CreateFactory();
        var orgId   = Guid.NewGuid();
        var page    = await SeedPageAsync(factory, orgId);
        var userId  = Guid.NewGuid();
        var ctrl    = Build(factory, SuperAdmin(userId));

        var result = await ctrl.Create(orgId, page.Id,
            new CreateCmsSectionRequest(CmsSectionType.CustomHtml, "Intro", "{\"html\":\"<h1>Hi</h1>\"}", 1, true),
            default);

        Assert.IsType<CreatedAtActionResult>(result.Result);

        await using var db = await factory.CreateDbContextAsync();
        Assert.True(await db.CmsSections.AnyAsync(s => s.OrganizationPageId == page.Id));
    }

    // ── Update ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_WhenNotFound_ReturnsNotFound()
    {
        var factory = CreateFactory();
        var page    = await SeedPageAsync(factory, Guid.NewGuid());
        var ctrl    = Build(factory, SuperAdmin(Guid.NewGuid()));

        var result = await ctrl.Update(page.OrganizationId, page.Id, Guid.NewGuid(),
            new UpdateCmsSectionRequest("Title", "{}", true), default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Update_AsSuperAdmin_UpdatesSection()
    {
        var factory = CreateFactory();
        var orgId   = Guid.NewGuid();
        var page    = await SeedPageAsync(factory, orgId);
        var section = await SeedSectionAsync(factory, page.Id);
        var ctrl    = Build(factory, SuperAdmin(Guid.NewGuid()));

        var result = await ctrl.Update(orgId, page.Id, section.Id,
            new UpdateCmsSectionRequest("New Title", "{\"html\":\"<p>Updated</p>\"}", false), default);

        Assert.IsType<OkObjectResult>(result.Result);

        await using var db = await factory.CreateDbContextAsync();
        var updated = await db.CmsSections.FindAsync(section.Id);
        Assert.Equal("New Title", updated!.Title);
        Assert.False(updated.IsActive);
    }

    // ── Reorder ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Reorder_AsSuperAdmin_ReturnsNoContent()
    {
        var factory = CreateFactory();
        var orgId   = Guid.NewGuid();
        var page    = await SeedPageAsync(factory, orgId);
        var s1      = await SeedSectionAsync(factory, page.Id, 1);
        var s2      = await SeedSectionAsync(factory, page.Id, 2);
        var ctrl    = Build(factory, SuperAdmin(Guid.NewGuid()));

        var result = await ctrl.Reorder(orgId, page.Id,
            new ReorderCmsSectionsRequest([s2.Id, s1.Id]), default);

        Assert.IsType<NoContentResult>(result);
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
    public async Task Delete_AsSuperAdmin_RemovesSection()
    {
        var factory = CreateFactory();
        var orgId   = Guid.NewGuid();
        var page    = await SeedPageAsync(factory, orgId);
        var section = await SeedSectionAsync(factory, page.Id);
        var ctrl    = Build(factory, SuperAdmin(Guid.NewGuid()));

        var result = await ctrl.Delete(orgId, page.Id, section.Id, default);

        Assert.IsType<NoContentResult>(result);

        await using var db = await factory.CreateDbContextAsync();
        Assert.Null(await db.CmsSections.FindAsync(section.Id));
    }
}
