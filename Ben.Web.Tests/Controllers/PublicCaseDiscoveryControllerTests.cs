using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Public;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Covers the fix removing per-request external geocoding from this unauthenticated,
/// public endpoint — it must now surface whatever coordinates are already stored on the
/// Case, never call out to a geocoding service on the request path.
/// </summary>
public class PublicCaseDiscoveryControllerTests
{
    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var opts = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PooledDbContextFactory<BenDataContext>(opts);
    }

    private static PublicCaseDiscoveryController Build(IDbContextFactory<BenDataContext> factory)
        => new(factory);

    private static Organization MakeOrg() => new()
    {
        Id = Guid.NewGuid(), Name = "Test Org", UrlName = $"org-{Guid.NewGuid():N}",
        DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid(),
    };

    private static Case MakeCase(Guid orgId, string title, decimal? lat = null, decimal? lon = null,
        bool isPublic = true, CaseStatus status = CaseStatus.Public) => new()
    {
        Id = Guid.NewGuid(), OrganizationId = orgId, Title = title,
        CaseYear = 2026, OrgCaseNumber = 1,
        StreetAddress1 = "1 Main St", City = "Nashville", State = "TN", ZipCode = "37201", Country = "US",
        Latitude = lat, Longitude = lon,
        IsPublic = isPublic, Status = status,
        DateCaseOpened = DateTime.UtcNow, DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid(),
    };

    [Fact]
    public async Task GetAll_ReturnsStoredCoordinates_WithoutGeocoding()
    {
        var factory = CreateFactory();
        var org     = MakeOrg();
        var c       = MakeCase(org.Id, "Haunted House", lat: 36.16m, lon: -86.78m);
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Organizations.Add(org);
            db.Cases.Add(c);
            await db.SaveChangesAsync();
        }
        var ctrl = Build(factory);

        var result = await ctrl.GetAll(ct: default);

        var ok   = Assert.IsType<OkObjectResult>(result.Result);
        var body = Assert.IsType<PublicCaseDiscoveryPagedResponse>(ok.Value);
        var item = Assert.Single(body.Items);
        Assert.Equal(36.16m, item.ApproxLatitude);
        Assert.Equal(-86.78m, item.ApproxLongitude);
    }

    [Fact]
    public async Task GetAll_OmitsCoordinates_WhenCaseHasNoneStored()
    {
        var factory = CreateFactory();
        var org     = MakeOrg();
        var c       = MakeCase(org.Id, "Unresolved Address");
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Organizations.Add(org);
            db.Cases.Add(c);
            await db.SaveChangesAsync();
        }
        var ctrl = Build(factory);

        var result = await ctrl.GetAll(ct: default);

        var ok   = Assert.IsType<OkObjectResult>(result.Result);
        var body = Assert.IsType<PublicCaseDiscoveryPagedResponse>(ok.Value);
        var item = Assert.Single(body.Items);
        Assert.Null(item.ApproxLatitude);
        Assert.Null(item.ApproxLongitude);
    }

    [Fact]
    public async Task GetAll_ExcludesNonPublicAndNonQualifyingStatusCases()
    {
        var factory = CreateFactory();
        var org = MakeOrg();
        var visible    = MakeCase(org.Id, "Visible", status: CaseStatus.Haunted);
        var notPublic  = MakeCase(org.Id, "Private", isPublic: false, status: CaseStatus.Public);
        var wrongState = MakeCase(org.Id, "Proposed", status: CaseStatus.Proposed);
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Organizations.Add(org);
            db.Cases.AddRange(visible, notPublic, wrongState);
            await db.SaveChangesAsync();
        }
        var ctrl = Build(factory);

        var result = await ctrl.GetAll(ct: default);

        var ok   = Assert.IsType<OkObjectResult>(result.Result);
        var body = Assert.IsType<PublicCaseDiscoveryPagedResponse>(ok.Value);
        var item = Assert.Single(body.Items);
        Assert.Equal("Visible", item.Title);
    }

    [Fact]
    public async Task GetAll_PaginatesAcrossOrganizations()
    {
        var factory = CreateFactory();
        var org = MakeOrg();
        var cases = Enumerable.Range(1, 5).Select(i => MakeCase(org.Id, $"Case {i}")).ToArray();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Organizations.Add(org);
            db.Cases.AddRange(cases);
            await db.SaveChangesAsync();
        }
        var ctrl = Build(factory);

        var result = await ctrl.GetAll(page: 1, pageSize: 2, ct: default);

        var ok   = Assert.IsType<OkObjectResult>(result.Result);
        var body = Assert.IsType<PublicCaseDiscoveryPagedResponse>(ok.Value);
        Assert.Equal(2, body.Items.Count);
        Assert.Equal(5, body.TotalCount);
    }
}
