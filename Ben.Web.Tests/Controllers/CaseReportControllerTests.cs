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
/// Tests for CaseReportController — report CRUD, sections, section files, and the Phase-B
/// cross-org chain fix. Before the fix, 11 of this controller's 12 actions checked only
/// "is the caller a member of the route org," never that caseId actually belonged to that org —
/// a real member of their OWN org could reach any other org's confidential investigation
/// reports just by knowing/guessing a caseId. Only Create verified the chain correctly.
/// </summary>
public class CaseReportControllerTests
{
    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var options = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PooledDbContextFactory<BenDataContext>(options);
    }

    private static CaseReportController Build(IDbContextFactory<BenDataContext> factory, Guid userId,
        bool isSuperAdmin = false)
    {
        var ctrl = new CaseReportController(factory, new Ben.Service.RepositoryService.Services.OrganizationSecurityService(factory), new Moq.Mock<Ben.Data.Common.Interfaces.IFileStorageService>().Object);
        List<Claim> claims = [new Claim(ClaimTypes.NameIdentifier, userId.ToString())];
        if (isSuperAdmin) claims.Add(new Claim(ClaimTypes.Role, Ben.Data.Common.Constants.RoleNames.SuperAdmin));
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer", ClaimTypes.NameIdentifier, ClaimTypes.Role))
            }
        };
        return ctrl;
    }

    [Fact]
    public async Task GetAll_SuperAdminNonMember_IsNotForbidden()
    {
        // Same bypass rule as CaseFileController — see its SuperAdmin test for the 2026-08-22 bug.
        var seeded = await SeedAsync();
        var ctrl = Build(seeded.Factory, Guid.NewGuid(), isSuperAdmin: true);

        var result = await ctrl.GetAll(seeded.OrgId, seeded.CaseId, default);

        Assert.IsNotType<ForbidResult>(result.Result);
    }

    private static async Task<(IDbContextFactory<BenDataContext> Factory, Guid OrgId, Guid CaseId, Guid UserId)> SeedAsync()
    {
        var factory = CreateFactory();
        var orgId   = Guid.NewGuid();
        var caseId  = Guid.NewGuid();
        var userId  = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();
        db.Organizations.Add(new Organization { Id = orgId, Name = "Test Org", UrlName = "test", DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = userId,
            Role = OrganizationMemberRole.Manager, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        db.Cases.Add(new Case
        {
            Id = caseId, OrganizationId = orgId, Title = "Test Case",
            CaseYear = 2026, OrgCaseNumber = 1,
            StreetAddress1 = "1 Main", City = "Nashville", State = "TN", ZipCode = "37201", Country = "US",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        await db.SaveChangesAsync();
        await TestSeeds.BridgeAsync(factory, orgId, TestSeeds.CaseWork);
        return (factory, orgId, caseId, userId);
    }

    private static UpsertCaseReportRequest MakeRequest(string title = "Investigation Report") =>
        new(title, "Summary", "Conclusion", null);

    // ── Happy path ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_ReturnsDraftReport()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var ctrl = Build(factory, userId);

        var result = await ctrl.Create(orgId, caseId, MakeRequest(), default);

        var ok  = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<CaseReportDetail>(ok.Value);
        Assert.Equal(CaseReportStatus.Draft, dto.Status);
        Assert.Equal("Investigation Report", dto.Title);
    }

    [Fact]
    public async Task GetAll_ReturnsCreatedReports()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var ctrl = Build(factory, userId);
        await ctrl.Create(orgId, caseId, MakeRequest(), default);

        var result = await ctrl.GetAll(orgId, caseId, default);

        var ok   = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsAssignableFrom<IEnumerable<CaseReportSummary>>(ok.Value);
        Assert.Single(list);
    }

    [Fact]
    public async Task GetById_ReturnsFullDetail()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var ctrl = Build(factory, userId);
        var created = ((CaseReportDetail)((OkObjectResult)(await ctrl.Create(orgId, caseId, MakeRequest(), default)).Result!).Value!);

        var result = await ctrl.GetById(orgId, caseId, created.Id, default);

        var ok  = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<CaseReportDetail>(ok.Value);
        Assert.Equal(created.Id, dto.Id);
    }

    [Fact]
    public async Task Publish_SetsPublishedStatus_AndNotifiesClient()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var ctrl = Build(factory, userId);
        var created = ((CaseReportDetail)((OkObjectResult)(await ctrl.Create(orgId, caseId, MakeRequest(), default)).Result!).Value!);

        var result = await ctrl.Publish(orgId, caseId, created.Id, default);

        var ok  = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<CaseReportDetail>(ok.Value);
        Assert.Equal(CaseReportStatus.Published, dto.Status);

        await using var db = await factory.CreateDbContextAsync();
        Assert.True(await db.CaseMessages.AnyAsync(m => m.CaseId == caseId));
    }

    [Fact]
    public async Task Delete_PublishedReport_ReturnsConflict()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var ctrl = Build(factory, userId);
        var created = ((CaseReportDetail)((OkObjectResult)(await ctrl.Create(orgId, caseId, MakeRequest(), default)).Result!).Value!);
        await ctrl.Publish(orgId, caseId, created.Id, default);

        var result = await ctrl.Delete(orgId, caseId, created.Id, default);

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task AddSection_AppendsToReport()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var ctrl = Build(factory, userId);
        var created = ((CaseReportDetail)((OkObjectResult)(await ctrl.Create(orgId, caseId, MakeRequest(), default)).Result!).Value!);

        var result = await ctrl.AddSection(orgId, caseId, created.Id,
            new UpsertSectionRequest("Findings", "Body text", CaseReportSectionType.Text), default);

        var ok  = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<CaseReportSectionDto>(ok.Value);
        Assert.Equal("Findings", dto.Title);
    }

    // ── Cross-org chain (Phase B) ────────────────────────────────────────────

    private static async Task<(Guid AttackerOrgId, Guid AttackerId)> SeedAttackerAsync(IDbContextFactory<BenDataContext> factory)
    {
        var attackerOrgId = Guid.NewGuid();
        var attackerId    = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        db.Organizations.Add(new Organization { Id = attackerOrgId, Name = "Attacker Org", UrlName = "attacker", DateCreated = DateTime.UtcNow, CreatedByAppUserId = attackerId });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership { Id = Guid.NewGuid(), OrganizationId = attackerOrgId, AppUserId = attackerId, Role = OrganizationMemberRole.Owner, IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = attackerId });
        await db.SaveChangesAsync();
        return (attackerOrgId, attackerId);
    }

    [Fact]
    public async Task GetAllGetByIdUpdatePublishDelete_CaseBelongsToDifferentOrg_ReturnsNotFound()
    {
        var (factory, victimOrgId, victimCaseId, victimUserId) = await SeedAsync();
        var victim  = Build(factory, victimUserId);
        var created = ((CaseReportDetail)((OkObjectResult)(await victim.Create(victimOrgId, victimCaseId, MakeRequest("Confidential"), default)).Result!).Value!);

        var (attackerOrgId, attackerId) = await SeedAttackerAsync(factory);
        var attacker = Build(factory, attackerId);

        Assert.IsType<NotFoundResult>((await attacker.GetAll(attackerOrgId, victimCaseId, default)).Result);
        Assert.IsType<NotFoundResult>((await attacker.GetById(attackerOrgId, victimCaseId, created.Id, default)).Result);
        Assert.IsType<NotFoundResult>((await attacker.Update(attackerOrgId, victimCaseId, created.Id, MakeRequest("Hijacked"), default)).Result);
        Assert.IsType<NotFoundResult>((await attacker.Publish(attackerOrgId, victimCaseId, created.Id, default)).Result);
        Assert.IsType<NotFoundResult>(await attacker.Delete(attackerOrgId, victimCaseId, created.Id, default));

        await using var db = await factory.CreateDbContextAsync();
        var stillThere = await db.CaseReports.FirstAsync(r => r.Id == created.Id);
        Assert.Equal("Confidential", stillThere.Title);
        Assert.Equal(CaseReportStatus.Draft, stillThere.Status);
    }

    [Fact]
    public async Task Create_CaseBelongsToDifferentOrg_ReturnsNotFound()
    {
        var (factory, _, victimCaseId, _) = await SeedAsync();
        var (attackerOrgId, attackerId) = await SeedAttackerAsync(factory);
        var attacker = Build(factory, attackerId);

        var result = await attacker.Create(attackerOrgId, victimCaseId, MakeRequest(), default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task SectionActions_CaseBelongsToDifferentOrg_ReturnsNotFound()
    {
        var (factory, victimOrgId, victimCaseId, victimUserId) = await SeedAsync();
        var victim  = Build(factory, victimUserId);
        var report  = ((CaseReportDetail)((OkObjectResult)(await victim.Create(victimOrgId, victimCaseId, MakeRequest(), default)).Result!).Value!);
        var section = ((CaseReportSectionDto)((OkObjectResult)(await victim.AddSection(victimOrgId, victimCaseId, report.Id,
            new UpsertSectionRequest("Findings", "Body", CaseReportSectionType.Text), default)).Result!).Value!);

        var (attackerOrgId, attackerId) = await SeedAttackerAsync(factory);
        var attacker = Build(factory, attackerId);

        Assert.IsType<NotFoundResult>((await attacker.AddSection(attackerOrgId, victimCaseId, report.Id,
            new UpsertSectionRequest("X", null, CaseReportSectionType.Text), default)).Result);
        Assert.IsType<NotFoundResult>((await attacker.UpdateSection(attackerOrgId, victimCaseId, report.Id, section.Id,
            new UpsertSectionRequest("Hijacked", null, CaseReportSectionType.Text), default)).Result);
        Assert.IsType<NotFoundResult>(await attacker.DeleteSection(attackerOrgId, victimCaseId, report.Id, section.Id, default));
        Assert.IsType<NotFoundResult>(await attacker.ExportPdf(attackerOrgId, victimCaseId, report.Id, default));
    }

    // ── Showing a report to the public (site evaluation 2026-09-06, W-P3) ────

    [Fact]
    public async Task A_new_report_is_not_shown_to_the_public()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var result = await Build(factory, userId).Create(orgId, caseId, MakeRequest(), default);
        var dto = Assert.IsType<CaseReportDetail>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.False(dto.IsPublicSummaryVisible);
    }

    [Fact]
    public async Task The_group_can_switch_a_report_on_and_off_again()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var ctrl = Build(factory, userId);
        var created = Assert.IsType<CaseReportDetail>(
            Assert.IsType<OkObjectResult>((await ctrl.Create(orgId, caseId, MakeRequest(), default)).Result).Value);

        var on = Assert.IsType<CaseReportDetail>(Assert.IsType<OkObjectResult>(
            (await ctrl.Update(orgId, caseId, created.Id,
                new UpsertCaseReportRequest("Investigation Report", "Summary", "Conclusion", null,
                    IsPublicSummaryVisible: true), default)).Result).Value);
        Assert.True(on.IsPublicSummaryVisible);

        var off = Assert.IsType<CaseReportDetail>(Assert.IsType<OkObjectResult>(
            (await ctrl.Update(orgId, caseId, created.Id,
                new UpsertCaseReportRequest("Investigation Report", "Summary", "Conclusion", null,
                    IsPublicSummaryVisible: false), default)).Result).Value);
        Assert.False(off.IsPublicSummaryVisible);
    }

    [Fact]
    public async Task An_edit_that_says_nothing_about_the_public_leaves_the_choice_alone()
    {
        // Every caller that predates the field sends null here. None of them should be able to
        // take a group's published finding off their public page as a side effect of a title edit.
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var ctrl = Build(factory, userId);
        var created = Assert.IsType<CaseReportDetail>(
            Assert.IsType<OkObjectResult>((await ctrl.Create(orgId, caseId, MakeRequest(), default)).Result).Value);

        await ctrl.Update(orgId, caseId, created.Id,
            new UpsertCaseReportRequest("Investigation Report", "Summary", "Conclusion", null,
                IsPublicSummaryVisible: true), default);

        var afterUnrelatedEdit = Assert.IsType<CaseReportDetail>(Assert.IsType<OkObjectResult>(
            (await ctrl.Update(orgId, caseId, created.Id,
                new UpsertCaseReportRequest("A better title", "Summary", "Conclusion", null), default)).Result).Value);

        Assert.True(afterUnrelatedEdit.IsPublicSummaryVisible);
        Assert.Equal("A better title", afterUnrelatedEdit.Title);
    }

    [Fact]
    public async Task The_leak_check_finds_the_clients_name_in_the_summary()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();

        var clientId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Users.Add(new AppUser
            {
                Id = clientId, UserName = "casey@t.com", NormalizedUserName = "CASEY@T.COM",
                Email = "casey@t.com", NormalizedEmail = "CASEY@T.COM",
                FirstName = "Casey", LastName = "Evaluator", DisplayName = "Casey Evaluator",
                DateCreated = DateTime.UtcNow,
            });
            var request = new ClientRequest
            {
                Id = Guid.NewGuid(), AppUserId = clientId, StreetAddress1 = "1 Main",
                City = "Nashville", State = "TN", ZipCode = "37201", Country = "US",
                Status = ClientRequestStatus.Assigned, DateCreated = DateTime.UtcNow,
                CreatedByAppUserId = clientId,
            };
            db.ClientRequests.Add(request);
            var tracked = await db.Cases.FirstAsync(c => c.Id == caseId);
            tracked.ClientRequestId = request.Id;
            await db.SaveChangesAsync();
        }

        var ctrl = Build(factory, userId);
        var created = Assert.IsType<CaseReportDetail>(Assert.IsType<OkObjectResult>(
            (await ctrl.Create(orgId, caseId,
                new UpsertCaseReportRequest("Report", "<p>We met Casey Evaluator on site.</p>", null, null),
                default)).Result).Value);

        var warnings = (IReadOnlyList<string>)Assert.IsType<OkObjectResult>(
            (await ctrl.PublicSummaryLeakCheck(orgId, caseId, created.Id, default)).Result).Value!;

        Assert.NotEmpty(warnings);

        // The sentence has to name the field the reader is looking at. This check is shared with
        // the case title, whose wording it was written for; telling somebody their "title"
        // contains a name while they are editing a summary sends them to the wrong screen.
        Assert.All(warnings, w => Assert.DoesNotContain("The title", w));
        Assert.Contains(warnings, w => w.Contains("summary"));
    }

    [Fact]
    public async Task The_leak_check_ignores_a_name_that_only_lives_in_markup()
    {
        // A name in an ATTRIBUTE — a link title, an image alt — is never shown to a visitor, so
        // warning about it is a warning nobody can act on. That is how a group learns to click
        // past all of them, including the real ones.
        var (factory, orgId, caseId, userId) = await SeedAsync();

        var clientId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Users.Add(new AppUser
            {
                Id = clientId, UserName = "casey2@t.com", NormalizedUserName = "CASEY2@T.COM",
                Email = "casey2@t.com", NormalizedEmail = "CASEY2@T.COM",
                FirstName = "Casey", LastName = "Evaluator", DisplayName = "Casey Evaluator",
                DateCreated = DateTime.UtcNow,
            });
            var request = new ClientRequest
            {
                Id = Guid.NewGuid(), AppUserId = clientId, StreetAddress1 = "1 Main",
                City = "Nashville", State = "TN", ZipCode = "37201", Country = "US",
                Status = ClientRequestStatus.Assigned, DateCreated = DateTime.UtcNow,
                CreatedByAppUserId = clientId,
            };
            db.ClientRequests.Add(request);
            var tracked = await db.Cases.FirstAsync(c => c.Id == caseId);
            tracked.ClientRequestId = request.Id;
            await db.SaveChangesAsync();
        }

        var ctrl = Build(factory, userId);
        var created = Assert.IsType<CaseReportDetail>(Assert.IsType<OkObjectResult>(
            (await ctrl.Create(orgId, caseId,
                new UpsertCaseReportRequest("Report",
                    "<p><img alt=\"Evaluator\" src=\"/x.png\" />The recordings had mundane sources.</p>",
                    null, null),
                default)).Result).Value);

        var warnings = Assert.IsType<OkObjectResult>(
            (await ctrl.PublicSummaryLeakCheck(orgId, caseId, created.Id, default)).Result).Value;

        Assert.Empty((IReadOnlyList<string>)warnings!);
    }

    [Fact]
    public async Task The_leak_check_is_quiet_about_prose_that_names_nobody()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var ctrl = Build(factory, userId);
        var created = Assert.IsType<CaseReportDetail>(Assert.IsType<OkObjectResult>(
            (await ctrl.Create(orgId, caseId,
                new UpsertCaseReportRequest("Report", "<p>Every recording had a mundane source.</p>", null, null),
                default)).Result).Value);

        var warnings = Assert.IsType<OkObjectResult>(
            (await ctrl.PublicSummaryLeakCheck(orgId, caseId, created.Id, default)).Result).Value;

        Assert.Empty((IReadOnlyList<string>)warnings!);
    }
}
