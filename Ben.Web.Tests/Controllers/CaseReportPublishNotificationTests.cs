using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Tests that publishing a CaseReport auto-creates a CaseMessage notification for the client.
/// </summary>
public class CaseReportPublishNotificationTests
{
    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var options = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PooledDbContextFactory<BenDataContext>(options);
    }

    private static CaseReportController BuildController(IDbContextFactory<BenDataContext> factory, Guid userId)
    {
        var ctrl = new CaseReportController(factory, new Ben.Service.RepositoryService.Services.OrganizationSecurityService(factory));
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Bearer"))
            }
        };
        return ctrl;
    }

    [Fact]
    public async Task Publish_CreatesClientNotificationMessage()
    {
        var factory  = CreateFactory();
        var orgId    = Guid.NewGuid();
        var caseId   = Guid.NewGuid();
        var reportId = Guid.NewGuid();
        var userId   = Guid.NewGuid();

        // Seed prerequisite data
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Organizations.Add(new Organization { Id = orgId, Name = "Test Org", UrlName = "test-org", DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId });
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = userId,
                Role = OrganizationMemberRole.Administrator, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
            });
            db.Cases.Add(new Case
            {
                Id = caseId, OrganizationId = orgId, Title = "Test Case",
                CaseYear = 2026, OrgCaseNumber = 1,
                StreetAddress1 = "123 Main St", City = "Nashville", State = "TN", ZipCode = "37201", Country = "US",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
            });
            db.CaseReports.Add(new CaseReport
            {
                Id = reportId, CaseId = caseId, Title = "Final Assessment Report",
                Status = CaseReportStatus.Draft,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
            });
            await db.SaveChangesAsync();
        }

        var ctrl   = BuildController(factory, userId);
        var result = await ctrl.Publish(orgId, caseId, reportId, CancellationToken.None);

        // Report should now be Published
        Assert.IsType<OkObjectResult>(result.Result);

        // A CaseMessage should have been created with IsReadByClient = false
        await using var verifyDb = await factory.CreateDbContextAsync();
        var msg = await verifyDb.CaseMessages
            .FirstOrDefaultAsync(m => m.CaseId == caseId);

        Assert.NotNull(msg);
        Assert.Equal(CaseMessageSide.Organization, msg.SenderSide);
        Assert.False(msg.IsReadByClient);
        Assert.True(msg.IsReadByOrg);
        Assert.Contains("Final Assessment Report", msg.Body);
        Assert.Equal(userId, msg.AuthorAppUserId);
    }

    [Fact]
    public async Task Publish_MessageBody_ContainsReportTitle()
    {
        var factory  = CreateFactory();
        var orgId    = Guid.NewGuid();
        var caseId   = Guid.NewGuid();
        var reportId = Guid.NewGuid();
        var userId   = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Organizations.Add(new Organization { Id = orgId, Name = "Org", UrlName = "org", DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId });
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership { Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = userId, Role = OrganizationMemberRole.Administrator, IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId });
            db.Cases.Add(new Case { Id = caseId, OrganizationId = orgId, Title = "T", CaseYear = 2026, OrgCaseNumber = 2, StreetAddress1 = "1 St", City = "City", State = "TN", ZipCode = "00000", Country = "US", DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId });
            db.CaseReports.Add(new CaseReport { Id = reportId, CaseId = caseId, Title = "EVP Analysis", Status = CaseReportStatus.Draft, DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId });
            await db.SaveChangesAsync();
        }

        var ctrl = BuildController(factory, userId);
        await ctrl.Publish(orgId, caseId, reportId, CancellationToken.None);

        await using var verifyDb = await factory.CreateDbContextAsync();
        var msg = await verifyDb.CaseMessages.FirstOrDefaultAsync(m => m.CaseId == caseId);
        Assert.NotNull(msg);
        Assert.Contains("EVP Analysis", msg!.Body);
    }
}
