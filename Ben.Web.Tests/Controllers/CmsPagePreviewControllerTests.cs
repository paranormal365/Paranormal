using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Cms;
using Ben.Data.WebApi.Controllers.Public;
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
/// Previewing a CMS page before it is published (backlog item #80, part 1).
/// </summary>
/// <remarks>
/// The preview's whole reason to exist is that it ignores <c>IsPublished</c>. That makes its
/// permission check the only thing standing between an unpublished page and the public, so the
/// tests below are mostly about who is refused rather than what is rendered.
/// </remarks>
public sealed class CmsPagePreviewControllerTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid EditorId = Guid.NewGuid();

    private static IDbContextFactory<BenDataContext> CreateFactory()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static CmsPagePreviewController Build(
        IDbContextFactory<BenDataContext> factory, Guid? userId, bool authorized)
    {
        var security = new Mock<IOrganizationSecurityService>();
        security.Setup(x => x.HasAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid>(),
                    It.IsAny<OrganizationSecurityTable>(), It.IsAny<OrganizationSecurityAction>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(authorized);

        var identity = userId is null
            ? new ClaimsIdentity()
            : new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString())], "Bearer");

        return new CmsPagePreviewController(factory, new Mock<IMapper>().Object, security.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
            }
        };
    }

    /// <summary>One unpublished page with one active section and one inactive one.</summary>
    private static async Task<Guid> SeedUnpublishedPageAsync(IDbContextFactory<BenDataContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();

        db.Organizations.Add(new Organization
        { Id = OrgId, Name = "Ghost Squad", UrlName = "ghost-squad", DateCreated = DateTime.UtcNow });

        var pageId = Guid.NewGuid();
        db.OrganizationPages.Add(new OrganizationPage
        {
            Id = pageId, OrganizationId = OrgId, PageTitle = "About Us", UrlName = "about",
            IsPublished = false, IsPublic = false, SortOrder = 1, DateCreated = DateTime.UtcNow,
        });

        db.CmsSections.Add(new CmsSection
        {
            Id = Guid.NewGuid(), OrganizationPageId = pageId, SectionType = CmsSectionType.RichText,
            Title = "Who we are", ContentJson = """{"html":"<p>Hello</p>"}""",
            SortOrder = 1, IsActive = true, DateCreated = DateTime.UtcNow,
        });
        db.CmsSections.Add(new CmsSection
        {
            Id = Guid.NewGuid(), OrganizationPageId = pageId, SectionType = CmsSectionType.RichText,
            Title = "Old draft", ContentJson = """{"html":"<p>Removed</p>"}""",
            SortOrder = 2, IsActive = false, DateCreated = DateTime.UtcNow,
        });

        await db.SaveChangesAsync();
        return pageId;
    }

    [Fact]
    public async Task An_editor_sees_an_unpublished_page()
    {
        var factory = CreateFactory();
        var pageId  = await SeedUnpublishedPageAsync(factory);

        var result = await Build(factory, EditorId, authorized: true).GetPreview(OrgId, pageId, default);
        var page   = Assert.IsType<OrgPublicPageResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Equal("About Us", page.Page.PageTitle);
        Assert.Equal("Ghost Squad", page.OrgName);
    }

    /// <summary>
    /// Only the publish flag is relaxed. Everything else — which sections are shown, and in what
    /// order — stays the public rule, or a preview would be reassuring about a page that will not
    /// look like that.
    /// </summary>
    [Fact]
    public async Task Only_the_publish_flag_is_relaxed_not_the_rest_of_the_public_rule()
    {
        var factory = CreateFactory();
        var pageId  = await SeedUnpublishedPageAsync(factory);

        var result = await Build(factory, EditorId, authorized: true).GetPreview(OrgId, pageId, default);
        var page   = Assert.IsType<OrgPublicPageResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);

        var section = Assert.Single(page.Page.Sections);
        Assert.Equal("Who we are", section.Title);
    }

    [Fact]
    public async Task Somebody_without_the_permission_is_refused()
    {
        var factory = CreateFactory();
        var pageId  = await SeedUnpublishedPageAsync(factory);

        // 404 rather than 403: an unpublished page should not be confirmed to exist by the shape of
        // the refusal, which is the same rule the equipment surfaces follow.
        var result = await Build(factory, Guid.NewGuid(), authorized: false).GetPreview(OrgId, pageId, default);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task An_anonymous_caller_is_refused()
    {
        var factory = CreateFactory();
        var pageId  = await SeedUnpublishedPageAsync(factory);

        var result = await Build(factory, userId: null, authorized: true).GetPreview(OrgId, pageId, default);
        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    /// <summary>
    /// A page belonging to a different group is not previewable by passing this group's id — the
    /// permission is checked against the org in the route, so the page must belong to it too.
    /// </summary>
    [Fact]
    public async Task A_page_from_another_group_is_not_reachable_through_this_one()
    {
        var factory = CreateFactory();
        await SeedUnpublishedPageAsync(factory);

        var otherOrgId = Guid.NewGuid();
        var otherPageId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Organizations.Add(new Organization
            { Id = otherOrgId, Name = "Rivals", UrlName = "rivals", DateCreated = DateTime.UtcNow });
            db.OrganizationPages.Add(new OrganizationPage
            {
                Id = otherPageId, OrganizationId = otherOrgId, PageTitle = "Secret", UrlName = "secret",
                IsPublished = false, IsPublic = false, DateCreated = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var result = await Build(factory, EditorId, authorized: true).GetPreview(OrgId, otherPageId, default);
        Assert.IsType<NotFoundResult>(result.Result);
    }
}
