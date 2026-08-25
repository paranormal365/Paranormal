using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Public;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Ghost walking tours as a kind of group (2026-08-24): the defaults a new one starts with,
/// and the finder that has to be able to single them out.
/// </summary>
/// <remarks>
/// Two claims. First, <b>the kind decides defaults and nothing else</b> — a tour is public by
/// default because that is what a tour is, but nothing is withheld from either kind and every
/// value stays changeable. Second, <b>the finder filters on the CAPABILITY, not the kind</b>,
/// so an investigation group that also runs tours is findable by somebody looking for a tour
/// without having to pretend to be a tour company.
/// </remarks>
public sealed class OrganizationKindTests
{
    // ── The defaults ────────────────────────────────────────────────────────

    [Fact]
    public void A_tour_starts_public_and_a_group_starts_private()
    {
        var tour = OrganizationKindDefaults.AddressDefaults(OrganizationKind.GhostWalkingTour);
        Assert.Equal(OrganizationAddressVisibility.Public, tour.Visibility);
        Assert.Equal(OrganizationAddressDisplayMode.FullAddressAndMap, tour.PublicDisplayMode);
        Assert.True(tour.IsSearchable);

        // The investigation-group answer is the PRE-EXISTING one, unchanged. A group's
        // headquarters is often somebody's home; this feature must not have moved it.
        var group = OrganizationKindDefaults.AddressDefaults(OrganizationKind.InvestigationGroup);
        Assert.Equal(OrganizationAddressVisibility.Private, group.Visibility);
        Assert.Equal(OrganizationAddressDisplayMode.Hidden, group.PublicDisplayMode);
    }

    [Fact]
    public void Events_default_public_for_a_tour_and_private_for_a_group()
    {
        Assert.True(OrganizationKindDefaults.EventsArePublicByDefault(OrganizationKind.GhostWalkingTour));
        Assert.False(OrganizationKindDefaults.EventsArePublicByDefault(OrganizationKind.InvestigationGroup));
    }

    [Fact]
    public void A_tour_runs_tours_by_definition_and_a_group_does_not_until_it_says_so()
    {
        Assert.True(OrganizationKindDefaults.RunsPublicTours(OrganizationKind.GhostWalkingTour));
        Assert.False(OrganizationKindDefaults.RunsPublicTours(OrganizationKind.InvestigationGroup));
    }

    [Fact]
    public void Investigation_group_is_zero_so_every_pre_existing_organization_is_one()
    {
        // The column defaults to 0 on a table full of rows written before this existed, and
        // every one of those rows IS an investigation group. Getting this backwards would
        // silently reclassify the entire site.
        Assert.Equal(0, (int)OrganizationKind.InvestigationGroup);
    }

    [Fact]
    public void Both_kinds_are_named_for_a_person()
    {
        Assert.Equal("Ghost walking tour", OrganizationKindDefaults.DisplayName(OrganizationKind.GhostWalkingTour));
        Assert.Equal("Investigation group", OrganizationKindDefaults.DisplayName(OrganizationKind.InvestigationGroup));
    }

    // ── The finder ──────────────────────────────────────────────────────────

    private sealed class SimpleFactory(DbContextOptions<BenDataContext> opts) : IDbContextFactory<BenDataContext>
    {
        public BenDataContext CreateDbContext() => new(opts);
        public Task<BenDataContext> CreateDbContextAsync(CancellationToken ct = default)
            => Task.FromResult(new BenDataContext(opts));
    }

    private static async Task<IDbContextFactory<BenDataContext>> SeedAsync()
    {
        var factory = new SimpleFactory(new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await using var db = await ((IDbContextFactory<BenDataContext>)factory).CreateDbContextAsync();

        db.Organizations.AddRange(
            new Organization
            {
                Id = Guid.NewGuid(), Name = "Tour Co", UrlName = "tour-co",
                Kind = OrganizationKind.GhostWalkingTour, RunsPublicTours = true,
                DateCreated = DateTime.UtcNow,
            },
            new Organization
            {
                Id = Guid.NewGuid(), Name = "Plain Investigators", UrlName = "plain",
                Kind = OrganizationKind.InvestigationGroup, RunsPublicTours = false,
                DateCreated = DateTime.UtcNow,
            },
            // The case the capability exists for: an investigation group that ALSO runs tours.
            new Organization
            {
                Id = Guid.NewGuid(), Name = "Both Of Them", UrlName = "both",
                Kind = OrganizationKind.InvestigationGroup, RunsPublicTours = true,
                DateCreated = DateTime.UtcNow,
            });
        await db.SaveChangesAsync();
        return factory;
    }

    [Fact]
    public async Task Browsing_everyone_returns_every_group()
    {
        var controller = new PublicOrganizationSearchController(await SeedAsync());
        var result = await controller.Browse(page: 1, pageSize: 24, toursOnly: false, default);
        var page = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(result.Result).Value as OrgBrowsePage;
        Assert.Equal(3, page!.TotalCount);
    }

    [Fact]
    public async Task Filtering_to_tours_finds_the_tour_company_AND_the_group_that_runs_tours()
    {
        var controller = new PublicOrganizationSearchController(await SeedAsync());
        var result = await controller.Browse(page: 1, pageSize: 24, toursOnly: true, default);
        var page = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(result.Result).Value as OrgBrowsePage;

        Assert.Equal(2, page!.TotalCount);
        Assert.Contains(page.Items, o => o.Name == "Tour Co");
        // The whole reason the capability is separate from the kind: this group does both,
        // and a visitor looking for a tour should find it without it lying about what it is.
        Assert.Contains(page.Items, o => o.Name == "Both Of Them");
        Assert.DoesNotContain(page.Items, o => o.Name == "Plain Investigators");
    }

    [Fact]
    public async Task The_browse_card_carries_the_kind_so_it_can_be_badged()
    {
        var controller = new PublicOrganizationSearchController(await SeedAsync());
        var result = await controller.Browse(page: 1, pageSize: 24, toursOnly: true, default);
        var page = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(result.Result).Value as OrgBrowsePage;

        var tour = page!.Items.Single(o => o.Name == "Tour Co");
        Assert.Equal(OrganizationKind.GhostWalkingTour, tour.Kind);

        var both = page.Items.Single(o => o.Name == "Both Of Them");
        // Still an investigation group — the badge tells the truth about what it mainly is,
        // even though it appears in a tour search.
        Assert.Equal(OrganizationKind.InvestigationGroup, both.Kind);
        Assert.True(both.RunsPublicTours);
    }
}
