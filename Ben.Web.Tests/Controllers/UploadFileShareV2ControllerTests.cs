using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Tests for UploadFileShareV2Controller — the generalized 4-target sharing model
/// (person / investigation team / organization / public) for the universal media library.
/// </summary>
public class UploadFileShareV2ControllerTests
{
    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var opts = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PooledDbContextFactory<BenDataContext>(opts);
    }

    private static UploadFileShareV2Controller Build(IDbContextFactory<BenDataContext> factory, Guid userId, bool isSuperAdmin = false)
    {
        var ctrl = new UploadFileShareV2Controller(factory, new Mock<IAuditLogService>().Object, new Ben.Service.RepositoryService.Services.OrganizationSecurityService(factory));
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
        if (isSuperAdmin) claims.Add(new Claim(ClaimTypes.Role, RoleNames.SuperAdmin));
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer")) }
        };
        return ctrl;
    }

    private static async Task<(IDbContextFactory<BenDataContext> Factory, Guid OwnerId, Guid FileId)> SeedFileAsync()
    {
        var factory = CreateFactory();
        var ownerId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        db.UploadFiles.Add(new UploadFile
        {
            Id = fileId, UploadFileTypeId = Guid.NewGuid(), AppUserId = ownerId,
            FileName = "evidence.jpg", StoredFileName = "s.jpg", ContentType = "image/jpeg",
            FileSize = 100, DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
        });
        await db.SaveChangesAsync();
        return (factory, ownerId, fileId);
    }

    // ── CreateShare ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateShare_Person_Succeeds()
    {
        var (factory, ownerId, fileId) = await SeedFileAsync();
        var targetId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.AppUsers.Add(new AppUser { Id = targetId, UserName = "t@t.com", NormalizedUserName = "T@T.COM", Email = "t@t.com", NormalizedEmail = "T@T.COM", DateCreated = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var ctrl = Build(factory, ownerId);
        var result = await ctrl.CreateShare(fileId, new CreateShareRequest(ShareTargetType.Person, targetId, null, null), default);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var record = Assert.IsType<UploadFileShareRecord>(created.Value);
        Assert.Equal(ShareTargetType.Person, record.TargetType);
        Assert.Equal(targetId, record.TargetAppUserId);
    }

    [Fact]
    public async Task CreateShare_Public_Succeeds_WithNoTargetFields()
    {
        var (factory, ownerId, fileId) = await SeedFileAsync();
        var ctrl = Build(factory, ownerId);

        var result = await ctrl.CreateShare(fileId, new CreateShareRequest(ShareTargetType.Public, null, null, null), default);

        Assert.IsType<CreatedAtActionResult>(result.Result);
    }

    [Fact]
    public async Task CreateShare_Person_MismatchedTargetField_ReturnsBadRequest()
    {
        var (factory, ownerId, fileId) = await SeedFileAsync();
        var ctrl = Build(factory, ownerId);

        // Person share but no TargetAppUserId set
        var result = await ctrl.CreateShare(fileId, new CreateShareRequest(ShareTargetType.Person, null, null, null), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task CreateShare_Organization_UnknownOrg_ReturnsBadRequest()
    {
        var (factory, ownerId, fileId) = await SeedFileAsync();
        var ctrl = Build(factory, ownerId);

        var result = await ctrl.CreateShare(fileId, new CreateShareRequest(ShareTargetType.Organization, null, null, Guid.NewGuid()), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task CreateShare_InvestigationTeam_NotOrgMember_ReturnsForbid()
    {
        var (factory, ownerId, fileId) = await SeedFileAsync();
        var orgId = Guid.NewGuid();
        var caseId = Guid.NewGuid();
        var invId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Organizations.Add(new Organization { Id = orgId, Name = "Org", UrlName = "org", DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId });
            db.Cases.Add(new Case
            {
                Id = caseId, OrganizationId = orgId, Title = "Case", CaseYear = 2026, OrgCaseNumber = 1,
                StreetAddress1 = "1 Main", City = "Nashville", State = "TN", ZipCode = "37201", Country = "US",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            db.Investigations.Add(new Investigation
            {
                Id = invId, CaseId = caseId, Title = "Investigation", ScheduledDateTime = DateTime.UtcNow,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            await db.SaveChangesAsync();
        }

        var ctrl = Build(factory, ownerId); // owns the file but is not a member of the investigation's org
        var result = await ctrl.CreateShare(fileId, new CreateShareRequest(ShareTargetType.InvestigationTeam, null, invId, null), default);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task CreateShare_NotFileOwner_ReturnsForbid()
    {
        var (factory, _, fileId) = await SeedFileAsync();
        var ctrl = Build(factory, Guid.NewGuid());

        var result = await ctrl.CreateShare(fileId, new CreateShareRequest(ShareTargetType.Public, null, null, null), default);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task CreateShare_SuperAdmin_CanShareFileTheyDontOwn()
    {
        var (factory, _, fileId) = await SeedFileAsync();
        var ctrl = Build(factory, Guid.NewGuid(), isSuperAdmin: true);

        var result = await ctrl.CreateShare(fileId, new CreateShareRequest(ShareTargetType.Public, null, null, null), default);

        Assert.IsType<CreatedAtActionResult>(result.Result);
    }

    // ── GetShares ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetShares_OwnerSeesActiveShares()
    {
        var (factory, ownerId, fileId) = await SeedFileAsync();
        var ctrl = Build(factory, ownerId);
        await ctrl.CreateShare(fileId, new CreateShareRequest(ShareTargetType.Public, null, null, null), default);

        var result = await ctrl.GetShares(fileId, default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsAssignableFrom<IEnumerable<UploadFileShareRecord>>(ok.Value);
        Assert.Single(list);
    }

    [Fact]
    public async Task GetShares_NotOwner_ReturnsForbid()
    {
        var (factory, _, fileId) = await SeedFileAsync();
        var ctrl = Build(factory, Guid.NewGuid());

        var result = await ctrl.GetShares(fileId, default);

        Assert.IsType<ForbidResult>(result.Result);
    }

    // ── RemoveShare ───────────────────────────────────────────────────────────

    [Fact]
    public async Task RemoveShare_Owner_SoftDeletes()
    {
        var (factory, ownerId, fileId) = await SeedFileAsync();
        var ctrl = Build(factory, ownerId);
        var created = (UploadFileShareRecord)((CreatedAtActionResult)(await ctrl.CreateShare(
            fileId, new CreateShareRequest(ShareTargetType.Public, null, null, null), default)).Result!).Value!;

        var result = await ctrl.RemoveShare(created.Id, default);

        Assert.IsType<NoContentResult>(result);
        await using var db = await factory.CreateDbContextAsync();
        var share = await db.UploadFileShares.FirstAsync(s => s.Id == created.Id);
        Assert.False(share.IsActive);

        // Removed shares shouldn't appear in GetShares anymore
        var afterRemoval = await ctrl.GetShares(fileId, default);
        var ok = Assert.IsType<OkObjectResult>(afterRemoval.Result);
        Assert.Empty((IEnumerable<UploadFileShareRecord>)ok.Value!);
    }

    [Fact]
    public async Task RemoveShare_NotOwner_ReturnsForbid()
    {
        var (factory, ownerId, fileId) = await SeedFileAsync();
        var owner = Build(factory, ownerId);
        var created = (UploadFileShareRecord)((CreatedAtActionResult)(await owner.CreateShare(
            fileId, new CreateShareRequest(ShareTargetType.Public, null, null, null), default)).Result!).Value!;

        var other = Build(factory, Guid.NewGuid());
        var result = await other.RemoveShare(created.Id, default);

        Assert.IsType<ForbidResult>(result);
    }
}
