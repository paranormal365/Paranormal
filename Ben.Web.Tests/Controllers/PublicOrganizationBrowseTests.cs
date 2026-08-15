using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Public;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Tests for the location-free organization browse listing.
/// </summary>
/// <remarks>
/// The proximity search beside it requires coordinates and skips any organization without an area
/// of operation, which meant the site's own "Browse All Groups" button led to a page that showed
/// nothing until you typed a location. These cover the listing that fixes that.
/// </remarks>
public class PublicOrganizationBrowseTests
{
    private static PublicOrganizationSearchController BuildController(
        IDbContextFactory<BenDataContext> factory) => new(factory);

    private static Organization MakeOrg(string name, bool acceptingClients) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        UrlName = name.ToLowerInvariant().Replace(" ", "-"),
        IsAcceptingClients = acceptingClients,
        DateCreated = DateTime.UtcNow,
    };

    private static OrgBrowsePage Result(ActionResult<OrgBrowsePage> result)
        => Assert.IsType<OrgBrowsePage>(Assert.IsType<OkObjectResult>(result.Result).Value);

    [Fact]
    public async Task Lists_a_group_that_has_no_area_of_operation()
    {
        // The exact blind spot in the proximity search: no area configured means invisible.
        var factory = TestDbFactory.Create();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Organizations.Add(MakeOrg("Nowhere Paranormal", acceptingClients: true));
            await db.SaveChangesAsync();
        }

        var page = Result(await BuildController(factory).Browse(ct: default));

        var only = Assert.Single(page.Items);
        Assert.Equal("Nowhere Paranormal", only.Name);
        Assert.Null(only.AreaLabel);
        Assert.Null(only.RadiusMiles);
    }

    [Fact]
    public async Task Groups_accepting_clients_come_first_then_alphabetical()
    {
        var factory = TestDbFactory.Create();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Organizations.AddRange(
                MakeOrg("Zeta Society", acceptingClients: true),
                MakeOrg("Alpha Group", acceptingClients: false),
                MakeOrg("Beta Circle", acceptingClients: true));
            await db.SaveChangesAsync();
        }

        var page = Result(await BuildController(factory).Browse(ct: default));

        Assert.Equal(
            ["Beta Circle", "Zeta Society", "Alpha Group"],
            page.Items.Select(i => i.Name));
    }

    [Fact]
    public async Task Paging_reports_the_full_total_and_returns_every_group_across_pages()
    {
        var factory = TestDbFactory.Create();
        await using (var db = await factory.CreateDbContextAsync())
        {
            for (var i = 0; i < 7; i++)
                db.Organizations.Add(MakeOrg($"Group {i:D2}", acceptingClients: true));
            await db.SaveChangesAsync();
        }

        var controller = BuildController(factory);
        var first = Result(await controller.Browse(page: 1, pageSize: 3, ct: default));
        var second = Result(await controller.Browse(page: 2, pageSize: 3, ct: default));
        var third = Result(await controller.Browse(page: 3, pageSize: 3, ct: default));

        Assert.Equal(7, first.TotalCount);
        Assert.Equal(3, first.Items.Count);
        Assert.Equal(3, second.Items.Count);
        Assert.Single(third.Items);

        // No duplicates and nothing missed — the tiebreaker on Id is what makes this hold.
        var all = first.Items.Concat(second.Items).Concat(third.Items).Select(i => i.Name).ToList();
        Assert.Equal(7, all.Distinct().Count());
    }

    [Theory]
    [InlineData(0, 1)]      // page below the floor
    [InlineData(-5, 1)]
    public async Task Page_below_one_is_treated_as_the_first_page(int requested, int expected)
    {
        var factory = TestDbFactory.Create();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Organizations.Add(MakeOrg("Only Group", acceptingClients: true));
            await db.SaveChangesAsync();
        }

        var page = Result(await BuildController(factory).Browse(page: requested, ct: default));

        Assert.Equal(expected, page.Page);
        Assert.Single(page.Items);
    }

    [Fact]
    public async Task Page_size_is_clamped_so_a_caller_cannot_ask_for_the_whole_table()
    {
        var factory = TestDbFactory.Create();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Organizations.Add(MakeOrg("Only Group", acceptingClients: true));
            await db.SaveChangesAsync();
        }

        var huge = Result(await BuildController(factory).Browse(pageSize: 5000, ct: default));
        var zero = Result(await BuildController(factory).Browse(pageSize: 0, ct: default));

        Assert.Equal(100, huge.PageSize);
        Assert.Equal(1, zero.PageSize);
    }

    [Fact]
    public void Browse_result_carries_no_coordinates()
    {
        // The search endpoint's contract is that centre coordinates never leave the server. This
        // listing sits next to it and reads the same table, so it inherits the same promise —
        // asserted structurally rather than trusted to code review of a future edit.
        var names = typeof(OrgBrowseResult).GetProperties().Select(p => p.Name).ToList();

        Assert.DoesNotContain(names, n => n.Contains("Latitude", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, n => n.Contains("Longitude", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, n => n.Contains("Center", StringComparison.OrdinalIgnoreCase));
    }
}
