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
/// Citing a field session in a case report.
/// </summary>
/// <remarks>
/// <para>The point of the feature: everything the phones collect in the field — readings, marks,
/// positions, rooms, recordings — is on the site but was absent from the document the client is
/// actually handed. A section can now point at a session.</para>
///
/// <para>The point of these tests: a citation must reach the session only through THIS case's
/// investigations. A report that could cite any session by id would put another client's night
/// into this client's report, under this org's letterhead.</para>
/// </remarks>
public class CaseReportFieldSessionTests
{
    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var options = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PooledDbContextFactory<BenDataContext>(options);
    }

    private static CaseReportController Build(IDbContextFactory<BenDataContext> factory, Guid userId)
    {
        var ctrl = new CaseReportController(
            factory, new Ben.Service.RepositoryService.Services.OrganizationSecurityService(factory));
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
                    "Bearer", ClaimTypes.NameIdentifier, ClaimTypes.Role)),
            }
        };
        return ctrl;
    }

    private sealed record World(
        IDbContextFactory<BenDataContext> Factory, Guid OrgId, Guid CaseId, Guid UserId,
        Guid InvestigationId, Guid SessionId, Guid OtherCaseId, Guid OtherSessionId);

    /// <summary>
    /// One org, two cases, one session on each. The second case exists precisely so a test can
    /// try to cite a session that isn't this case's.
    /// </summary>
    private static async Task<World> SeedAsync()
    {
        var factory = CreateFactory();
        var orgId = Guid.NewGuid();
        var caseId = Guid.NewGuid();
        var otherCaseId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var investigationId = Guid.NewGuid();
        var otherInvestigationId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var otherSessionId = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();
        db.Organizations.Add(new Organization
        {
            Id = orgId, Name = "Test Org", UrlName = "test",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = userId,
            Role = OrganizationMemberRole.Manager, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });

        foreach (var (id, number, title) in new[] { (caseId, 1, "Test Case"), (otherCaseId, 2, "Other Case") })
        {
            db.Cases.Add(new Case
            {
                Id = id, OrganizationId = orgId, Title = title, CaseYear = 2026, OrgCaseNumber = number,
                StreetAddress1 = "1 Main", City = "Nashville", State = "TN", ZipCode = "37201", Country = "US",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
            });
        }

        foreach (var (id, ownerCase, title) in new[]
                 { (investigationId, caseId, "The Old Mill"), (otherInvestigationId, otherCaseId, "Somewhere else") })
        {
            db.Investigations.Add(new Investigation
            {
                Id = id, OrganizationId = orgId, CaseId = ownerCase, Title = title,
                ScheduledDateTime = DateTime.UtcNow.AddDays(-1), DateCreated = DateTime.UtcNow,
            });
        }

        foreach (var (id, investigation, label) in new[]
                 { (sessionId, investigationId, "Cellar"), (otherSessionId, otherInvestigationId, "Not this case") })
        {
            var documentId = Guid.NewGuid();
            db.UploadFiles.Add(new UploadFile
            {
                Id = documentId, FileName = "data.json", ContentType = "application/json",
                FileSize = 1024, StoragePath = $"orgs/{orgId}/{documentId}.json",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
            });
            db.FieldSessionUploads.Add(new FieldSessionUpload
            {
                Id = id, InvestigationId = investigation, SubmittedByAppUserId = userId,
                RecordedByAppUserId = userId, RecordedByName = "Sam Reed",
                DeviceSessionId = Guid.NewGuid(), DocumentUploadFileId = documentId,
                DeviceModel = "iPhone17,1", LocationLabel = label,
                StartedAt = new DateTime(2026, 8, 20, 23, 0, 0, DateTimeKind.Utc),
                EndedAt = new DateTime(2026, 8, 21, 3, 30, 0, DateTimeKind.Utc),
                ReadingCount = 8_412, MarkerCount = 17,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
            });
        }

        await db.SaveChangesAsync();
        await TestSeeds.BridgeAsync(factory, orgId);
        return new World(factory, orgId, caseId, userId, investigationId, sessionId, otherCaseId, otherSessionId);
    }

    private static async Task<(Guid ReportId, Guid SectionId)> MakeReportWithSectionAsync(
        World world, CaseReportController ctrl)
    {
        var report = (CaseReportDetail)((OkObjectResult)(await ctrl.Create(
            world.OrgId, world.CaseId,
            new UpsertCaseReportRequest("Report", null, null, null), default)).Result!).Value!;
        var section = (CaseReportSectionDto)((OkObjectResult)(await ctrl.AddSection(
            world.OrgId, world.CaseId, report.Id,
            new UpsertSectionRequest("Field work", null, CaseReportSectionType.FieldSessions),
            default)).Result!).Value!;
        return (report.Id, section.Id);
    }

    [Fact]
    public async Task AvailableSessions_AreTheOnesRecordedForThisCase()
    {
        var world = await SeedAsync();
        var ctrl = Build(world.Factory, world.UserId);

        var ok = Assert.IsType<OkObjectResult>(
            (await ctrl.GetAvailableFieldSessions(world.OrgId, world.CaseId, default)).Result);
        var list = Assert.IsAssignableFrom<IEnumerable<AvailableFieldSessionDto>>(ok.Value).ToList();

        var only = Assert.Single(list);
        Assert.Equal(world.SessionId, only.Id);
        Assert.Equal("The Old Mill", only.InvestigationTitle);
        Assert.Equal(8_412, only.ReadingCount);
    }

    [Fact]
    public async Task CitingASession_PutsItOnTheSection()
    {
        var world = await SeedAsync();
        var ctrl = Build(world.Factory, world.UserId);
        var (reportId, sectionId) = await MakeReportWithSectionAsync(world, ctrl);

        var ok = Assert.IsType<OkObjectResult>((await ctrl.AddSectionFieldSession(
            world.OrgId, world.CaseId, reportId, sectionId,
            new AddSectionFieldSessionRequest(world.SessionId, "the cellar spike at 01:14"),
            default)).Result);
        var dto = Assert.IsType<CaseReportSectionFieldSessionDto>(ok.Value);
        Assert.Equal(world.SessionId, dto.FieldSessionUploadId);
        Assert.Equal("the cellar spike at 01:14", dto.Caption);

        // And it comes back on the report, which is what the builder and the PDF both read.
        var detail = (CaseReportDetail)((OkObjectResult)(await ctrl.GetById(
            world.OrgId, world.CaseId, reportId, default)).Result!).Value!;
        var cited = Assert.Single(detail.Sections.Single().FieldSessions);
        Assert.Equal("Cellar", cited.LocationLabel);
        Assert.Equal(17, cited.MarkerCount);
    }

    /// <summary>
    /// The boundary that matters: a session belonging to another case cannot be cited, even by a
    /// manager of the org that owns both. Otherwise one client's night appears in another
    /// client's report.
    /// </summary>
    [Fact]
    public async Task ASessionFromAnotherCase_CannotBeCited()
    {
        var world = await SeedAsync();
        var ctrl = Build(world.Factory, world.UserId);
        var (reportId, sectionId) = await MakeReportWithSectionAsync(world, ctrl);

        var result = await ctrl.AddSectionFieldSession(
            world.OrgId, world.CaseId, reportId, sectionId,
            new AddSectionFieldSessionRequest(world.OtherSessionId, null), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task CitingTheSameSessionTwice_UpdatesRatherThanDuplicates()
    {
        var world = await SeedAsync();
        var ctrl = Build(world.Factory, world.UserId);
        var (reportId, sectionId) = await MakeReportWithSectionAsync(world, ctrl);

        await ctrl.AddSectionFieldSession(world.OrgId, world.CaseId, reportId, sectionId,
            new AddSectionFieldSessionRequest(world.SessionId, "first note"), default);
        await ctrl.AddSectionFieldSession(world.OrgId, world.CaseId, reportId, sectionId,
            new AddSectionFieldSessionRequest(world.SessionId, "second note"), default);

        var detail = (CaseReportDetail)((OkObjectResult)(await ctrl.GetById(
            world.OrgId, world.CaseId, reportId, default)).Result!).Value!;
        var cited = Assert.Single(detail.Sections.Single().FieldSessions);
        Assert.Equal("second note", cited.Caption);
    }

    /// <summary>Removing a citation removes the CITATION. The session is evidence and stays.</summary>
    [Fact]
    public async Task RemovingACitation_LeavesTheSessionAlone()
    {
        var world = await SeedAsync();
        var ctrl = Build(world.Factory, world.UserId);
        var (reportId, sectionId) = await MakeReportWithSectionAsync(world, ctrl);
        var link = (CaseReportSectionFieldSessionDto)((OkObjectResult)(await ctrl.AddSectionFieldSession(
            world.OrgId, world.CaseId, reportId, sectionId,
            new AddSectionFieldSessionRequest(world.SessionId, null), default)).Result!).Value!;

        Assert.IsType<NoContentResult>(await ctrl.RemoveSectionFieldSession(
            world.OrgId, world.CaseId, reportId, sectionId, link.Id, default));

        await using var db = await world.Factory.CreateDbContextAsync();
        Assert.Empty(db.CaseReportSectionFieldSessions);
        Assert.NotNull(await db.FieldSessionUploads.FindAsync(world.SessionId));
    }

    /// <summary>
    /// A member of another org cannot list this case's sessions. The org check and the
    /// case-belongs-to-org check are both load-bearing here — see the Phase-B chain fix.
    /// </summary>
    [Fact]
    public async Task AStranger_CannotListThisCasesSessions()
    {
        var world = await SeedAsync();
        var ctrl = Build(world.Factory, Guid.NewGuid());

        var result = await ctrl.GetAvailableFieldSessions(world.OrgId, world.CaseId, default);

        Assert.IsType<ForbidResult>(result.Result);
    }

    /// <summary>
    /// The PDF is what the client actually reads, so a cited session has to SAY something there.
    /// </summary>
    /// <remarks>
    /// The bytes are compressed, so this reads the text back out of the generated document
    /// rather than grepping it — the earlier version of this test compared file SIZES, which
    /// would have passed just as happily if the generator had written the wrong words.
    /// </remarks>
    [Fact]
    public async Task ThePdf_StatesWhatTheSessionHolds()
    {
        var world = await SeedAsync();
        var ctrl = Build(world.Factory, world.UserId);
        var (reportId, sectionId) = await MakeReportWithSectionAsync(world, ctrl);
        await ctrl.AddSectionFieldSession(world.OrgId, world.CaseId, reportId, sectionId,
            new AddSectionFieldSessionRequest(world.SessionId, "the cellar spike at 01:14"), default);

        var pdf = Assert.IsType<FileContentResult>(
            await ctrl.ExportPdf(world.OrgId, world.CaseId, reportId, default));
        Assert.Equal("application/pdf", pdf.ContentType);

        var text = ExtractText(pdf.FileContents);
        Assert.Contains("Cellar", text);                       // where it was
        Assert.Contains("Sam Reed", text);                     // who recorded it
        Assert.Contains("iPhone17,1", text);                   // on what
        Assert.Contains("8,412 readings", text);               // how much it holds
        Assert.Contains("17 marks", text);
        Assert.Contains("the cellar spike at 01:14", text);    // what the manager said about it
    }

    /// <summary>
    /// An interrupted session has no honest end time. The report must say so rather than print
    /// a guess a client could later be told was wrong.
    /// </summary>
    [Fact]
    public async Task ThePdf_SaysWhenASessionWasInterrupted()
    {
        var world = await SeedAsync();
        await using (var db = await world.Factory.CreateDbContextAsync())
        {
            var session = await db.FieldSessionUploads.FindAsync(world.SessionId);
            session!.EndedAt = null;
            await db.SaveChangesAsync();
        }

        var ctrl = Build(world.Factory, world.UserId);
        var (reportId, sectionId) = await MakeReportWithSectionAsync(world, ctrl);
        await ctrl.AddSectionFieldSession(world.OrgId, world.CaseId, reportId, sectionId,
            new AddSectionFieldSessionRequest(world.SessionId, null), default);

        var pdf = Assert.IsType<FileContentResult>(
            await ctrl.ExportPdf(world.OrgId, world.CaseId, reportId, default));

        Assert.Contains("interrupted", ExtractText(pdf.FileContents));
    }

    /// <summary>Reads the words back out of a generated PDF.</summary>
    private static string ExtractText(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var document = new Telerik.Windows.Documents.Fixed.FormatProviders.Pdf.PdfFormatProvider().Import(stream, null);
        var builder = new System.Text.StringBuilder();
        foreach (var page in document.Pages)
        {
            foreach (var element in page.Content)
            {
                if (element is Telerik.Windows.Documents.Fixed.Model.Text.TextFragment fragment)
                    builder.Append(fragment.Text).Append(' ');
            }
        }
        return builder.ToString();
    }
}

/// <summary>
/// Deleting the investigation a cited session was recorded during.
/// </summary>
/// <remarks>
/// The session cascades away with its investigation, so without a check the citation's foreign key
/// turns an ordinary delete into a 500 — and the panel that ignored the delete's result showed
/// that as success. The report must not quietly lose its evidence, so the delete is refused with a
/// sentence somebody can act on, and the panel now reads it.
/// </remarks>
public class InvestigationDeleteWithCitedSessionTests
{
    [Fact]
    public async Task Deleting_an_investigation_whose_session_is_cited_is_refused_with_a_reason()
    {
        var world = await SeedAsync(citeTheSession: true);
        var ctrl = BuildController(world.Factory, world.UserId);

        var result = await ctrl.Delete(world.OrgId, world.CaseId, world.InvestigationId, default);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Contains("cites", conflict.Value!.ToString()!);
        Assert.Contains("Remove the citation", conflict.Value!.ToString()!);

        // And nothing was removed on the way to refusing.
        await using var db = await world.Factory.CreateDbContextAsync();
        Assert.NotNull(await db.Investigations.FindAsync(world.InvestigationId));
        Assert.NotNull(await db.FieldSessionUploads.FindAsync(world.SessionId));
    }

    /// <summary>The same visit, with nothing citing it, still deletes.</summary>
    [Fact]
    public async Task An_uncited_investigation_still_deletes()
    {
        var world = await SeedAsync(citeTheSession: false);
        var ctrl = BuildController(world.Factory, world.UserId);

        var result = await ctrl.Delete(world.OrgId, world.CaseId, world.InvestigationId, default);

        Assert.IsType<NoContentResult>(result);
    }

    private static Ben.Data.WebApi.Controllers.Entities.InvestigationController BuildController(
        IDbContextFactory<BenDataContext> factory, Guid userId)
    {
        var mapper = new Moq.Mock<AutoMapper.IMapper>().Object;
        var ctrl = new Ben.Data.WebApi.Controllers.Entities.InvestigationController(
            factory, mapper,
            new Ben.Data.WebApi.Services.Billing.SubscriptionLimitGuard(factory),
            new Ben.Service.RepositoryService.Services.OrganizationSecurityService(factory));
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Bearer")),
            }
        };
        return ctrl;
    }

    private sealed record DeleteWorld(
        IDbContextFactory<BenDataContext> Factory, Guid OrgId, Guid CaseId, Guid UserId,
        Guid InvestigationId, Guid SessionId);

    private static async Task<DeleteWorld> SeedAsync(bool citeTheSession)
    {
        var options = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var factory = new PooledDbContextFactory<BenDataContext>(options);

        var orgId = Guid.NewGuid();
        var caseId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var investigationId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var documentId = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Organizations.Add(new Organization
            {
                Id = orgId, Name = "Test Org", UrlName = "test",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
            });
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = userId,
                Role = OrganizationMemberRole.Manager, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
            });
            db.Cases.Add(new Case
            {
                Id = caseId, OrganizationId = orgId, Title = "Test Case", CaseYear = 2026, OrgCaseNumber = 1,
                StreetAddress1 = "1 Main", City = "Nashville", State = "TN", ZipCode = "37201", Country = "US",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
            });
            db.Investigations.Add(new Investigation
            {
                Id = investigationId, OrganizationId = orgId, CaseId = caseId, Title = "The Old Mill",
                ScheduledDateTime = DateTime.UtcNow.AddDays(-1),
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
            });
            db.UploadFiles.Add(new UploadFile
            {
                Id = documentId, FileName = "data.json", ContentType = "application/json",
                FileSize = 1024, StoragePath = $"orgs/{orgId}/{documentId}.json",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
            });
            db.FieldSessionUploads.Add(new FieldSessionUpload
            {
                Id = sessionId, InvestigationId = investigationId, SubmittedByAppUserId = userId,
                DeviceSessionId = Guid.NewGuid(), DocumentUploadFileId = documentId,
                DeviceModel = "iPhone17,1", StartedAt = DateTime.UtcNow.AddHours(-4),
                EndedAt = DateTime.UtcNow, ReadingCount = 10, MarkerCount = 1,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
            });

            if (citeTheSession)
            {
                var report = new CaseReport
                {
                    Id = Guid.NewGuid(), CaseId = caseId, Title = "Report",
                    Status = CaseReportStatus.Draft,
                    DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
                };
                var section = new CaseReportSection
                {
                    Id = Guid.NewGuid(), CaseReportId = report.Id, Title = "Field work",
                    SectionType = CaseReportSectionType.FieldSessions, SortOrder = 10,
                    DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
                };
                db.CaseReports.Add(report);
                db.CaseReportSections.Add(section);
                db.CaseReportSectionFieldSessions.Add(new CaseReportSectionFieldSession
                {
                    Id = Guid.NewGuid(), CaseReportSectionId = section.Id,
                    FieldSessionUploadId = sessionId, SortOrder = 10,
                });
            }

            await db.SaveChangesAsync();
        }

        await TestSeeds.BridgeAsync(factory, orgId);
        return new DeleteWorld(factory, orgId, caseId, userId, investigationId, sessionId);
    }
}
