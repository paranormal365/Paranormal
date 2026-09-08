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
        var ctrl = new CaseReportController(factory, new Ben.Service.RepositoryService.Services.OrganizationSecurityService(factory), new Moq.Mock<Ben.Data.Common.Interfaces.IFileStorageService>().Object);
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

    /// <summary>
    /// Seeds a group, a case and one report, and hands back a controller acting as an admin.
    /// </summary>
    private static async Task<(CaseReportController Ctrl, Guid OrgId, Guid CaseId, Guid ReportId, Guid UserId)>
        SeedReportAsync(IDbContextFactory<BenDataContext> factory, string title = "Final Report")
    {
        var orgId = Guid.NewGuid(); var caseId = Guid.NewGuid();
        var reportId = Guid.NewGuid(); var userId = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Organizations.Add(new Organization { Id = orgId, Name = "Org", UrlName = $"org-{orgId:N}", DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId });
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership { Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = userId, Role = OrganizationMemberRole.Administrator, IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId });
            db.Cases.Add(new Case { Id = caseId, OrganizationId = orgId, Title = "T", CaseYear = 2026, OrgCaseNumber = 3, StreetAddress1 = "1 St", City = "City", State = "TN", ZipCode = "00000", Country = "US", DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId });
            db.CaseReports.Add(new CaseReport { Id = reportId, CaseId = caseId, Title = title, Status = CaseReportStatus.Draft, DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId });
            await db.SaveChangesAsync();
        }

        return (BuildController(factory, userId), orgId, caseId, reportId, userId);
    }

    /// <summary>
    /// Editing a section marks the whole report as changed.
    /// </summary>
    /// <remarks>
    /// <para>W-A14 of the 2026-09-06 evaluation. Only the report row's own Update touched
    /// DateUpdated, so a report whose text had been entirely rewritten — section by section, which
    /// is how a report is actually written — still claimed it had not changed since it was
    /// published. Any "edited since published" flag built on that would have been blind to the
    /// ordinary case.</para>
    /// </remarks>
    [Fact]
    public async Task Editing_a_section_marks_the_report_as_changed()
    {
        var factory = CreateFactory();
        var (ctrl, orgId, caseId, reportId, _) = await SeedReportAsync(factory);

        await ctrl.Publish(orgId, caseId, reportId, CancellationToken.None);

        DateTime publishedAt;
        await using (var db = await factory.CreateDbContextAsync())
            publishedAt = (await db.CaseReports.SingleAsync(r => r.Id == reportId)).PublishedAt!.Value;

        await Task.Delay(10);
        var added = await ctrl.AddSection(orgId, caseId, reportId,
            new UpsertSectionRequest("What we heard", "<p>Two knocks.</p>", CaseReportSectionType.Text),
            CancellationToken.None);
        Assert.IsType<OkObjectResult>(added.Result);

        await using var check = await factory.CreateDbContextAsync();
        var report = await check.CaseReports.SingleAsync(r => r.Id == reportId);
        Assert.NotNull(report.DateUpdated);
        Assert.True(report.DateUpdated > publishedAt,
            "Adding a section left the report claiming it had not changed since it was published.");
    }

    /// <summary>
    /// Publishing a second time tells the client the document changed, not that it arrived.
    /// </summary>
    /// <remarks>
    /// W-A14: the client had been sent a report and reads a PDF generated live, so a group that
    /// corrects it silently hands them a different document. The notice is the whole fix — the
    /// endpoint always allowed a second publish; nothing ever offered one.
    /// </remarks>
    [Fact]
    public async Task Re_publishing_tells_the_client_the_report_was_updated()
    {
        var factory = CreateFactory();
        var (ctrl, orgId, caseId, reportId, _) = await SeedReportAsync(factory, "Bell Witch Findings");

        await ctrl.Publish(orgId, caseId, reportId, CancellationToken.None);
        await ctrl.Publish(orgId, caseId, reportId, CancellationToken.None);

        await using var check = await factory.CreateDbContextAsync();
        var messages = await check.CaseMessages.Where(m => m.CaseId == caseId)
            .OrderBy(m => m.DateCreated).ToListAsync();

        Assert.Equal(2, messages.Count);
        Assert.Contains("has been published", messages[0].Body);
        Assert.Contains("has been updated",   messages[1].Body);
        Assert.All(messages, m => Assert.Contains("Bell Witch Findings", m.Body));
    }
}
