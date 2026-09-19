using AutoMapper;
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
using static Ben.Data.WebApi.Controllers.Entities.UploadFileShareController;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Tests for <see cref="UploadFileShareController"/>: GetSharesForFile, GetOrgFiles,
/// ShareWithOrg (new + reactivate existing), UpdateVisibility, RemoveShare.
/// <para>
/// Phase-A regression focus: every action here previously had no authorization at all beyond
/// [Authorize] — any authenticated user could share/retarget/delete anyone's file shares. Each
/// "happy path" test below now runs as the specific caller who should legitimately be allowed to
/// do that (file owner, active org member, admin-tier member), and each Forbid test asserts the
/// exact attacker shape that used to work: a caller with none of those relationships.
/// </para>
/// </summary>
public class UploadFileShareControllerTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var opts = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PooledDbContextFactory<BenDataContext>(opts);
    }

    private static IMapper CreateMapper()
    {
        var m = new Mock<IMapper>();
        m.Setup(x => x.Map<UploadFileOrganizationShareRecord>(It.IsAny<object>()))
         .Returns<object>(o =>
         {
             if (o is not UploadFileOrganizationShare s) return new UploadFileOrganizationShareRecord();
             return new UploadFileOrganizationShareRecord
             {
                 Id = s.Id, UploadFileId = s.UploadFileId,
                 OrganizationId = s.OrganizationId, IsActive = s.IsActive,
                 Visibility = s.Visibility,
             };
         });
        m.Setup(x => x.Map<IEnumerable<UploadFileOrganizationShareRecord>>(It.IsAny<object>()))
         .Returns<object>(o =>
         {
             if (o is not IEnumerable<UploadFileOrganizationShare> list) return [];
             return list.Select(s => new UploadFileOrganizationShareRecord
             {
                 Id = s.Id, UploadFileId = s.UploadFileId,
                 OrganizationId = s.OrganizationId, IsActive = s.IsActive,
             });
         });
        m.Setup(x => x.Map<IEnumerable<UploadFileRecord>>(It.IsAny<object>()))
         .Returns<object>(o =>
         {
             if (o is not IEnumerable<UploadFile> list) return [];
             return list.Select(f => new UploadFileRecord
             {
                 Id = f.Id, FileName = f.FileName, StoredFileName = f.StoredFileName, ContentType = f.ContentType
             });
         });
        return m.Object;
    }

    private static UploadFileShareController Build(IDbContextFactory<BenDataContext> factory, Guid userId, bool isSuperAdmin = false)
    {
        var ctrl = new UploadFileShareController(factory, CreateMapper(), new Mock<IAuditLogService>().Object, new Ben.Service.RepositoryService.Services.OrganizationSecurityService(factory));
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
        var fileId  = Guid.NewGuid();
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

    private static async Task<Guid> SeedShareAsync(
        IDbContextFactory<BenDataContext> factory,
        Guid fileId, Guid orgId, bool isActive = true,
        FileShareVisibility vis = FileShareVisibility.OrgAdminsOnly)
    {
        var shareId = Guid.NewGuid();
        var userId  = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        db.UploadFileOrganizationShares.Add(new UploadFileOrganizationShare
        {
            Id = shareId, UploadFileId = fileId, OrganizationId = orgId,
            SharedByAppUserId = userId, Visibility = vis, IsActive = isActive,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        await db.SaveChangesAsync();
        return shareId;
    }

    private static async Task AddMembershipAsync(
        IDbContextFactory<BenDataContext> factory, Guid orgId, Guid userId,
        OrganizationMemberRole role = OrganizationMemberRole.Member, bool isActive = true)
    {
        await using var db = await factory.CreateDbContextAsync();
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = userId,
            Role = role, IsActive = isActive, DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        await db.SaveChangesAsync();
    }

    // ── GetSharesForFile ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetSharesForFile_Owner_ReturnsOnlyActiveShares()
    {
        var (factory, ownerId, fileId) = await SeedFileAsync();
        var orgId1 = Guid.NewGuid();
        var orgId2 = Guid.NewGuid();
        await SeedShareAsync(factory, fileId, orgId1, isActive: true);
        await SeedShareAsync(factory, fileId, orgId2, isActive: false); // soft-deleted
        var ctrl = Build(factory, ownerId);

        var result = await ctrl.GetSharesForFile(fileId, default);

        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var shares = Assert.IsAssignableFrom<IEnumerable<UploadFileOrganizationShareRecord>>(ok.Value)
                           .ToList();
        Assert.Single(shares);
        Assert.Equal(orgId1, shares[0].OrganizationId);
    }

    [Fact]
    public async Task GetSharesForFile_SuperAdmin_CanViewAnyonesShares()
    {
        var (factory, _, fileId) = await SeedFileAsync();
        await SeedShareAsync(factory, fileId, Guid.NewGuid());
        var ctrl = Build(factory, Guid.NewGuid(), isSuperAdmin: true);

        var result = await ctrl.GetSharesForFile(fileId, default);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetSharesForFile_NotOwner_ReturnsForbid()
    {
        // The core of the fix: this used to return the file's full sharing configuration to any
        // authenticated caller, regardless of who owns it.
        var (factory, _, fileId) = await SeedFileAsync();
        await SeedShareAsync(factory, fileId, Guid.NewGuid());
        var ctrl = Build(factory, Guid.NewGuid());

        var result = await ctrl.GetSharesForFile(fileId, default);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task GetSharesForFile_FileNotFound_ReturnsNotFound()
    {
        var factory = CreateFactory();
        var ctrl    = Build(factory, Guid.NewGuid());

        var result  = await ctrl.GetSharesForFile(Guid.NewGuid(), default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    // ── GetOrgFiles ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetOrgFiles_NotAMember_ReturnsForbid()
    {
        // The core of the fix: previously any authenticated user could list every file shared
        // (at any visibility tier) into any org, regardless of membership.
        var factory = CreateFactory();
        var orgId   = Guid.NewGuid();
        await SeedShareAsync(factory, Guid.NewGuid(), orgId, vis: FileShareVisibility.Public);
        var ctrl = Build(factory, Guid.NewGuid());

        var result = await ctrl.GetOrgFiles(orgId, default);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task GetOrgFiles_PlainMember_ExcludesOrgAdminsOnlyShares()
    {
        var factory  = CreateFactory();
        var orgId    = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        await AddMembershipAsync(factory, orgId, memberId, OrganizationMemberRole.Member);

        var publicFileId = Guid.NewGuid();
        var adminFileId  = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFiles.Add(new UploadFile { Id = publicFileId, UploadFileTypeId = Guid.NewGuid(), AppUserId = Guid.NewGuid(), FileName = "a", StoredFileName = "a", ContentType = "x", FileSize = 1, DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid() });
            db.UploadFiles.Add(new UploadFile { Id = adminFileId, UploadFileTypeId = Guid.NewGuid(), AppUserId = Guid.NewGuid(), FileName = "b", StoredFileName = "b", ContentType = "x", FileSize = 1, DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid() });
            await db.SaveChangesAsync();
        }
        await SeedShareAsync(factory, publicFileId, orgId, vis: FileShareVisibility.OrgMembers);
        await SeedShareAsync(factory, adminFileId, orgId, vis: FileShareVisibility.OrgAdminsOnly);

        var ctrl = Build(factory, memberId);
        var result = await ctrl.GetOrgFiles(orgId, default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var files = Assert.IsAssignableFrom<IEnumerable<UploadFileRecord>>(ok.Value).ToList();
        Assert.Single(files);
        Assert.Equal(publicFileId, files[0].Id);
    }

    [Fact]
    public async Task GetOrgFiles_AdminTierMember_SeesOrgAdminsOnlyShares()
    {
        var factory  = CreateFactory();
        var orgId    = Guid.NewGuid();
        var adminId  = Guid.NewGuid();
        await AddMembershipAsync(factory, orgId, adminId, OrganizationMemberRole.Administrator);

        var adminFileId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFiles.Add(new UploadFile { Id = adminFileId, UploadFileTypeId = Guid.NewGuid(), AppUserId = Guid.NewGuid(), FileName = "b", StoredFileName = "b", ContentType = "x", FileSize = 1, DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid() });
            await db.SaveChangesAsync();
        }
        await SeedShareAsync(factory, adminFileId, orgId, vis: FileShareVisibility.OrgAdminsOnly);

        var ctrl = Build(factory, adminId);
        var result = await ctrl.GetOrgFiles(orgId, default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var files = Assert.IsAssignableFrom<IEnumerable<UploadFileRecord>>(ok.Value).ToList();
        Assert.Single(files);
        Assert.Equal(adminFileId, files[0].Id);
    }

    [Fact]
    public async Task GetOrgFiles_InactiveMembership_ReturnsForbid()
    {
        var factory  = CreateFactory();
        var orgId    = Guid.NewGuid();
        var userId   = Guid.NewGuid();
        await AddMembershipAsync(factory, orgId, userId, isActive: false);

        var ctrl = Build(factory, userId);
        var result = await ctrl.GetOrgFiles(orgId, default);

        Assert.IsType<ForbidResult>(result.Result);
    }

    // ── ShareWithOrg ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ShareWithOrg_Owner_Creates201()
    {
        var (factory, ownerId, fileId) = await SeedFileAsync();
        var orgId = Guid.NewGuid();
        var ctrl  = Build(factory, ownerId);

        var result = await ctrl.ShareWithOrg(fileId,
            new ShareWithOrgRequest(orgId, FileShareVisibility.OrgMembers), default);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var share   = Assert.IsType<UploadFileOrganizationShareRecord>(created.Value);
        Assert.Equal(fileId, share.UploadFileId);
        Assert.Equal(orgId,  share.OrganizationId);
        Assert.True(share.IsActive);
    }

    [Fact]
    public async Task ShareWithOrg_SuperAdmin_CanShareAnyonesFile()
    {
        var (factory, _, fileId) = await SeedFileAsync();
        var ctrl = Build(factory, Guid.NewGuid(), isSuperAdmin: true);

        var result = await ctrl.ShareWithOrg(fileId,
            new ShareWithOrgRequest(Guid.NewGuid(), FileShareVisibility.OrgMembers), default);

        Assert.IsType<CreatedAtActionResult>(result.Result);
    }

    [Fact]
    public async Task ShareWithOrg_NotOwner_ReturnsForbid()
    {
        // The core of the fix: this used to let any authenticated caller share someone else's
        // private file into any org, with SharedByAppUserId spoofable in the body.
        var (factory, _, fileId) = await SeedFileAsync();
        var ctrl = Build(factory, Guid.NewGuid());

        var result = await ctrl.ShareWithOrg(fileId,
            new ShareWithOrgRequest(Guid.NewGuid(), FileShareVisibility.OrgMembers), default);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task ShareWithOrg_FileNotFound_ReturnsNotFound()
    {
        var factory = CreateFactory();
        var ctrl    = Build(factory, Guid.NewGuid());

        var result = await ctrl.ShareWithOrg(Guid.NewGuid(),
            new ShareWithOrgRequest(Guid.NewGuid(), FileShareVisibility.OrgMembers), default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task ShareWithOrg_Owner_Reactivates_WhenSoftDeletedShareExists()
    {
        var (factory, ownerId, fileId) = await SeedFileAsync();
        var orgId   = Guid.NewGuid();
        var shareId = await SeedShareAsync(factory, fileId, orgId, isActive: false);
        var ctrl    = Build(factory, ownerId);

        var result = await ctrl.ShareWithOrg(fileId,
            new ShareWithOrgRequest(orgId, FileShareVisibility.Public), default);

        var ok    = Assert.IsType<OkObjectResult>(result.Result);
        var share = Assert.IsType<UploadFileOrganizationShareRecord>(ok.Value);
        Assert.Equal(shareId, share.Id);
        Assert.True(share.IsActive);
        Assert.Equal(FileShareVisibility.Public, share.Visibility);
    }

    [Fact]
    public async Task ShareWithOrg_ConcurrentFirstShares_BothSucceedWithExactlyOneRow()
    {
        // Regression for the check-then-insert race on (UploadFileId, OrganizationId): two
        // concurrent first-time shares used to be able to both pass the "not yet shared" check
        // and both try to insert, so the loser hit an unhandled DbUpdateException (raw 500) from
        // the unique index. The fix catches that and reconciles onto the winning row instead.
        var (factory, ownerId, fileId) = await SeedFileAsync();
        var orgId = Guid.NewGuid();
        var ctrl1 = Build(factory, ownerId);
        var ctrl2 = Build(factory, ownerId);

        var results = await Task.WhenAll(
            ctrl1.ShareWithOrg(fileId, new ShareWithOrgRequest(orgId, FileShareVisibility.OrgMembers), default),
            ctrl2.ShareWithOrg(fileId, new ShareWithOrgRequest(orgId, FileShareVisibility.Public), default));

        Assert.All(results, r => Assert.True(r.Result is CreatedAtActionResult or OkObjectResult));

        await using var verify = await factory.CreateDbContextAsync();
        var shares = await verify.UploadFileOrganizationShares
            .Where(s => s.UploadFileId == fileId && s.OrganizationId == orgId)
            .ToListAsync();
        Assert.Single(shares);
    }

    // ── UpdateVisibility ──────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateVisibility_ReturnsNotFound_WhenShareMissing()
    {
        var factory = CreateFactory();
        var ctrl    = Build(factory, Guid.NewGuid());

        var result  = await ctrl.UpdateVisibility(Guid.NewGuid(),
            new UpdateVisibilityRequest(FileShareVisibility.Public), default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task UpdateVisibility_AdminTierMember_ChangesVisibility()
    {
        var factory  = CreateFactory();
        var fileId   = Guid.NewGuid();
        var orgId    = Guid.NewGuid();
        var adminId  = Guid.NewGuid();
        await AddMembershipAsync(factory, orgId, adminId, OrganizationMemberRole.Administrator);
        var shareId  = await SeedShareAsync(factory, fileId, orgId, vis: FileShareVisibility.OrgAdminsOnly);
        var ctrl     = Build(factory, adminId);

        var result = await ctrl.UpdateVisibility(shareId,
            new UpdateVisibilityRequest(FileShareVisibility.Public), default);

        var ok    = Assert.IsType<OkObjectResult>(result.Result);
        var share = Assert.IsType<UploadFileOrganizationShareRecord>(ok.Value);
        Assert.Equal(FileShareVisibility.Public, share.Visibility);
    }

    [Fact]
    public async Task UpdateVisibility_PlainMember_ReturnsForbid()
    {
        // Ordinary (non-admin-tier) members can see shares but shouldn't be able to retarget
        // their visibility — that's an org-policy decision reserved for admin-tier members.
        var factory  = CreateFactory();
        var fileId   = Guid.NewGuid();
        var orgId    = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        await AddMembershipAsync(factory, orgId, memberId, OrganizationMemberRole.Member);
        var shareId  = await SeedShareAsync(factory, fileId, orgId);
        var ctrl     = Build(factory, memberId);

        var result = await ctrl.UpdateVisibility(shareId,
            new UpdateVisibilityRequest(FileShareVisibility.Public), default);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task UpdateVisibility_NotAMember_ReturnsForbid()
    {
        // The core of the fix: this action had zero authorization at all previously.
        var factory = CreateFactory();
        var shareId = await SeedShareAsync(factory, Guid.NewGuid(), Guid.NewGuid());
        var ctrl    = Build(factory, Guid.NewGuid());

        var result = await ctrl.UpdateVisibility(shareId,
            new UpdateVisibilityRequest(FileShareVisibility.Public), default);

        Assert.IsType<ForbidResult>(result.Result);
    }

    // ── RemoveShare ───────────────────────────────────────────────────────────

    [Fact]
    public async Task RemoveShare_ReturnsNotFound_WhenShareMissing()
    {
        var factory = CreateFactory();
        var ctrl    = Build(factory, Guid.NewGuid());

        var result  = await ctrl.RemoveShare(Guid.NewGuid(), default);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task RemoveShare_FileOwner_SoftDeletesShare_AndReturnsNoContent()
    {
        var (factory, ownerId, fileId) = await SeedFileAsync();
        var orgId   = Guid.NewGuid();
        var shareId = await SeedShareAsync(factory, fileId, orgId, isActive: true);
        var ctrl    = Build(factory, ownerId);

        var result  = await ctrl.RemoveShare(shareId, default);

        Assert.IsType<NoContentResult>(result);

        await using var db = await factory.CreateDbContextAsync();
        var saved = await db.UploadFileOrganizationShares.FindAsync(shareId);
        Assert.NotNull(saved);
        Assert.False(saved!.IsActive);       // soft-deleted, not hard-deleted
        Assert.Equal(ownerId, saved.RemovedByAppUserId);
        Assert.NotNull(saved.RemovalDate);
    }

    [Fact]
    public async Task RemoveShare_AdminTierMember_CanRemove_EvenIfNotFileOwner()
    {
        var (factory, _, fileId) = await SeedFileAsync();
        var orgId   = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        await AddMembershipAsync(factory, orgId, adminId, OrganizationMemberRole.Administrator);
        var shareId = await SeedShareAsync(factory, fileId, orgId);
        var ctrl    = Build(factory, adminId);

        var result = await ctrl.RemoveShare(shareId, default);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task RemoveShare_NeitherOwnerNorAdminTierMember_ReturnsForbid()
    {
        // The core of the fix: this used to have zero authorization — any authenticated caller
        // could delete any org's file shares given only the shareId.
        var (factory, _, fileId) = await SeedFileAsync();
        var orgId   = Guid.NewGuid();
        var shareId = await SeedShareAsync(factory, fileId, orgId);
        var ctrl    = Build(factory, Guid.NewGuid());

        var result = await ctrl.RemoveShare(shareId, default);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task RemoveShare_PlainMember_NotFileOwner_ReturnsForbid()
    {
        var (factory, _, fileId) = await SeedFileAsync();
        var orgId    = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        await AddMembershipAsync(factory, orgId, memberId, OrganizationMemberRole.Member);
        var shareId  = await SeedShareAsync(factory, fileId, orgId);
        var ctrl     = Build(factory, memberId);

        var result = await ctrl.RemoveShare(shareId, default);

        Assert.IsType<ForbidResult>(result);
    }
}
