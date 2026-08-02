using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Public;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Controllers;

public class PublicCaseControllerTests
{
    private static PublicCaseController Build(IDbContextFactory<BenDataContext> factory)
        => new(factory, Mock.Of<IMapper>());

    private static async Task<(Organization org, Case c)> SeedPublicCaseAsync(
        IDbContextFactory<BenDataContext> factory,
        CaseStatus status = CaseStatus.Public,
        bool isPublic = true,
        string? pseudonym = null)
    {
        await using var db = await factory.CreateDbContextAsync();

        var org = new Organization { Id = Guid.NewGuid(), Name = "Test Org", UrlName = "test-org", CreatedByAppUserId = Guid.NewGuid() };
        var @case = new Case
        {
            Id                 = Guid.NewGuid(),
            OrganizationId     = org.Id,
            Title              = "The Haunted Manor",
            City               = "Springfield",
            State              = "IL",
            Country            = "US",
            CaseYear           = 2026,
            OrgCaseNumber      = 1,
            Status             = status,
            IsPublic           = isPublic,
            DateCaseOpened     = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            Description        = "<p>Strange events.</p>",
            PublicPseudonym    = pseudonym,
            CreatedByAppUserId = org.CreatedByAppUserId,
        };

        db.Organizations.Add(org);
        db.Cases.Add(@case);
        await db.SaveChangesAsync();
        return (org, @case);
    }

    // ── GetPublicCases ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetPublicCases_UnknownOrg_Returns404()
    {
        var factory = TestDbFactory.Create();
        var result  = await Build(factory).GetPublicCases("unknown-org", CancellationToken.None);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetPublicCases_ReturnsOnlyPublicAndHauntedCases()
    {
        var factory = TestDbFactory.Create();
        await using var db = await factory.CreateDbContextAsync();

        var org = new Organization { Id = Guid.NewGuid(), Name = "O", UrlName = "testorg", CreatedByAppUserId = Guid.NewGuid() };
        db.Organizations.Add(org);

        var statuses = Enum.GetValues<CaseStatus>();
        int i = 1;
        foreach (var s in statuses)
        {
            db.Cases.Add(new Case
            {
                Id = Guid.NewGuid(), OrganizationId = org.Id, Title = $"Case {s}",
                City = "City", State = "ST", Country = "US",
                CaseYear = 2026, OrgCaseNumber = i++, Status = s, IsPublic = true,
                DateCaseOpened     = DateTime.UtcNow,
                CreatedByAppUserId = org.CreatedByAppUserId,
            });
        }
        await db.SaveChangesAsync();

        var result = await Build(factory).GetPublicCases("testorg", CancellationToken.None);
        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var list   = Assert.IsAssignableFrom<IEnumerable<PublicCaseListItem>>(ok.Value);
        Assert.All(list, item => Assert.True(item.IsHaunted || item.Status == CaseStatus.Public || item.Status == CaseStatus.Haunted));
        Assert.DoesNotContain(list, item => item.Status == CaseStatus.Proposed);
        Assert.DoesNotContain(list, item => item.Status == CaseStatus.Accepted);
    }

    [Fact]
    public async Task GetPublicCases_PrivateCases_AreExcluded()
    {
        var factory = TestDbFactory.Create();
        await SeedPublicCaseAsync(factory, CaseStatus.Public, isPublic: false);

        var result = await Build(factory).GetPublicCases("test-org", CancellationToken.None);
        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var list   = Assert.IsAssignableFrom<IEnumerable<PublicCaseListItem>>(ok.Value);
        Assert.Empty(list);
    }

    // ── GetPublicCase ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetPublicCase_InvalidRef_Returns400()
    {
        var factory = TestDbFactory.Create();
        var result  = await Build(factory).GetPublicCase("test-org", "not-a-ref", CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetPublicCase_UnknownOrg_Returns404()
    {
        var factory = TestDbFactory.Create();
        var result  = await Build(factory).GetPublicCase("unknown-org", "2026-001", CancellationToken.None);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetPublicCase_UnknownCase_Returns404()
    {
        var factory = TestDbFactory.Create();
        await SeedPublicCaseAsync(factory);  // seeds case #2026-001

        var result = await Build(factory).GetPublicCase("test-org", "2026-999", CancellationToken.None);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetPublicCase_PrivateCase_Returns404()
    {
        var factory = TestDbFactory.Create();
        await SeedPublicCaseAsync(factory, CaseStatus.Public, isPublic: false);

        var result = await Build(factory).GetPublicCase("test-org", "2026-001", CancellationToken.None);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetPublicCase_AcceptedStatus_Returns404()
    {
        var factory = TestDbFactory.Create();
        await SeedPublicCaseAsync(factory, CaseStatus.Accepted, isPublic: true);

        var result = await Build(factory).GetPublicCase("test-org", "2026-001", CancellationToken.None);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetPublicCase_ValidPublicCase_ReturnsDetail()
    {
        var factory    = TestDbFactory.Create();
        var (org, _)   = await SeedPublicCaseAsync(factory, CaseStatus.Public);

        var result = await Build(factory).GetPublicCase("test-org", "2026-001", CancellationToken.None);
        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var detail = Assert.IsType<PublicCaseDetail>(ok.Value);

        Assert.Equal("#2026-001", detail.CaseReference);
        Assert.Equal("The Haunted Manor", detail.Title);
        Assert.Equal("Springfield", detail.City);
        Assert.Equal(org.Name, detail.OrgName);
    }

    [Fact]
    public async Task GetPublicCase_HauntedStatus_IsHauntedTrue()
    {
        var factory = TestDbFactory.Create();
        await SeedPublicCaseAsync(factory, CaseStatus.Haunted);

        var result = await Build(factory).GetPublicCase("test-org", "2026-001", CancellationToken.None);
        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var detail = Assert.IsType<PublicCaseDetail>(ok.Value);
        Assert.True(detail.IsHaunted);
    }

    [Fact]
    public async Task GetPublicCase_WithPseudonym_SubstitutesClientName()
    {
        var factory = TestDbFactory.Create();
        await SeedPublicCaseAsync(factory, CaseStatus.Public, pseudonym: "The Smith Family");

        var result = await Build(factory).GetPublicCase("test-org", "2026-001", CancellationToken.None);
        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var detail = Assert.IsType<PublicCaseDetail>(ok.Value);
        Assert.Equal("The Smith Family", detail.ClientName);
    }

    [Fact]
    public async Task GetPublicCase_NoPseudonym_ClientNameIsNull()
    {
        var factory = TestDbFactory.Create();
        await SeedPublicCaseAsync(factory, CaseStatus.Public, pseudonym: null);

        var result = await Build(factory).GetPublicCase("test-org", "2026-001", CancellationToken.None);
        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var detail = Assert.IsType<PublicCaseDetail>(ok.Value);
        Assert.Null(detail.ClientName);
    }

    [Fact]
    public async Task GetPublicCase_ResponseDoesNotContainCoordinates()
    {
        var factory = TestDbFactory.Create();
        await SeedPublicCaseAsync(factory);

        var result = await Build(factory).GetPublicCase("test-org", "2026-001", CancellationToken.None);
        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var detail = Assert.IsType<PublicCaseDetail>(ok.Value);

        // Privacy assertions — lat/lon must never appear in public case response
        var props = typeof(PublicCaseDetail).GetProperties().Select(p => p.Name);
        Assert.DoesNotContain("Latitude",      props);
        Assert.DoesNotContain("Longitude",     props);
        Assert.DoesNotContain("StreetAddress1", props);
    }
}
