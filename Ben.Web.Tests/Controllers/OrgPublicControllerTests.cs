using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Public;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ben.Web.Tests.Controllers;

public class OrgPublicControllerTests
{
    private static OrgPublicController Build(IDbContextFactory<BenDataContext> factory)
        => new(factory);

    private static async Task<Organization> SeedOrgAsync(IDbContextFactory<BenDataContext> factory, string urlName = "my-org")
    {
        await using var db = await factory.CreateDbContextAsync();
        var org = new Organization { Id = Guid.NewGuid(), Name = "My Org", UrlName = urlName, CreatedByAppUserId = Guid.NewGuid() };
        db.Organizations.Add(org);
        await db.SaveChangesAsync();
        return org;
    }

    private static async Task<OrganizationPage> SeedPageAsync(
        IDbContextFactory<BenDataContext> factory,
        Guid orgId,
        string pageTitle,
        string urlName,
        bool isHome      = false,
        bool isPublished = true,
        bool isPublic    = true,
        int  sortOrder   = 0)
    {
        await using var db = await factory.CreateDbContextAsync();
        var page = new OrganizationPage
        {
            Id                 = Guid.NewGuid(),
            OrganizationId     = orgId,
            PageTitle          = pageTitle,
            UrlName            = urlName,
            IsHome             = isHome,
            IsPublished        = isPublished,
            IsPublic           = isPublic,
            SortOrder          = sortOrder,
            CreatedByAppUserId = Guid.NewGuid(),
        };
        db.OrganizationPages.Add(page);
        await db.SaveChangesAsync();
        return page;
    }

