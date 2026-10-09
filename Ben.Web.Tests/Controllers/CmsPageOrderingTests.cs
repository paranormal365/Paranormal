using AutoMapper;
using Ben.Data.Common;
using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Cms;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Data.WebApi.Services;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Ben.Web.Website.Library.Organization.Cms;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// The CMS editor's tree (Ben, 10/09/2026): a page's place in the menu, three levels at most, the
/// Ordering window's moves, and the Summary / Intro that now reaches visitors above the sections.
/// </summary>
public sealed class CmsPageOrderingTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid OtherOrgId = Guid.NewGuid();
    private static readonly Guid EditorId = Guid.NewGuid();

    // ── The depth rule (Ben.Data.Common.CmsPageTree), shared by server and editor ──

    private static Dictionary<Guid, Guid?> Chain(out Guid top, out Guid middle, out Guid bottom, out Guid loose)
    {
        top = Guid.NewGuid(); middle = Guid.NewGuid(); bottom = Guid.NewGuid(); loose = Guid.NewGuid();
        return new() { [top] = null, [middle] = top, [bottom] = middle, [loose] = null };
    }

    [Fact]
    public void A_page_may_sit_three_levels_deep_and_no_deeper()
    {
        var map = Chain(out var top, out var middle, out var bottom, out var loose);
        Assert.Null(CmsPageTree.WhyNotPlace(loose, middle, map));            // third level: fine
        Assert.Equal(CmsPageTree.TooDeep, CmsPageTree.WhyNotPlace(loose, bottom, map));   // fourth: no
    }

    [Fact]
    public void What_hangs_under_a_page_moves_with_it_and_counts_toward_the_depth()
    {
        var map = Chain(out var top, out var middle, out var bottom, out var loose);
        // "top" carries two levels under it; put under "loose" it would reach a fourth level.
        Assert.Equal(CmsPageTree.TooDeep, CmsPageTree.WhyNotPlace(top, loose, map));
        Assert.Null(CmsPageTree.WhyNotPlace(middle, loose, map));
    }

    [Fact]
    public void A_page_cannot_go_under_itself_or_its_own_pages()
    {
        var map = Chain(out var top, out var middle, out var bottom, out _);
        Assert.NotNull(CmsPageTree.WhyNotPlace(top, top, map));
        Assert.NotNull(CmsPageTree.WhyNotPlace(top, bottom, map));
        Assert.NotNull(CmsPageTree.WhyNotPlace(top, Guid.NewGuid(), map));   // not one of the group's pages
        Assert.Null(CmsPageTree.WhyNotPlace(bottom, null, map));            // the top level is always open
    }

    // ── The editor's tree (CmsSiteTree) ───────────────────────────────────────

    private static List<CmsSiteTree.Page> Site(out Guid a, out Guid b, out Guid c, out Guid a1)
    {
        a = Guid.NewGuid(); b = Guid.NewGuid(); c = Guid.NewGuid(); a1 = Guid.NewGuid();
        return [new(a, null, 1, "About"), new(b, null, 2, "Cases"), new(c, null, 3, "Contact"), new(a1, a, 1, "Team")];
    }

    [Fact]
    public void Dropping_before_after_or_onto_a_page_places_it_there()
    {
        var site = Site(out var a, out var b, out var c, out var a1);
        Assert.Equal(new CmsSiteTree.Placement(null, 0), CmsSiteTree.WhereDropped(site, c, a, CmsSiteTree.DropPlace.Before));
        Assert.Equal(new CmsSiteTree.Placement(null, 1), CmsSiteTree.WhereDropped(site, c, a, CmsSiteTree.DropPlace.After));
        Assert.Equal(new CmsSiteTree.Placement(a, 1), CmsSiteTree.WhereDropped(site, c, a, CmsSiteTree.DropPlace.Over));
    }

    [Fact]
    public void Applying_a_move_renumbers_both_rows_like_the_server()
    {
        var site = Site(out var a, out var b, out var c, out var a1);
        var moved = CmsSiteTree.Apply(site, b, new(a, 0));
        Assert.Equal([b, a1], CmsSiteTree.Siblings(moved, a).Select(p => p.Id));
        Assert.Equal([1, 2], CmsSiteTree.Siblings(moved, a).Select(p => p.SortOrder));
        Assert.Equal([a, c], CmsSiteTree.Siblings(moved, null).Select(p => p.Id));
        Assert.Equal([1, 2], CmsSiteTree.Siblings(moved, null).Select(p => p.SortOrder));
    }

    [Fact]
    public void The_arrows_move_a_page_up_down_in_and_out()
    {
        var site = Site(out var a, out var b, out var c, out var a1);
        Assert.Null(CmsSiteTree.Nudged(site, a, CmsSiteTree.Nudge.Up));           // already first
        Assert.Equal(new CmsSiteTree.Placement(null, 0), CmsSiteTree.Nudged(site, b, CmsSiteTree.Nudge.Up));
        Assert.Equal(new CmsSiteTree.Placement(a, 1), CmsSiteTree.Nudged(site, b, CmsSiteTree.Nudge.In));   // under About, last
        Assert.Equal(new CmsSiteTree.Placement(null, 1), CmsSiteTree.Nudged(site, a1, CmsSiteTree.Nudge.Out)); // just after About
        Assert.Null(CmsSiteTree.Nudged(site, a, CmsSiteTree.Nudge.In));           // nothing above it to go under
    }

    [Fact]
    public void The_arrows_refuse_a_fourth_level()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var c = Guid.NewGuid(); var d = Guid.NewGuid();
        List<CmsSiteTree.Page> site = [new(a, null, 1, "A"), new(b, a, 1, "B"), new(c, b, 1, "C"), new(d, b, 2, "D")];
        Assert.Null(CmsSiteTree.Nudged(site, d, CmsSiteTree.Nudge.In));   // under C would be a fourth level
    }

    [Fact]
    public void Menu_order_lists_each_page_followed_by_what_is_under_it()
    {
        var site = Site(out var a, out var b, out var c, out var a1);
        var order = CmsSiteTree.InMenuOrder(site);
        Assert.Equal([a, a1, b, c], order.Select(x => x.Page.Id));
        Assert.Equal([0, 1, 0, 0], order.Select(x => x.Depth));
    }

    [Fact]
    public void A_section_moves_only_among_its_own_page()
    {
        var s1 = Guid.NewGuid(); var s2 = Guid.NewGuid(); var s3 = Guid.NewGuid();
        Assert.Equal([s3, s1, s2], CmsSiteTree.SectionsAfterDrop([s1, s2, s3], s3, s1, CmsSiteTree.DropPlace.Before, false));
        Assert.Equal([s2, s1, s3], CmsSiteTree.SectionsAfterDrop([s1, s2, s3], s1, s2, CmsSiteTree.DropPlace.After, false));
        Assert.Equal([s2, s1, s3], CmsSiteTree.SectionsAfterDrop([s1, s2, s3], s2, Guid.NewGuid(), CmsSiteTree.DropPlace.Over, true));
        Assert.Null(CmsSiteTree.SectionsAfterDrop([s1, s2, s3], s1, Guid.NewGuid(), CmsSiteTree.DropPlace.Before, false));
    }

    [Theory]
    [InlineData("About us", "about-us")]
    [InlineData("History of the Property!", "history-of-the-property")]
    [InlineData("  Rooms & Stays  ", "rooms-stays")]
    [InlineData("FAQ 2026", "faq-2026")]
    public void A_title_makes_a_page_address(string title, string slug)
        => Assert.Equal(slug, CmsPageIdeas.SlugFrom(title));

    // ── The server: moving a page ─────────────────────────────────────────────

    private static IDbContextFactory<BenDataContext> CreateFactory()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static Mock<IOrganizationSecurityService> Security(bool authorized = true)
    {
        var s = new Mock<IOrganizationSecurityService>();
        s.Setup(x => x.HasAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid>(),
              It.IsAny<OrganizationSecurityTable>(), It.IsAny<OrganizationSecurityAction>(), It.IsAny<CancellationToken>()))
         .ReturnsAsync(authorized);
        s.Setup(x => x.GetOrganizationsForUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        return s;
    }

    private static ControllerContext Context(Guid? userId) => new()
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(userId is null ? new ClaimsIdentity()
                : new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString())], "Bearer")),
        },
    };

    private static OrgCmsPageController Pages(IDbContextFactory<BenDataContext> f, bool authorized = true)
    {
        var mapper = new Mock<IMapper>();
        mapper.Setup(x => x.Map<IReadOnlyList<CmsSectionRecord>>(It.IsAny<object>())).Returns(Array.Empty<CmsSectionRecord>());
        return new OrgCmsPageController(f, mapper.Object, Security(authorized).Object, new Mock<IAuditLogService>().Object,
            new CmsMarkupSanitizer(), new Ben.Data.WebApi.Services.Billing.SubscriptionLimitGuard(f))
        { ControllerContext = Context(EditorId) };
    }

    private static CmsSectionController Sections(IDbContextFactory<BenDataContext> f)
    {
        var mapper = new Mock<IMapper>();
        return new CmsSectionController(f, mapper.Object, Security().Object, new Mock<IAuditLogService>().Object, new CmsMarkupSanitizer())
        { ControllerContext = Context(EditorId) };
    }

    /// <summary>About (with Team under it), Cases, Contact — and one page belonging to another group.</summary>
    private static async Task<(Guid About, Guid Team, Guid Cases, Guid Contact, Guid Foreign)> SeedSiteAsync(IDbContextFactory<BenDataContext> f)
    {
        await using var db = await f.CreateDbContextAsync();
        db.Organizations.Add(new Organization { Id = OrgId, Name = "Ghost Squad", UrlName = "ghost-squad", DateCreated = DateTime.UtcNow });
        db.Organizations.Add(new Organization { Id = OtherOrgId, Name = "Others", UrlName = "others", DateCreated = DateTime.UtcNow });
        OrganizationPage Page(Guid org, string title, string slug, int sort, Guid? parent = null, string html = "")
            => new()
            {
                Id = Guid.NewGuid(), OrganizationId = org, PageTitle = title, UrlName = slug, PageHtml = html,
                ParentPageId = parent, SortOrder = sort, IsPublished = true, IsPublic = true, DateCreated = DateTime.UtcNow,
            };
        var about = Page(OrgId, "About", "about", 1, html: "<p>We look into old places.</p><script>alert(1)</script>");
        var team = Page(OrgId, "Team", "team", 1, about.Id);
        var cases = Page(OrgId, "Cases", "our-cases", 2);
        var contact = Page(OrgId, "Contact", "contact", 3);
        var foreign = Page(OtherOrgId, "Theirs", "theirs", 1);
        db.OrganizationPages.AddRange(about, team, cases, contact, foreign);
        await db.SaveChangesAsync();
        return (about.Id, team.Id, cases.Id, contact.Id, foreign.Id);
    }

    private static async Task<Dictionary<Guid, (Guid? Parent, int Sort)>> PlacesAsync(IDbContextFactory<BenDataContext> f)
    {
        await using var db = await f.CreateDbContextAsync();
        return await db.OrganizationPages.ToDictionaryAsync(p => p.Id, p => (p.ParentPageId, p.SortOrder));
    }

    [Fact]
    public async Task Moving_a_page_puts_it_at_the_position_and_renumbers_both_rows()
    {
        var f = CreateFactory();
        var site = await SeedSiteAsync(f);

        var result = await Pages(f).Move(OrgId, site.Contact, new MoveCmsPageRequest(site.About, 0), default);
        Assert.IsType<OkObjectResult>(result.Result);

        var places = await PlacesAsync(f);
        Assert.Equal((site.About, 1), places[site.Contact]);
        Assert.Equal((site.About, 2), places[site.Team]);
        Assert.Equal(((Guid?)null, 1), places[site.About]);
        Assert.Equal(((Guid?)null, 2), places[site.Cases]);
    }

    [Fact]
    public async Task Moving_a_page_a_fourth_level_down_is_refused_with_a_sentence()
    {
        var f = CreateFactory();
        var site = await SeedSiteAsync(f);
        // Contact under Team makes three levels; Cases under Contact would be a fourth.
        Assert.IsType<OkObjectResult>((await Pages(f).Move(OrgId, site.Contact, new MoveCmsPageRequest(site.Team, 0), default)).Result);

        var refused = Assert.IsType<BadRequestObjectResult>((await Pages(f).Move(OrgId, site.Cases, new MoveCmsPageRequest(site.Contact, 0), default)).Result);
        Assert.Equal(CmsPageTree.TooDeep, refused.Value);
        Assert.Equal(((Guid?)null, 2), (await PlacesAsync(f))[site.Cases]);
    }

    [Fact]
    public async Task A_page_cannot_be_moved_under_its_own_page_or_another_groups()
    {
        var f = CreateFactory();
        var site = await SeedSiteAsync(f);
        Assert.IsType<BadRequestObjectResult>((await Pages(f).Move(OrgId, site.About, new MoveCmsPageRequest(site.Team, 0), default)).Result);
        Assert.IsType<BadRequestObjectResult>((await Pages(f).Move(OrgId, site.About, new MoveCmsPageRequest(site.Foreign, 0), default)).Result);
        Assert.IsType<NotFoundResult>((await Pages(f).Move(OrgId, site.Foreign, new MoveCmsPageRequest(null, 0), default)).Result);
        Assert.IsType<ForbidResult>((await Pages(f, authorized: false).Move(OrgId, site.Contact, new MoveCmsPageRequest(null, 0), default)).Result);
    }

    [Fact]
    public async Task Saving_a_page_under_a_page_too_deep_is_refused()
    {
        var f = CreateFactory();
        var site = await SeedSiteAsync(f);
        await Pages(f).Move(OrgId, site.Contact, new MoveCmsPageRequest(site.Team, 0), default);

        var update = await Pages(f).Update(OrgId, site.Cases,
            new UpdateCmsPageRequest("Cases", "our-cases", "", true, true, site.Contact, 1), default);
        Assert.IsType<BadRequestObjectResult>(update.Result);

        var create = await Pages(f).Create(OrgId,
            new CreateCmsPageRequest("Deep", "deep", "", true, site.Contact, 1), default);
        Assert.IsType<BadRequestObjectResult>(create.Result);
    }

    [Fact]
    public async Task The_intro_is_cleaned_when_saved()
    {
        var f = CreateFactory();
        await SeedSiteAsync(f);
        var created = await Pages(f).Create(OrgId,
            new CreateCmsPageRequest("Rooms", "rooms", "<p onclick=\"steal()\">Twelve rooms</p><script>alert(1)</script>", true, null, 1), default);
        var page = Assert.IsType<CmsPageDetailResponse>(Assert.IsType<CreatedAtActionResult>(created.Result).Value);
        Assert.Contains("Twelve rooms", page.PageHtml);
        Assert.DoesNotContain("script", page.PageHtml);
        Assert.DoesNotContain("onclick", page.PageHtml);
    }

    [Fact]
    public async Task The_outline_lists_every_page_with_its_sections_and_no_drafts()
    {
        var f = CreateFactory();
        var site = await SeedSiteAsync(f);
        await using (var db = await f.CreateDbContextAsync())
        {
            db.OrganizationPages.Add(new OrganizationPage
            {
                Id = Guid.NewGuid(), OrganizationId = OrgId, PageTitle = "About (draft)", UrlName = "about-draft",
                PageHtml = "", DraftOfOrganizationPageId = site.About, SortOrder = 1, DateCreated = DateTime.UtcNow,
            });
            foreach (var (title, order) in new[] { ("Second", 2), ("First", 1) })
                db.CmsSections.Add(new CmsSection
                {
                    Id = Guid.NewGuid(), OrganizationPageId = site.About, SectionType = CmsSectionType.RichText,
                    Title = title, ContentJson = "{}", SortOrder = order, IsActive = true, DateCreated = DateTime.UtcNow,
                });
            await db.SaveChangesAsync();
        }

        var outline = Assert.IsAssignableFrom<IEnumerable<CmsOutlinePage>>(
            Assert.IsType<OkObjectResult>((await Pages(f).GetOutline(OrgId, default)).Result).Value).ToList();

        Assert.Equal(4, outline.Count);
        Assert.DoesNotContain(outline, p => p.PageTitle.Contains("draft"));
        Assert.Equal(["First", "Second"], outline.Single(p => p.Id == site.About).Sections.Select(s => s.Title));
    }

    // ── The server: sections ──────────────────────────────────────────────────

    [Fact]
    public async Task Reordering_another_groups_sections_is_refused()
    {
        var f = CreateFactory();
        var site = await SeedSiteAsync(f);
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        await using (var db = await f.CreateDbContextAsync())
        {
            db.CmsSections.Add(new CmsSection { Id = first, OrganizationPageId = site.Foreign, SectionType = CmsSectionType.RichText, ContentJson = "{}", SortOrder = 1, DateCreated = DateTime.UtcNow });
            db.CmsSections.Add(new CmsSection { Id = second, OrganizationPageId = site.Foreign, SectionType = CmsSectionType.RichText, ContentJson = "{}", SortOrder = 2, DateCreated = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        // Permission in OUR group, and the other group's page id.
        var result = await Sections(f).Reorder(OrgId, site.Foreign, new ReorderCmsSectionsRequest([second, first]), default);

        Assert.IsType<NotFoundResult>(result);
        await using var check = await f.CreateDbContextAsync();
        Assert.Equal(1, (await check.CmsSections.SingleAsync(s => s.Id == first)).SortOrder);
    }

    [Fact]
    public async Task A_section_preview_is_cleaned_like_the_saved_section_and_saves_nothing()
    {
        var f = CreateFactory();
        var site = await SeedSiteAsync(f);

        var result = await Sections(f).Preview(OrgId, site.About, new CreateCmsSectionRequest(
            CmsSectionType.RichText, " Our history ", """{"html":"<p>Since 1859</p><script>x()</script>"}""", 1, true), default);

        var item = Assert.IsType<OrgPublicSectionItem>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("Our history", item.Title);
        Assert.Contains("Since 1859", item.ContentJson);
        Assert.DoesNotContain("script", item.ContentJson);

        await using var db = await f.CreateDbContextAsync();
        Assert.False(await db.CmsSections.AnyAsync());
        Assert.IsType<NotFoundResult>((await Sections(f).Preview(OrgId, site.Foreign,
            new CreateCmsSectionRequest(CmsSectionType.RichText, null, "{}", 1, true), default)).Result);
    }

    // ── Visitors: the intro above the sections ────────────────────────────────

    [Fact]
    public async Task A_visitor_gets_the_pages_intro_cleaned()
    {
        var f = CreateFactory();
        await SeedSiteAsync(f);

        var result = await new OrgPublicController(f) { ControllerContext = Context(null) }.GetPage("ghost-squad", "about", default);
        var page = Assert.IsType<OrgPublicPageResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);

        // Seeded straight into the table, as a page saved before the intro was cleaned on the way in.
        Assert.Contains("We look into old places.", page.Page.IntroHtml);
        Assert.DoesNotContain("script", page.Page.IntroHtml);
    }

    [Fact]
    public async Task Publishing_a_draft_leaves_the_live_page_where_the_menu_put_it()
    {
        var f = CreateFactory();
        var site = await SeedSiteAsync(f);
        var drafts = new CmsPageDraftController(f, new Mock<IMapper>().Object, Security().Object, new Mock<IAuditLogService>().Object)
        { ControllerContext = Context(EditorId) };

        Assert.IsType<OkObjectResult>((await drafts.StartDraft(OrgId, site.Contact, default)).Result);
        // Moved in the Ordering window while the draft was open.
        await Pages(f).Move(OrgId, site.Contact, new MoveCmsPageRequest(site.About, 0), default);
        Assert.IsType<NoContentResult>(await drafts.Publish(OrgId, site.Contact, default));

        Assert.Equal((site.About, 1), (await PlacesAsync(f))[site.Contact]);
    }

    // ── The sanitizer keeps what the editor's new buttons put in ─────────────

    [Fact]
    public void Links_to_the_groups_own_pages_and_library_pictures_survive_cleaning()
    {
        var clean = new CmsMarkupSanitizer().SanitizeHtml(
            """<p><a href="/o/ghost-squad/about">About us</a></p><img src="https://api.ishaunted.com/api/upload-files/1b4e28ba-2fa1-11d2-883f-0016d3cca427/download" alt="Fountain" />""");
        Assert.Contains("href=\"/o/ghost-squad/about\"", clean);
        Assert.Contains("src=\"https://api.ishaunted.com/api/upload-files/", clean);
    }
}
