using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Cms;
using Ben.Data.WebApi.Controllers.Public;
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
/// CMS page drafts (backlog item #80, part 3) — editing a live page without the public watching.
/// </summary>
/// <remarks>
/// <para>A draft is an <c>OrganizationPage</c> row of its own. That makes one thing worth testing
/// above everything else: <b>a draft must be invisible to the public read path</b>, and invisible
/// because of its own flags rather than because somebody remembered to exclude it. The test that
/// fetches the published page while a draft exists is the one that matters.</para>
///
/// <para>The second is that publishing keeps the live page's <b>id</b>. Swapping rows would be
/// simpler and would silently break every link, permission row and case attached to the page.</para>
/// </remarks>
public sealed class CmsPageDraftControllerTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid EditorId = Guid.NewGuid();

    private static IDbContextFactory<BenDataContext> CreateFactory()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static Mock<IOrganizationSecurityService> Security(bool authorized)
    {
        var s = new Mock<IOrganizationSecurityService>();
        s.Setup(x => x.HasAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid>(),
              It.IsAny<OrganizationSecurityTable>(), It.IsAny<OrganizationSecurityAction>(),
              It.IsAny<CancellationToken>()))
         .ReturnsAsync(authorized);
        return s;
    }

    private static ControllerContext Context(Guid? userId)
        => new()
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(userId is null
                    ? new ClaimsIdentity()
                    : new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString())], "Bearer")),
            }
        };

    private static CmsPageDraftController Build(
        IDbContextFactory<BenDataContext> factory, Guid? userId = null, bool authorized = true)
        => new(factory, new Mock<IMapper>().Object, Security(authorized).Object,
               new Mock<IAuditLogService>().Object)
        { ControllerContext = Context(userId ?? EditorId) };

    private static OrgPublicController BuildPublic(IDbContextFactory<BenDataContext> factory)
        => new(factory) { ControllerContext = Context(null) };

    /// <summary>One published, public page with two sections.</summary>
    private static async Task<Guid> SeedLivePageAsync(IDbContextFactory<BenDataContext> factory, bool published = true)
    {
        await using var db = await factory.CreateDbContextAsync();

        db.Organizations.Add(new Organization
        { Id = OrgId, Name = "Ghost Squad", UrlName = "ghost-squad", DateCreated = DateTime.UtcNow });

        var pageId = Guid.NewGuid();
        db.OrganizationPages.Add(new OrganizationPage
        {
            Id = pageId, OrganizationId = OrgId, PageTitle = "About Us", UrlName = "about",
            PageHtml = "", IsPublished = published, IsPublic = published,
            SortOrder = 1, DateCreated = DateTime.UtcNow,
        });

        foreach (var (title, order) in new[] { ("Who we are", 1), ("What we do", 2) })
            db.CmsSections.Add(new CmsSection
            {
                Id = Guid.NewGuid(), OrganizationPageId = pageId, SectionType = CmsSectionType.RichText,
                Title = title, ContentJson = $$"""{"html":"<p>{{title}}</p>"}""",
                SortOrder = order, IsActive = true, DateCreated = DateTime.UtcNow,
            });

        await db.SaveChangesAsync();
        return pageId;
    }

    private static async Task<Guid> StartDraftAsync(IDbContextFactory<BenDataContext> factory, Guid pageId)
    {
        var result = await Build(factory).StartDraft(OrgId, pageId, default);
        var state  = Assert.IsType<CmsDraftStateResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.NotNull(state.DraftPageId);
        return state.DraftPageId!.Value;
    }

    // ── The draft is invisible ───────────────────────────────────────────────

    /// <summary>
    /// The public page is untouched while a draft exists and is edited. This is the whole promise.
    /// </summary>
    [Fact]
    public async Task A_draft_changes_nothing_a_visitor_sees()
    {
        var factory = CreateFactory();
        var pageId  = await SeedLivePageAsync(factory);
        var draftId = await StartDraftAsync(factory, pageId);

        await using (var db = await factory.CreateDbContextAsync())
        {
            var draft = await db.OrganizationPages.SingleAsync(p => p.Id == draftId);
            draft.PageTitle = "Completely Rewritten";
            var section = await db.CmsSections.FirstAsync(s => s.OrganizationPageId == draftId);
            section.Title = "Draft heading";
            await db.SaveChangesAsync();
        }

        var result = await BuildPublic(factory).GetPage("ghost-squad", "about", default);
        var page   = Assert.IsType<OrgPublicPageResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Equal("About Us", page.Page.PageTitle);
        Assert.Equal(pageId, page.Page.Id);
        Assert.Contains(page.Page.Sections, s => s.Title == "Who we are");
        Assert.DoesNotContain(page.Page.Sections, s => s.Title == "Draft heading");
    }

    /// <summary>
    /// And it is invisible because of its own flags, not because a query remembered to exclude it —
    /// which is what lets every existing and future public query stay unaware drafts exist.
    /// </summary>
    [Fact]
    public async Task A_draft_is_stored_unpublished_and_not_public()
    {
        var factory = CreateFactory();
        var pageId  = await SeedLivePageAsync(factory);
        var draftId = await StartDraftAsync(factory, pageId);

        await using var db = await factory.CreateDbContextAsync();
        var draft = await db.OrganizationPages.AsNoTracking().SingleAsync(p => p.Id == draftId);

        Assert.False(draft.IsPublished);
        Assert.False(draft.IsPublic);
        Assert.False(draft.IsHome);
        Assert.True(draft.IsDraft);
    }

    [Fact]
    public async Task A_draft_copies_the_pages_sections()
    {
        var factory = CreateFactory();
        var pageId  = await SeedLivePageAsync(factory);
        var draftId = await StartDraftAsync(factory, pageId);

        await using var db = await factory.CreateDbContextAsync();
        var draftSections = await db.CmsSections.AsNoTracking()
            .Where(s => s.OrganizationPageId == draftId).OrderBy(s => s.SortOrder).ToListAsync();

        Assert.Equal(2, draftSections.Count);
        Assert.Equal("Who we are", draftSections[0].Title);

        // Copies, not the same rows — editing the draft must not reach into the live page.
        var liveSectionIds = await db.CmsSections.AsNoTracking()
            .Where(s => s.OrganizationPageId == pageId).Select(s => s.Id).ToListAsync();
        Assert.Empty(draftSections.Select(s => s.Id).Intersect(liveSectionIds));
    }

    // ── Starting one ─────────────────────────────────────────────────────────

    /// <summary>
    /// Two editors opening the page at once, or one double-click, must not make two drafts — the
    /// unique index behind this would otherwise turn the second into a 500.
    /// </summary>
    [Fact]
    public async Task Starting_a_draft_twice_returns_the_same_one()
    {
        var factory = CreateFactory();
        var pageId  = await SeedLivePageAsync(factory);

        var first  = await StartDraftAsync(factory, pageId);
        var second = await StartDraftAsync(factory, pageId);

        Assert.Equal(first, second);

        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(1, await db.OrganizationPages.CountAsync(p => p.DraftOfOrganizationPageId == pageId));
    }

    [Fact]
    public async Task An_unpublished_page_is_edited_directly_and_takes_no_draft()
    {
        var factory = CreateFactory();
        var pageId  = await SeedLivePageAsync(factory, published: false);

        var result = await Build(factory).StartDraft(OrgId, pageId, default);
        Assert.IsType<ConflictObjectResult>(result.Result);

        var state = Assert.IsType<CmsDraftStateResponse>(Assert.IsType<OkObjectResult>(
            (await Build(factory).GetState(OrgId, pageId, default)).Result).Value);
        Assert.False(state.NeedsDraft);
    }

    /// <summary>
    /// The editor is routed by whichever page is open and should not need to know in advance which
    /// it has — so asking about a draft's own id answers about the pair.
    /// </summary>
    [Fact]
    public async Task Asking_about_a_draft_answers_about_the_page_it_drafts()
    {
        var factory = CreateFactory();
        var pageId  = await SeedLivePageAsync(factory);
        var draftId = await StartDraftAsync(factory, pageId);

        var result = await Build(factory).GetState(OrgId, draftId, default);
        var state  = Assert.IsType<CmsDraftStateResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Equal(pageId, state.LivePageId);
        Assert.Equal(draftId, state.DraftPageId);
    }

    // ── Publishing ───────────────────────────────────────────────────────────

    /// <summary>
    /// Publishing copies onto the live row. The id survives, which is what keeps every link,
    /// permission row and attached case pointing at the right thing.
    /// </summary>
    [Fact]
    public async Task Publishing_keeps_the_live_pages_id()
    {
        var factory = CreateFactory();
        var pageId  = await SeedLivePageAsync(factory);
        var draftId = await StartDraftAsync(factory, pageId);

        await using (var db = await factory.CreateDbContextAsync())
        {
            var draft = await db.OrganizationPages.SingleAsync(p => p.Id == draftId);
            draft.PageTitle = "About Our Team";
            var section = await db.CmsSections.FirstAsync(s => s.OrganizationPageId == draftId);
            section.Title = "Meet us";
            await db.SaveChangesAsync();
        }

        Assert.IsType<NoContentResult>(await Build(factory).Publish(OrgId, pageId, default));

        var result = await BuildPublic(factory).GetPage("ghost-squad", "about", default);
        var page   = Assert.IsType<OrgPublicPageResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Equal(pageId, page.Page.Id);
        Assert.Equal("About Our Team", page.Page.PageTitle);
        Assert.Contains(page.Page.Sections, s => s.Title == "Meet us");
    }

    [Fact]
    public async Task Publishing_removes_the_draft_and_its_sections()
    {
        var factory = CreateFactory();
        var pageId  = await SeedLivePageAsync(factory);
        var draftId = await StartDraftAsync(factory, pageId);

        await Build(factory).Publish(OrgId, pageId, default);

        await using var db = await factory.CreateDbContextAsync();
        Assert.False(await db.OrganizationPages.AnyAsync(p => p.Id == draftId));
        Assert.False(await db.CmsSections.AnyAsync(s => s.OrganizationPageId == draftId));
    }

    /// <summary>
    /// Publishing a draft must not make an unpublished page live, or drafting a page would be a way
    /// to publish it without meaning to. Those flags belong to the page, not to a draft.
    /// </summary>
    [Fact]
    public async Task Publishing_does_not_change_whether_the_page_is_visible()
    {
        var factory = CreateFactory();
        var pageId  = await SeedLivePageAsync(factory);
        var draftId = await StartDraftAsync(factory, pageId);

        await using (var db = await factory.CreateDbContextAsync())
        {
            // Unpublish the live page after the draft was started.
            var live = await db.OrganizationPages.SingleAsync(p => p.Id == pageId);
            live.IsPublished = false;
            live.IsPublic = false;
            await db.SaveChangesAsync();
        }

        await Build(factory).Publish(OrgId, pageId, default);

        await using var check = await factory.CreateDbContextAsync();
        var after = await check.OrganizationPages.AsNoTracking().SingleAsync(p => p.Id == pageId);
        Assert.False(after.IsPublished);
        Assert.False(after.IsPublic);
        _ = draftId;
    }

    [Fact]
    public async Task Publishing_with_no_draft_is_refused()
    {
        var factory = CreateFactory();
        var pageId  = await SeedLivePageAsync(factory);

        Assert.IsType<ConflictObjectResult>(await Build(factory).Publish(OrgId, pageId, default));
    }

    // ── Discarding ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Discarding_leaves_the_live_page_exactly_as_it_was()
    {
        var factory = CreateFactory();
        var pageId  = await SeedLivePageAsync(factory);
        var draftId = await StartDraftAsync(factory, pageId);

        await using (var db = await factory.CreateDbContextAsync())
        {
            var draft = await db.OrganizationPages.SingleAsync(p => p.Id == draftId);
            draft.PageTitle = "Never to be seen";
            await db.SaveChangesAsync();
        }

        Assert.IsType<NoContentResult>(await Build(factory).Discard(OrgId, pageId, default));

        await using var check = await factory.CreateDbContextAsync();
        Assert.False(await check.OrganizationPages.AnyAsync(p => p.Id == draftId));
        Assert.False(await check.CmsSections.AnyAsync(s => s.OrganizationPageId == draftId));
        Assert.Equal("About Us",
            (await check.OrganizationPages.AsNoTracking().SingleAsync(p => p.Id == pageId)).PageTitle);
    }

    // ── Permissions ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Somebody_without_update_permission_cannot_start_publish_or_discard()
    {
        var factory = CreateFactory();
        var pageId  = await SeedLivePageAsync(factory);
        await StartDraftAsync(factory, pageId);

        var stranger = Build(factory, Guid.NewGuid(), authorized: false);

        Assert.IsType<NotFoundResult>((await stranger.StartDraft(OrgId, pageId, default)).Result);
        Assert.IsType<NotFoundResult>(await stranger.Publish(OrgId, pageId, default));
        Assert.IsType<NotFoundResult>(await stranger.Discard(OrgId, pageId, default));
    }

    [Fact]
    public async Task Another_groups_page_is_not_reachable_through_this_one()
    {
        var factory = CreateFactory();
        await SeedLivePageAsync(factory);

        var otherOrgId = Guid.NewGuid();
        var otherPageId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Organizations.Add(new Organization
            { Id = otherOrgId, Name = "Rivals", UrlName = "rivals", DateCreated = DateTime.UtcNow });
            db.OrganizationPages.Add(new OrganizationPage
            {
                Id = otherPageId, OrganizationId = otherOrgId, PageTitle = "Theirs", UrlName = "theirs",
                PageHtml = "", IsPublished = true, IsPublic = true, DateCreated = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        Assert.IsType<NotFoundResult>((await Build(factory).StartDraft(OrgId, otherPageId, default)).Result);
    }
}