    // ── GetHome ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetHome_UnknownUrlName_Returns404()
    {
        var factory = TestDbFactory.Create();
        var result  = await Build(factory).GetHome("does-not-exist", CancellationToken.None);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetHome_KnownOrg_ReturnsOrgNameAndUrlName()
    {
        var factory = TestDbFactory.Create();
        var org     = await SeedOrgAsync(factory, "my-org");

        var result = await Build(factory).GetHome("my-org", CancellationToken.None);
        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var home   = Assert.IsType<OrgPublicHomeResponse>(ok.Value);

        Assert.Equal(org.Name, home.OrgName);
        Assert.Equal("my-org", home.OrgUrlName);
    }

    [Fact]
    public async Task GetHome_UrlNameIsCaseInsensitive()
    {
        var factory = TestDbFactory.Create();
        await SeedOrgAsync(factory, "my-org");

        var result = await Build(factory).GetHome("MY-ORG", CancellationToken.None);
        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetHome_NoPublishedHomePage_HomePageIsNull()
    {
        var factory = TestDbFactory.Create();
        var org     = await SeedOrgAsync(factory);

        var result = await Build(factory).GetHome("my-org", CancellationToken.None);
        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var home   = Assert.IsType<OrgPublicHomeResponse>(ok.Value);
        Assert.Null(home.HomePage);
    }

    [Fact]
    public async Task GetHome_WithPublishedHomePage_ReturnsHomePage()
    {
        var factory = TestDbFactory.Create();
        var org     = await SeedOrgAsync(factory);
        await SeedPageAsync(factory, org.Id, "Welcome", "home", isHome: true);

        var result = await Build(factory).GetHome("my-org", CancellationToken.None);
        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var home   = Assert.IsType<OrgPublicHomeResponse>(ok.Value);

        Assert.NotNull(home.HomePage);
        Assert.Equal("Welcome", home.HomePage.PageTitle);
    }

    [Fact]
    public async Task GetHome_UnpublishedHomePage_HomePageIsNull()
    {
        var factory = TestDbFactory.Create();
        var org     = await SeedOrgAsync(factory);
        await SeedPageAsync(factory, org.Id, "Draft", "home", isHome: true, isPublished: false);

        var result = await Build(factory).GetHome("my-org", CancellationToken.None);
        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var home   = Assert.IsType<OrgPublicHomeResponse>(ok.Value);
        Assert.Null(home.HomePage);
    }

    [Fact]
    public async Task GetHome_PrivateHomePage_HomePageIsNull()
    {
        var factory = TestDbFactory.Create();
        var org     = await SeedOrgAsync(factory);
        await SeedPageAsync(factory, org.Id, "Members Only", "home", isHome: true, isPublic: false);

        var result = await Build(factory).GetHome("my-org", CancellationToken.None);
        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var home   = Assert.IsType<OrgPublicHomeResponse>(ok.Value);
        Assert.Null(home.HomePage);
    }

    [Fact]
    public async Task GetHome_NavPages_ExcludesHomePageFromNav()
    {
        var factory = TestDbFactory.Create();
        var org     = await SeedOrgAsync(factory);
        await SeedPageAsync(factory, org.Id, "Welcome", "home", isHome: true);
        await SeedPageAsync(factory, org.Id, "About", "about", isHome: false);

        var result  = await Build(factory).GetHome("my-org", CancellationToken.None);
        var ok      = Assert.IsType<OkObjectResult>(result.Result);
        var home    = Assert.IsType<OrgPublicHomeResponse>(ok.Value);

        Assert.NotNull(home.HomePage);
        Assert.DoesNotContain(home.NavPages, p => p.UrlName == "home");
        Assert.Contains(home.NavPages, p => p.UrlName == "about");
    }

    [Fact]
    public async Task GetHome_NavPages_ExcludesUnpublishedAndPrivate()
    {
        var factory = TestDbFactory.Create();
        var org     = await SeedOrgAsync(factory);
        await SeedPageAsync(factory, org.Id, "Secret", "secret", isPublic: false);
        await SeedPageAsync(factory, org.Id, "Draft", "draft", isPublished: false);
        await SeedPageAsync(factory, org.Id, "Public", "public");

        var result = await Build(factory).GetHome("my-org", CancellationToken.None);
        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var home   = Assert.IsType<OrgPublicHomeResponse>(ok.Value);

        var navNames = home.NavPages.Select(p => p.UrlName).ToList();
        Assert.DoesNotContain("secret", navNames);
        Assert.DoesNotContain("draft",  navNames);
        Assert.Contains("public", navNames);
    }

    // ── GetPage ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetPage_UnknownOrg_Returns404()
    {
        var factory = TestDbFactory.Create();
        var result  = await Build(factory).GetPage("unknown-org", "about", CancellationToken.None);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetPage_UnknownSlug_Returns404()
    {
        var factory = TestDbFactory.Create();
        var org     = await SeedOrgAsync(factory);

        var result = await Build(factory).GetPage("my-org", "nonexistent", CancellationToken.None);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetPage_UnpublishedPage_Returns404()
    {
        var factory = TestDbFactory.Create();
        var org     = await SeedOrgAsync(factory);
        await SeedPageAsync(factory, org.Id, "Draft", "draft", isPublished: false);

        var result = await Build(factory).GetPage("my-org", "draft", CancellationToken.None);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetPage_PrivatePage_Returns404()
    {
        var factory = TestDbFactory.Create();
        var org     = await SeedOrgAsync(factory);
        await SeedPageAsync(factory, org.Id, "Members", "members", isPublic: false);

        var result = await Build(factory).GetPage("my-org", "members", CancellationToken.None);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetPage_PublicPublishedPage_ReturnsPageContent()
    {
        var factory = TestDbFactory.Create();
        var org     = await SeedOrgAsync(factory);
        await SeedPageAsync(factory, org.Id, "About Us", "about");

        var result = await Build(factory).GetPage("my-org", "about", CancellationToken.None);
        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var page   = Assert.IsType<OrgPublicPageResponse>(ok.Value);

        Assert.Equal("About Us", page.Page.PageTitle);
        Assert.Equal(org.Name, page.OrgName);
    }

    [Fact]
    public async Task GetPage_SlugIsCaseInsensitive()
    {
        var factory = TestDbFactory.Create();
        var org     = await SeedOrgAsync(factory);
        await SeedPageAsync(factory, org.Id, "About Us", "about");

        var result = await Build(factory).GetPage("my-org", "ABOUT", CancellationToken.None);
        Assert.IsType<OkObjectResult>(result.Result);
    }
}
