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
/// Tests for AdminInvestigationController — the SuperAdmin cross-org "All Investigations" view
/// (backlog item #2: SuperAdmin visibility into all cases and investigations).
/// </summary>
public class AdminInvestigationControllerTests
{
    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var opts = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PooledDbContextFactory<BenDataContext>(opts);
    }

    [Fact]
    public async Task GetAll_ReturnsInvestigationsFromEveryOrganization_WithOrgAndCaseJoined()
    {
        var factory = CreateFactory();
        var userId  = Guid.NewGuid();
        var orgA    = Guid.NewGuid();
        var caseA   = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Organizations.Add(new Organization { Id = orgA, Name = "Org A", UrlName = "org-a", DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId });
            db.Cases.Add(new Case
            {
                Id = caseA, OrganizationId = orgA, Title = "Case in Org A",
                CaseYear = 2026, OrgCaseNumber = 7,
                StreetAddress1 = "1 Main", City = "Nashville", State = "TN", ZipCode = "37201", Country = "US",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
            });
            db.Investigations.Add(new Investigation
            {
                Id = Guid.NewGuid(), CaseId = caseA, Title = "Night Survey",
                ScheduledDateTime = new DateTime(2026, 9, 1, 20, 0, 0, DateTimeKind.Utc),
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
            });
            await db.SaveChangesAsync();
        }

        var ctrl = new AdminInvestigationController(factory);
        var result = await ctrl.GetAll(default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsAssignableFrom<IEnumerable<AdminInvestigationSummaryRecord>>(ok.Value).ToList();

        var item = Assert.Single(list);
        Assert.Equal("Org A", item.OrganizationName);
        Assert.Equal("#2026-007", item.CaseReference);
        Assert.Equal("Night Survey", item.Title);
    }

    [Fact]
    public async Task GetAll_NoInvestigations_ReturnsEmptyList()
    {
        var factory = CreateFactory();
        var ctrl = new AdminInvestigationController(factory);

        var result = await ctrl.GetAll(default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Empty((IEnumerable<AdminInvestigationSummaryRecord>)ok.Value!);
    }
}
