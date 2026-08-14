using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Admin;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Tests for AdminCaseController — the SuperAdmin cross-org "All Cases" view
/// (backlog item #2: SuperAdmin visibility into all cases and investigations).
/// </summary>
public class AdminCaseControllerTests
{
    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var opts = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PooledDbContextFactory<BenDataContext>(opts);
    }

    [Fact]
    public async Task GetAll_ReturnsCasesFromEveryOrganization_WithOrganizationNameJoined()
    {
        var factory = CreateFactory();
        var userId  = Guid.NewGuid();
        var orgA    = Guid.NewGuid();
        var orgB    = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Organizations.Add(new Organization { Id = orgA, Name = "Org A", UrlName = "org-a", DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId });
            db.Organizations.Add(new Organization { Id = orgB, Name = "Org B", UrlName = "org-b", DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId });
            db.Cases.Add(new Case
            {
                Id = Guid.NewGuid(), OrganizationId = orgA, Title = "Case in Org A",
                CaseYear = 2026, OrgCaseNumber = 1,
                StreetAddress1 = "1 Main", City = "Nashville", State = "TN", ZipCode = "37201", Country = "US",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
            });
            db.Cases.Add(new Case
            {
                Id = Guid.NewGuid(), OrganizationId = orgB, Title = "Case in Org B",
                CaseYear = 2026, OrgCaseNumber = 1,
                StreetAddress1 = "2 Main", City = "Memphis", State = "TN", ZipCode = "38103", Country = "US",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
            });
            await db.SaveChangesAsync();
        }

        var ctrl = new AdminCaseController(factory);
        var result = await ctrl.GetAll(default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsAssignableFrom<IEnumerable<AdminCaseSummaryRecord>>(ok.Value).ToList();

        Assert.Equal(2, list.Count);
        Assert.Contains(list, c => c.OrganizationName == "Org A" && c.Title == "Case in Org A");
        Assert.Contains(list, c => c.OrganizationName == "Org B" && c.Title == "Case in Org B");
    }

    [Fact]
    public async Task GetAll_NoCases_ReturnsEmptyList()
    {
        var factory = CreateFactory();
        var ctrl = new AdminCaseController(factory);

        var result = await ctrl.GetAll(default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Empty((IEnumerable<AdminCaseSummaryRecord>)ok.Value!);
    }
}
