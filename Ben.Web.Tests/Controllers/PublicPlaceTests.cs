using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Public;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Ben.Data.WebApi.Services.Access;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// The place page as a visitor sees it.
/// </summary>
/// <remarks>
/// The anonymous surface is the one where a sharing mistake is worst, so these tests are mostly
/// about what must <i>not</i> come back. The endpoint runs the same
/// <c>InvestigationVisibilityFilter</c> as the signed-in one, passed no organizations — the point
/// being that there is no second copy of the rules to fall out of step.
/// </remarks>
public class PublicPlaceTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid PlaceId = Guid.NewGuid();

    private static PublicPlaceController Build(IDbContextFactory<BenDataContext> f)
        => new(f)
        {
            // No user at all, as a visitor.
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

    private static async Task<IDbContextFactory<BenDataContext>> SeedAsync(
        PlaceKind kind = PlaceKind.PublicLocation, decimal? lat = 36.5893m, decimal? lon = -87.0625m)
    {
        var factory = TestDbFactory.Create();
        await using var db = await factory.CreateDbContextAsync();

        db.Organizations.Add(new Organization
        { Id = OrgId, Name = "Tennessee Ghost Hunters", UrlName = "tgh", DateCreated = DateTime.UtcNow });
        db.Places.Add(new Place
        {
            Id = PlaceId, Name = "Bell Witch Cave", City = "Adams", State = "TN",
            Latitude = lat, Longitude = lon, Kind = kind,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid(),
        });
        await db.SaveChangesAsync();
        return factory;
    }

    private static async Task AddAsync(
        IDbContextFactory<BenDataContext> f, string title, InvestigationVisibility visibility, int yearsAgo = 1)
    {
        await using var db = await f.CreateDbContextAsync();
        db.Investigations.Add(new Investigation
        {
            Id = Guid.NewGuid(), OrganizationId = OrgId, PlaceId = PlaceId,
            Title = title, Visibility = visibility,
            ScheduledDateTime = DateTime.UtcNow.AddYears(-yearsAgo),
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid(),
        });
        await db.SaveChangesAsync();
    }

    private static async Task<PublicPlaceResponse> GetAsync(IDbContextFactory<BenDataContext> f)
    {
        var result = await Build(f).GetById(PlaceId, default);
        return Assert.IsType<PublicPlaceResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
    }

    // ── What a visitor must not see ───────────────────────────────────────────

    [Fact]
    public async Task Only_published_investigations_come_back()
    {
        var f = await SeedAsync();
        await AddAsync(f, "Published", InvestigationVisibility.Public);
        await AddAsync(f, "Group only", InvestigationVisibility.GroupOnly);
        await AddAsync(f, "Shared with fellow investigators", InvestigationVisibility.PlaceInvestigators);

        var response = await GetAsync(f);

        // PlaceInvestigators must not leak to an anonymous caller: they have investigated nowhere,
        // so they never qualify for that audience however it is worded.
        Assert.Equal("Published", Assert.Single(response.Investigations).Title);
    }

    [Fact]
    public async Task A_place_with_nothing_published_still_loads()
    {
        var f = await SeedAsync();
        await AddAsync(f, "Group only", InvestigationVisibility.GroupOnly);

        var response = await GetAsync(f);

        // The place exists and is worth showing; it simply has no published history. A 404 here
        // would tell a visitor the place is not real, which is a different and wrong statement.
        Assert.Equal("Bell Witch Cave", response.Place.Name);
        Assert.Empty(response.Investigations);
    }

    [Fact]
    public async Task An_unknown_place_is_not_found()
    {
        var f = await SeedAsync();

        Assert.IsType<NotFoundResult>((await Build(f).GetById(Guid.NewGuid(), default)).Result);
    }

    // ── The summary ───────────────────────────────────────────────────────────

    [Fact]
    public async Task The_summary_counts_only_what_is_visible()
    {
        var f = await SeedAsync();
        await AddAsync(f, "Published one", InvestigationVisibility.Public, yearsAgo: 5);
        await AddAsync(f, "Published two", InvestigationVisibility.Public, yearsAgo: 1);
        await AddAsync(f, "Hidden", InvestigationVisibility.GroupOnly, yearsAgo: 9);

        var summary = (await GetAsync(f)).Summary;

        // Counting everything would quietly tell the visitor how much is being withheld — and the
        // "since" year would give away the date of a visit they cannot see.
        Assert.Equal(2, summary.InvestigationCount);
        Assert.Equal(1, summary.OrganizationCount);
        Assert.Equal(DateTime.UtcNow.AddYears(-5).Year, summary.Since);
    }

    [Fact]
    public async Task The_summary_reports_no_year_when_there_is_nothing_to_show()
    {
        var f = await SeedAsync();

        var summary = (await GetAsync(f)).Summary;

        // Null rather than 0 or this year, so the page can drop the phrase instead of printing
        // something meaningless.
        Assert.Equal(0, summary.InvestigationCount);
        Assert.Null(summary.Since);
    }

    [Fact]
    public async Task Groups_are_counted_once_however_many_visits_they_made()
    {
        var f = await SeedAsync();
        await AddAsync(f, "First", InvestigationVisibility.Public, yearsAgo: 3);
        await AddAsync(f, "Second", InvestigationVisibility.Public, yearsAgo: 2);

        Assert.Equal(1, (await GetAsync(f)).Summary.OrganizationCount);
    }

    // ── What it carries ───────────────────────────────────────────────────────

    [Fact]
    public async Task The_row_carries_the_groups_public_url_name()
    {
        var f = await SeedAsync();
        await AddAsync(f, "Published", InvestigationVisibility.Public);

        var row = Assert.Single((await GetAsync(f)).Investigations);

        // So the page can link to the group's public page without a second lookup.
        Assert.Equal("Tennessee Ghost Hunters", row.OrganizationName);
        Assert.Equal("tgh", row.OrganizationUrlName);
    }

    [Fact]
    public async Task A_place_with_no_coordinates_still_loads()
    {
        var f = await SeedAsync(lat: null, lon: null);
        await AddAsync(f, "Published", InvestigationVisibility.Public);

        var response = await GetAsync(f);

        // The page drops the map rather than failing; an unplaceable place is still a place.
        Assert.Null(response.Place.Latitude);
        Assert.Single(response.Investigations);
    }
}
