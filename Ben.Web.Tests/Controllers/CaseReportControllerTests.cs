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
        var ctrl = new CaseReportController(factory);
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
}
