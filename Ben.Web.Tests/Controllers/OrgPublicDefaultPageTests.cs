using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Public;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>A group with no authored home page gets a page built from what it already keeps (item 205).</summary>
public class OrgPublicDefaultPageTests
{
    private static IDbContextFactory<BenDataContext> Factory()
        => new PooledDbContextFactory<BenDataContext>(new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task No_home_page_means_facts_the_group_already_keeps()
    {
        var f = Factory();
        var me = Guid.NewGuid();
        var org = new Organization { Id = Guid.NewGuid(), Name = "Music City Spirit Seekers", UrlName = "music-city", IsAcceptingClients = true, DateCreated = new DateTime(2026, 8, 1) };
        await using (var db = await f.CreateDbContextAsync())
        {
            db.AppUsers.Add(new AppUser { Id = me, UserName = "a@benco.dev", Email = "a@benco.dev" });
            db.Organizations.Add(org);
            db.OrganizationUserMemberships.AddRange(
                new OrganizationUserMembership { Id = Guid.NewGuid(), OrganizationId = org.Id, AppUserId = me, IsActive = true, Role = OrganizationMemberRole.Owner, CreatedByAppUserId = me },
                new OrganizationUserMembership { Id = Guid.NewGuid(), OrganizationId = org.Id, AppUserId = Guid.NewGuid(), IsActive = true, Role = OrganizationMemberRole.Member, CreatedByAppUserId = me },
                new OrganizationUserMembership { Id = Guid.NewGuid(), OrganizationId = org.Id, AppUserId = Guid.NewGuid(), IsActive = false, Role = OrganizationMemberRole.Member, CreatedByAppUserId = me });
            db.OrganizationAreaOfOperations.Add(new OrganizationAreaOfOperation { Id = Guid.NewGuid(), OrganizationId = org.Id, DisplayLabel = "Middle Tennessee", RadiusMiles = 60, CenterLatitude = 36.16m, CenterLongitude = -86.78m, CreatedByAppUserId = me });
            db.Cases.AddRange(
                new Case { Id = Guid.NewGuid(), OrganizationId = org.Id, Title = "Public one", CaseYear = 2026, OrgCaseNumber = 1, Status = CaseStatus.Public, IsPublic = true, City = "Nashville", DateCaseOpened = DateTime.UtcNow },
                new Case { Id = Guid.NewGuid(), OrganizationId = org.Id, Title = "Private one", CaseYear = 2026, OrgCaseNumber = 2, Status = CaseStatus.Active, IsPublic = false, City = "Nashville", DateCaseOpened = DateTime.UtcNow });
            db.OrgCalendarEvents.AddRange(
                new OrgCalendarEvent { Id = Guid.NewGuid(), OrganizationId = org.Id, Title = "Public night walk", UrlName = "night-walk", IsPublic = true, StartDateTime = DateTime.UtcNow.AddDays(7), EndDateTime = DateTime.UtcNow.AddDays(7).AddHours(2), Location = "Adams, TN", CreatedByAppUserId = me },
                new OrgCalendarEvent { Id = Guid.NewGuid(), OrganizationId = org.Id, Title = "Members only", IsPublic = false, StartDateTime = DateTime.UtcNow.AddDays(3), EndDateTime = DateTime.UtcNow.AddDays(3).AddHours(1), CreatedByAppUserId = me },
                new OrgCalendarEvent { Id = Guid.NewGuid(), OrganizationId = org.Id, Title = "Last month", IsPublic = true, StartDateTime = DateTime.UtcNow.AddDays(-30), EndDateTime = DateTime.UtcNow.AddDays(-30).AddHours(1), CreatedByAppUserId = me });
            await db.SaveChangesAsync();
        }

        var result = await new OrgPublicController(f).GetHome("music-city", default);
        var home = Assert.IsType<OrgPublicHomeResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Null(home.HomePage);
        var facts = Assert.IsType<OrgPublicFacts>(home.Facts);
        Assert.Equal("Middle Tennessee", facts.AreaServed);
        Assert.True(facts.IsAcceptingClients);
        Assert.Equal(2, facts.MemberCount);                    // the inactive one is not a member
        Assert.Equal(2026, facts.OnSinceYear);
        Assert.Equal(1, facts.PublicCaseCount);
        Assert.Equal("Public night walk", facts.NextPublicEvent!.Title); // not the members-only one, not last month's
    }

    [Fact]
    public async Task An_authored_home_page_wins_and_carries_no_facts()
    {
        var f = Factory();
        var me = Guid.NewGuid();
        var org = new Organization { Id = Guid.NewGuid(), Name = "Authors", UrlName = "authors" };
        await using (var db = await f.CreateDbContextAsync())
        {
            db.AppUsers.Add(new AppUser { Id = me, UserName = "b@benco.dev", Email = "b@benco.dev" });
            db.Organizations.Add(org);
            db.OrganizationPages.Add(new OrganizationPage { Id = Guid.NewGuid(), OrganizationId = org.Id, PageTitle = "Home", UrlName = "home", IsHome = true, IsPublished = true, IsPublic = true, CreatedByAppUserId = me });
            await db.SaveChangesAsync();
        }
        var home = Assert.IsType<OrgPublicHomeResponse>(Assert.IsType<OkObjectResult>((await new OrgPublicController(f).GetHome("authors", default)).Result).Value);
        Assert.NotNull(home.HomePage);
        Assert.Null(home.Facts);
    }

    // ── The address switches an owner actually set (2026-09-17 audit) ────────────────────────
    //
    // "Based in {city}" took the first address by Id with no filter, so an owner could mark an
    // address Private — and see a grey "Private" badge confirming it — while the group's public
    // page named that address's city and state anyway.

    private static async Task<(IDbContextFactory<BenDataContext> F, Organization Org)> SeedGroupAsync()
    {
        var f = Factory();
        var me = Guid.NewGuid();
        var org = new Organization
        {
            Id = Guid.NewGuid(), Name = "Address Test Group", UrlName = "address-test",
            DateCreated = new DateTime(2026, 8, 1),
        };
        await using var db = await f.CreateDbContextAsync();
        db.AppUsers.Add(new AppUser { Id = me, UserName = "a@benco.dev", Email = "a@benco.dev" });
        db.Organizations.Add(org);
        await db.SaveChangesAsync();
        return (f, org);
    }

    private static async Task AddAddressAsync(
        IDbContextFactory<BenDataContext> f, Guid orgId,
        OrganizationAddressVisibility visibility,
        OrganizationAddressDisplayMode publicMode = OrganizationAddressDisplayMode.FullAddressAndMap)
    {
        await using var db = await f.CreateDbContextAsync();
        db.OrganizationAddresses.Add(new OrganizationAddress
        {
            Id = Guid.NewGuid(), OrganizationId = orgId,
            StreetAddress1 = "1 Main St", City = "Franklin", State = "TN", ZipCode = "37064",
            Visibility = visibility, PublicDisplayMode = publicMode,
            CreatedByAppUserId = Guid.NewGuid(),
        });
        await db.SaveChangesAsync();
    }

    private static async Task<OrgPublicFacts> FactsAsync(IDbContextFactory<BenDataContext> f, string slug)
    {
        var result = await new OrgPublicController(f).GetHome(slug, default);
        var home = Assert.IsType<OrgPublicHomeResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
        return Assert.IsType<OrgPublicFacts>(home.Facts);
    }

    [Theory]
    [InlineData(OrganizationAddressVisibility.Private)]
    [InlineData(OrganizationAddressVisibility.MembersOnly)]
    [InlineData(OrganizationAddressVisibility.SpecificMembers)]
    public async Task An_address_that_is_not_public_does_not_name_the_city(
        OrganizationAddressVisibility visibility)
    {
        var (f, org) = await SeedGroupAsync();
        await AddAddressAsync(f, org.Id, visibility);

        Assert.Null((await FactsAsync(f, "address-test")).AreaServed);
    }

    [Fact]
    public async Task A_public_address_hidden_from_the_public_page_does_not_name_the_city()
    {
        var (f, org) = await SeedGroupAsync();
        await AddAddressAsync(f, org.Id, OrganizationAddressVisibility.Public,
                              OrganizationAddressDisplayMode.Hidden);

        Assert.Null((await FactsAsync(f, "address-test")).AreaServed);
    }

    [Fact]
    public async Task A_public_address_still_names_the_city()
    {
        var (f, org) = await SeedGroupAsync();
        await AddAddressAsync(f, org.Id, OrganizationAddressVisibility.Public);

        Assert.Equal("Franklin, TN", (await FactsAsync(f, "address-test")).AreaServed);
    }

    /// <summary>
    /// RegionOnly withholds the street and the exact pin, not the region — and a city and state
    /// are the region. So it still answers, which is what the owner asked for.
    /// </summary>
    [Fact]
    public async Task A_region_only_address_still_names_the_city()
    {
        var (f, org) = await SeedGroupAsync();
        await AddAddressAsync(f, org.Id, OrganizationAddressVisibility.Public,
                              OrganizationAddressDisplayMode.RegionOnly);

        Assert.Equal("Franklin, TN", (await FactsAsync(f, "address-test")).AreaServed);
    }
}
