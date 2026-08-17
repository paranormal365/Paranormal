using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Published investigations at their own shareable address (backlog item #89).
/// </summary>
/// <remarks>
/// Two things carry weight. Visibility goes through the <b>shared</b> filter rather than a second
/// copy of the rule, so a group-only investigation is unreachable here for the same reason it is
/// unreachable on a place page. And the location is <b>approximate</b> — a published account says a
/// group was somewhere, not precisely where.
/// </remarks>
public sealed class PublicInvestigationControllerTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private sealed record World(IDbContextFactory<BenDataContext> Factory, Guid PublicId, Guid GroupOnlyId);

    private static IDbContextFactory<BenDataContext> CreateFactory()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static PublicInvestigationController Build(IDbContextFactory<BenDataContext> f)
        => new(f)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) }
            }
        };

    private static async Task<World> SeedAsync()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();

        db.Users.Add(new AppUser
        { Id = UserId, UserName = "u@t", Email = "u@t", DisplayName = "A Member", DateCreated = DateTime.UtcNow });

        db.Organizations.Add(new Organization
        { Id = OrgId, Name = "Ghost Squad", UrlName = "ghost-squad", DateCreated = DateTime.UtcNow, CreatedByAppUserId = UserId });

        var placeId = Guid.NewGuid();
        db.Places.Add(new Place
        {
            Id = placeId, Name = "The Old Mill", Kind = PlaceKind.PublicLocation,
            StreetAddress1 = "12 Elm Street", City = "Nashville", State = "TN",
            Latitude = 36.1627m, Longitude = -86.7816m,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = UserId,
        });

        Guid AddInvestigation(string title, string? slug, InvestigationVisibility visibility)
        {
            var id = Guid.NewGuid();
            db.Investigations.Add(new Investigation
            {
                Id = id, OrganizationId = OrgId, PlaceId = placeId,
                Title = title, UrlName = slug,
                Notes = "We heard three knocks.",
                ScheduledDateTime = new DateTime(2026, 8, 24, 21, 0, 0, DateTimeKind.Utc),
                Status = InvestigationStatus.Completed,
                Visibility = visibility,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = UserId,
            });
            return id;
        }

        var publicId    = AddInvestigation("The Mill at Night", "2026-08-24-the-mill-at-night", InvestigationVisibility.Public);
        var groupOnlyId = AddInvestigation("Quiet night", "2026-08-25-quiet-night", InvestigationVisibility.GroupOnly);

        await db.SaveChangesAsync();
        return new World(factory, publicId, groupOnlyId);
    }

    [Fact]
    public async Task A_published_investigation_opens_at_its_address()
    {
        var w = await SeedAsync();

        var result = await Build(w.Factory)
            .GetPublished("ghost-squad", "2026-08-24-the-mill-at-night", default);
        var detail = Assert.IsType<PublicInvestigationDetail>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Equal(w.PublicId, detail.Id);
        Assert.Equal("We heard three knocks.", detail.Notes);
    }

    /// <summary>
    /// A group-only investigation is not reachable, and it is unreachable through the same shared
    /// predicate the place page uses — not a second copy of the rule that could drift.
    /// </summary>
    [Fact]
    public async Task A_group_only_investigation_is_not_published()
    {
        var w = await SeedAsync();

        Assert.IsType<NotFoundResult>((await Build(w.Factory)
            .GetPublished("ghost-squad", "2026-08-25-quiet-night", default)).Result);

        var list = await Build(w.Factory).GetPublished("ghost-squad", default);
        var rows = Assert.IsAssignableFrom<IReadOnlyList<PublicInvestigationListItem>>(
            Assert.IsType<OkObjectResult>(list.Result).Value);

        Assert.DoesNotContain(rows, r => r.Id == w.GroupOnlyId);
    }

    /// <summary>
    /// The location is published to within a few miles. A write-up says a group was somewhere, not
    /// which door they knocked on.
    /// </summary>
    [Fact]
    public async Task The_location_is_approximate()
    {
        var w = await SeedAsync();

        var result = await Build(w.Factory)
            .GetPublished("ghost-squad", "2026-08-24-the-mill-at-night", default);
        var detail = Assert.IsType<PublicInvestigationDetail>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.NotNull(detail.ApproximateLatitude);
        Assert.NotEqual(36.1627m, detail.ApproximateLatitude);
        Assert.Equal("Nashville", detail.City);
    }

    /// <summary>
    /// Everything listed can be opened. A list whose rows link nowhere is the shape this codebase
    /// keeps shipping.
    /// </summary>
    [Fact]
    public async Task Everything_listed_can_be_opened()
    {
        var w = await SeedAsync();

        var list = await Build(w.Factory).GetPublished("ghost-squad", default);
        var rows = Assert.IsAssignableFrom<IReadOnlyList<PublicInvestigationListItem>>(
            Assert.IsType<OkObjectResult>(list.Result).Value);

        Assert.NotEmpty(rows);

        foreach (var row in rows)
        {
            Assert.False(string.IsNullOrWhiteSpace(row.UrlName), $"'{row.Title}' links nowhere.");
            Assert.IsType<OkObjectResult>(
                (await Build(w.Factory).GetPublished("ghost-squad", row.UrlName!, default)).Result);
        }
    }

    [Fact]
    public async Task Another_organizations_address_does_not_resolve_here()
    {
        var w = await SeedAsync();

        Assert.IsType<NotFoundResult>((await Build(w.Factory)
            .GetPublished("some-other-group", "2026-08-24-the-mill-at-night", default)).Result);
    }
}
