using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Tests for CaseResearchController — org note/link/file entries on a case.
/// </summary>
public class CaseResearchControllerTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var options = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PooledDbContextFactory<BenDataContext>(options);
    }

    private static CaseResearchController BuildController(IDbContextFactory<BenDataContext> factory, Guid userId,
        bool isSuperAdmin = false)
    {
        var storage = new Mock<IFileStorageService>();
        storage.Setup(s => s.CaseFilePath(It.IsAny<Guid>(), It.IsAny<string>())).Returns("fake/path");
        storage.Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
               .Returns(Task.CompletedTask);
        storage.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .Returns(Task.CompletedTask);

        var ctrl = new CaseResearchController(factory, storage.Object, new Ben.Service.RepositoryService.Services.OrganizationSecurityService(factory));
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    isSuperAdmin
                        ? [new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                           new Claim(ClaimTypes.Role, Ben.Data.Common.Constants.RoleNames.SuperAdmin)]
                        : [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
                    "Bearer", ClaimTypes.NameIdentifier, ClaimTypes.Role))
            }
        };
        return ctrl;
    }

    private static async Task<(IDbContextFactory<BenDataContext>, Guid orgId, Guid caseId, Guid userId)> SeedAsync()
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
        await TestSeeds.BridgeAsync(factory, orgId);
        return (factory, orgId, caseId, userId);
    }

    // ── GetAll ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_SuperAdminNonMember_IsNotForbidden()
    {
        // Same bypass rule as CaseFileController — see its SuperAdmin test for the 2026-08-22 bug.
        var (factory, orgId, caseId, _) = await SeedAsync();
        var ctrl = BuildController(factory, Guid.NewGuid(), isSuperAdmin: true);

        var result = await ctrl.GetAll(orgId, caseId, default);

        Assert.IsNotType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task GetAll_NonMember_ReturnsForbid()
    {
        var (factory, orgId, caseId, _) = await SeedAsync();
        var ctrl = BuildController(factory, Guid.NewGuid());

        var result = await ctrl.GetAll(orgId, caseId, default);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task GetAll_Member_ReturnsEmptyList()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var ctrl = BuildController(factory, userId);

        var result = await ctrl.GetAll(orgId, caseId, default);

        var ok   = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsAssignableFrom<IEnumerable<CaseResearchEntryDto>>(ok.Value);
        Assert.Empty(list);
    }

    // ── Create ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_Note_ReturnsOkWithEntry()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var ctrl    = BuildController(factory, userId);
        var request = new UpsertResearchRequest(CaseResearchType.Note, "Background History", "Found newspaper clippings.", null);

        var result = await ctrl.Create(orgId, caseId, request, default);

        var ok  = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<CaseResearchEntryDto>(ok.Value);
        Assert.Equal("Background History", dto.Title);
        Assert.Equal(CaseResearchType.Note, dto.ResearchType);
        Assert.Equal(10, dto.SortOrder);
    }

    [Fact]
    public async Task Create_Link_PersistsUrl()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var ctrl    = BuildController(factory, userId);
        var request = new UpsertResearchRequest(CaseResearchType.Link, "Local News Story", null, "https://example.com/news");

        var result = await ctrl.Create(orgId, caseId, request, default);

        var ok  = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<CaseResearchEntryDto>(ok.Value);
        Assert.Equal("https://example.com/news", dto.Url);
    }

    [Fact]
    public async Task Create_SortOrder_IncrementsBy10()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var ctrl = BuildController(factory, userId);

        await ctrl.Create(orgId, caseId, new UpsertResearchRequest(CaseResearchType.Note, "First", null, null), default);
        var result = await ctrl.Create(orgId, caseId, new UpsertResearchRequest(CaseResearchType.Note, "Second", null, null), default);

        var ok  = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<CaseResearchEntryDto>(ok.Value);
        Assert.Equal(20, dto.SortOrder);
    }

    [Fact]
    public async Task Create_CaseNotFound_ReturnsNotFound()
    {
        var (factory, orgId, _, userId) = await SeedAsync();
        var ctrl = BuildController(factory, userId);

        var result = await ctrl.Create(orgId, Guid.NewGuid(),
            new UpsertResearchRequest(CaseResearchType.Note, "X", null, null), default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    // ── Update ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_ExistingEntry_ReturnsUpdatedDto()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var ctrl = BuildController(factory, userId);
        var create = await ctrl.Create(orgId, caseId,
            new UpsertResearchRequest(CaseResearchType.Note, "Original", "Body", null), default);
        var entryId = ((CaseResearchEntryDto)((OkObjectResult)create.Result!).Value!).Id;

        var result = await ctrl.Update(orgId, caseId, entryId,
            new UpsertResearchRequest(CaseResearchType.Note, "Updated", "New body", null), default);

        var ok  = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<CaseResearchEntryDto>(ok.Value);
        Assert.Equal("Updated", dto.Title);
        Assert.Equal("New body", dto.Body);
    }

    [Fact]
    public async Task Update_MissingEntry_ReturnsNotFound()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var ctrl = BuildController(factory, userId);

        var result = await ctrl.Update(orgId, caseId, Guid.NewGuid(),
            new UpsertResearchRequest(CaseResearchType.Note, "X", null, null), default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_ExistingEntry_ReturnsNoContent()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var ctrl = BuildController(factory, userId);
        var create = await ctrl.Create(orgId, caseId,
            new UpsertResearchRequest(CaseResearchType.Note, "To delete", null, null), default);
        var entryId = ((CaseResearchEntryDto)((OkObjectResult)create.Result!).Value!).Id;

        var result = await ctrl.Delete(orgId, caseId, entryId, default);

        Assert.IsType<NoContentResult>(result);
        await using var db = await factory.CreateDbContextAsync();
        Assert.False(await db.CaseResearchEntries.AnyAsync(e => e.Id == entryId));
    }

    [Fact]
    public async Task Delete_MissingEntry_ReturnsNotFound()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var ctrl = BuildController(factory, userId);

        var result = await ctrl.Delete(orgId, caseId, Guid.NewGuid(), default);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Delete_NonMember_ReturnsForbid()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var ctrl = BuildController(factory, userId);
        var create = await ctrl.Create(orgId, caseId,
            new UpsertResearchRequest(CaseResearchType.Note, "X", null, null), default);
        var entryId = ((CaseResearchEntryDto)((OkObjectResult)create.Result!).Value!).Id;

        var nonMemberCtrl = BuildController(factory, Guid.NewGuid());
        var result = await nonMemberCtrl.Delete(orgId, caseId, entryId, default);

        Assert.IsType<ForbidResult>(result);
    }

    // ── Cross-org chain (Phase B) ────────────────────────────────────────────

    [Fact]
    public async Task GetAllUpdateDelete_CaseBelongsToDifferentOrg_ReturnsNotFound()
    {
        // The core of the fix: GetAll/Update/Delete checked org membership but never that caseId
        // actually belonged to the route orgId — a member of their OWN org could reach another
        // org's research entries just by knowing the caseId/entryId.
        var (factory, victimOrgId, victimCaseId, victimUserId) = await SeedAsync();
        var victim  = BuildController(factory, victimUserId);
        var created = await victim.Create(victimOrgId, victimCaseId,
            new UpsertResearchRequest(CaseResearchType.Note, "Private", "Secret", null), default);
        var entryId = ((CaseResearchEntryDto)((OkObjectResult)created.Result!).Value!).Id;

        var attackerOrgId = Guid.NewGuid();
        var attackerId    = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Organizations.Add(new Organization { Id = attackerOrgId, Name = "Attacker Org", UrlName = "attacker", DateCreated = DateTime.UtcNow, CreatedByAppUserId = attackerId });
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership { Id = Guid.NewGuid(), OrganizationId = attackerOrgId, AppUserId = attackerId, Role = OrganizationMemberRole.Manager, IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = attackerId });
            await db.SaveChangesAsync();
        }
        await TestSeeds.BridgeAsync(factory, attackerOrgId);
        var attacker = BuildController(factory, attackerId);

        Assert.IsType<NotFoundResult>((await attacker.GetAll(attackerOrgId, victimCaseId, default)).Result);
        Assert.IsType<NotFoundResult>((await attacker.Update(attackerOrgId, victimCaseId, entryId,
            new UpsertResearchRequest(CaseResearchType.Note, "Hijacked", null, null), default)).Result);
        Assert.IsType<NotFoundResult>(await attacker.Delete(attackerOrgId, victimCaseId, entryId, default));

        await using var verifyDb = await factory.CreateDbContextAsync();
        Assert.True(await verifyDb.CaseResearchEntries.AnyAsync(e => e.Id == entryId && e.Title == "Private"));
    }
}
