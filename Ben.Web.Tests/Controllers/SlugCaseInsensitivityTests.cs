using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Moq;
using AutoMapper;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// A shared link works whatever case somebody typed or a mail client mangled it.
/// </summary>
/// <remarks>
/// <para>These are the <b>positive</b> half of the slug work. The guards elsewhere prove a bad
/// address is refused; these prove every good one resolves — through every public route, in the
/// casing a real link arrives in.</para>
///
/// <para>The bug they were written for: organizations were created through two paths that
/// normalized differently, and readers variously lowercased the incoming segment or did not. On SQL
/// Server's default case-insensitive collation that mostly worked, which is the dangerous part — the
/// behaviour was right by accident of database configuration rather than by anything in the code.
/// The InMemory provider used here is case-<i>sensitive</i>, so these tests see what a
/// case-sensitive collation would.</para>
/// </remarks>
public sealed class SlugCaseInsensitivityTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private static IDbContextFactory<BenDataContext> CreateFactory()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static ControllerContext Anonymous() => new()
    {
        HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) }
    };

    /// <summary>One organization with a published case, investigation, event and CMS page.</summary>
    private static async Task<IDbContextFactory<BenDataContext>> SeedAsync()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();

        db.Users.Add(new AppUser
        { Id = UserId, UserName = "u@t", Email = "u@t", DisplayName = "A Member", DateCreated = DateTime.UtcNow });

        // Stored lowercase, as every write path now normalizes it.
        db.Organizations.Add(new Organization
        { Id = OrgId, Name = "Ghost Squad", UrlName = "ghost-squad", DateCreated = DateTime.UtcNow, CreatedByAppUserId = UserId });

        var placeId = Guid.NewGuid();
        db.Places.Add(new Place
        {
            Id = placeId, Name = "The Old Mill", Kind = PlaceKind.PublicLocation,
            City = "Nashville", State = "TN", Latitude = 36.1627m, Longitude = -86.7816m,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = UserId,
        });

        db.Cases.Add(new Case
        {
            Id = Guid.NewGuid(), OrganizationId = OrgId, Title = "The Mill House",
            UrlName = "the-mill-house", City = "Nashville", State = "TN", Country = "US",
            CaseYear = 2026, OrgCaseNumber = 1,
            Status = CaseStatus.Public, IsPublic = true,
            DateCaseOpened = DateTime.UtcNow, DateCreated = DateTime.UtcNow, CreatedByAppUserId = UserId,
        });

        db.Investigations.Add(new Investigation
        {
            Id = Guid.NewGuid(), OrganizationId = OrgId, PlaceId = placeId,
            Title = "The Mill at Night", UrlName = "2026-08-24-the-mill-at-night",
            ScheduledDateTime = new DateTime(2026, 8, 24, 21, 0, 0, DateTimeKind.Utc),
            Status = InvestigationStatus.Completed, Visibility = InvestigationVisibility.Public,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = UserId,
        });

        db.OrgCalendarEvents.Add(new OrgCalendarEvent
        {
            Id = Guid.NewGuid(), OrganizationId = OrgId, Title = "Ghost Walk",
            UrlName = "2026-08-24-ghost-walk", PlaceId = placeId,
            StartDateTime = DateTime.UtcNow.AddDays(7), EndDateTime = DateTime.UtcNow.AddDays(7).AddHours(3),
            IsPublic = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = UserId,
        });

        db.OrganizationPages.Add(new OrganizationPage
        {
            Id = Guid.NewGuid(), OrganizationId = OrgId, PageTitle = "About Us", UrlName = "about-us",
            PageHtml = "", IsPublished = true, IsPublic = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = UserId,
        });

        await db.SaveChangesAsync();
        return factory;
    }

    /// <summary>The casings a link genuinely arrives in — typed, shouted, or auto-capitalised.</summary>
    public static TheoryData<string> OrgCasings() => new() { "ghost-squad", "Ghost-Squad", "GHOST-SQUAD", "  Ghost-Squad  " };

    [Theory]
    [MemberData(nameof(OrgCasings))]
    public async Task An_organizations_home_page_resolves(string orgSegment)
    {
        var factory = await SeedAsync();
        var ctrl = new OrgPublicController(factory) { ControllerContext = Anonymous() };

        Assert.IsType<OkObjectResult>((await ctrl.GetHome(orgSegment, default)).Result);
    }

    [Theory]
    [MemberData(nameof(OrgCasings))]
    public async Task A_cms_page_resolves(string orgSegment)
    {
        var factory = await SeedAsync();
        var ctrl = new OrgPublicController(factory) { ControllerContext = Anonymous() };

        Assert.IsType<OkObjectResult>((await ctrl.GetPage(orgSegment, "About-Us", default)).Result);
    }

    [Theory]
    [MemberData(nameof(OrgCasings))]
    public async Task A_published_case_resolves(string orgSegment)
    {
        var factory = await SeedAsync();
        var ctrl = new PublicCaseController(factory, Mock.Of<IMapper>()) { ControllerContext = Anonymous() };

        Assert.IsType<OkObjectResult>((await ctrl.GetPublicCase(orgSegment, "The-Mill-House", default)).Result);
    }

    [Theory]
    [MemberData(nameof(OrgCasings))]
    public async Task A_published_investigation_resolves(string orgSegment)
    {
        var factory = await SeedAsync();
        var ctrl = new PublicInvestigationController(factory) { ControllerContext = Anonymous() };

        Assert.IsType<OkObjectResult>(
            (await ctrl.GetPublished(orgSegment, "2026-08-24-The-Mill-At-Night", default)).Result);
    }

    [Theory]
    [MemberData(nameof(OrgCasings))]
    public async Task A_public_event_resolves(string orgSegment)
    {
        var factory = await SeedAsync();
        var ctrl = new PublicEventController(factory, new Ben.Data.WebApi.Services.CmsMarkupSanitizer()) { ControllerContext = Anonymous() };

        Assert.IsType<OkObjectResult>(
            (await ctrl.GetEventBySlug(orgSegment, "2026-08-24-GHOST-WALK", default)).Result);
    }

    /// <summary>
    /// And the list route, which narrows by organization rather than resolving one thing.
    /// </summary>
    [Theory]
    [MemberData(nameof(OrgCasings))]
    public async Task The_events_list_for_an_organization_resolves(string orgSegment)
    {
        var factory = await SeedAsync();
        var ctrl = new PublicEventController(factory, new Ben.Data.WebApi.Services.CmsMarkupSanitizer()) { ControllerContext = Anonymous() };

        var result = await ctrl.GetUpcoming(orgSegment, 50, default);
        var items = Assert.IsAssignableFrom<IReadOnlyList<PublicEventListItem>>(
            Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Single(items);
    }
}
