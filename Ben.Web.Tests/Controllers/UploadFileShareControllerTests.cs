using AutoMapper;
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
using Xunit;
using static Ben.Data.WebApi.Controllers.Entities.UploadFileShareController;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Tests for <see cref="UploadFileShareController"/>:
/// GetSharesForFile, ShareWithOrg (new + reactivate existing), UpdateVisibility, RemoveShare.
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
         .Returns<object>(_ => []);
        return m.Object;
    }

    private static UploadFileShareController Build(IDbContextFactory<BenDataContext> factory)
    {
        var ctrl = new UploadFileShareController(factory, CreateMapper(), new Mock<IAuditLogService>().Object);
        ctrl.ControllerContext = new ControllerContext
            { HttpContext = new DefaultHttpContext() };
        return ctrl;
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

    // ── GetSharesForFile ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetSharesForFile_ReturnsOnlyActiveShares()
    {
        var factory = CreateFactory();
        var fileId  = Guid.NewGuid();
        var orgId1  = Guid.NewGuid();
        var orgId2  = Guid.NewGuid();
        await SeedShareAsync(factory, fileId, orgId1, isActive: true);
        await SeedShareAsync(factory, fileId, orgId2, isActive: false); // soft-deleted
        var ctrl = Build(factory);

        var result = await ctrl.GetSharesForFile(fileId, default);

        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var shares = Assert.IsAssignableFrom<IEnumerable<UploadFileOrganizationShareRecord>>(ok.Value)
                           .ToList();
        Assert.Single(shares);
        Assert.Equal(orgId1, shares[0].OrganizationId);
    }

    [Fact]
    public async Task GetSharesForFile_ReturnsEmpty_WhenNoShares()
    {
        var factory = CreateFactory();
        var ctrl    = Build(factory);

        var result  = await ctrl.GetSharesForFile(Guid.NewGuid(), default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Empty(Assert.IsAssignableFrom<IEnumerable<UploadFileOrganizationShareRecord>>(ok.Value));
    }

    // ── ShareWithOrg ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ShareWithOrg_Creates201_WhenNotAlreadyShared()
    {
        var factory   = CreateFactory();
        var fileId    = Guid.NewGuid();
        var orgId     = Guid.NewGuid();
        var sharedBy  = Guid.NewGuid();
        var ctrl      = Build(factory);

        var result = await ctrl.ShareWithOrg(fileId,
            new ShareWithOrgRequest(orgId, sharedBy, FileShareVisibility.OrgMembers), default);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var share   = Assert.IsType<UploadFileOrganizationShareRecord>(created.Value);
        Assert.Equal(fileId, share.UploadFileId);
        Assert.Equal(orgId,  share.OrganizationId);
        Assert.True(share.IsActive);
    }

    [Fact]
    public async Task ShareWithOrg_Reactivates_WhenSoftDeletedShareExists()
    {
        var factory  = CreateFactory();
        var fileId   = Guid.NewGuid();
        var orgId    = Guid.NewGuid();
        var shareId  = await SeedShareAsync(factory, fileId, orgId, isActive: false);
        var ctrl     = Build(factory);

        var result = await ctrl.ShareWithOrg(fileId,
            new ShareWithOrgRequest(orgId, Guid.NewGuid(), FileShareVisibility.Public), default);

        var ok    = Assert.IsType<OkObjectResult>(result.Result);
        var share = Assert.IsType<UploadFileOrganizationShareRecord>(ok.Value);
        Assert.Equal(shareId, share.Id);
        Assert.True(share.IsActive);
        Assert.Equal(FileShareVisibility.Public, share.Visibility);
    }

    // ── UpdateVisibility ──────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateVisibility_ReturnsNotFound_WhenShareMissing()
    {
        var factory = CreateFactory();
        var ctrl    = Build(factory);

        var result  = await ctrl.UpdateVisibility(Guid.NewGuid(),
            new UpdateVisibilityRequest(FileShareVisibility.Public, Guid.NewGuid()), default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task UpdateVisibility_ChangesVisibility()
    {
        var factory  = CreateFactory();
        var fileId   = Guid.NewGuid();
        var orgId    = Guid.NewGuid();
        var shareId  = await SeedShareAsync(factory, fileId, orgId,
            vis: FileShareVisibility.OrgAdminsOnly);
        var ctrl     = Build(factory);

        var result   = await ctrl.UpdateVisibility(shareId,
            new UpdateVisibilityRequest(FileShareVisibility.Public, Guid.NewGuid()), default);

        var ok    = Assert.IsType<OkObjectResult>(result.Result);
        var share = Assert.IsType<UploadFileOrganizationShareRecord>(ok.Value);
        Assert.Equal(FileShareVisibility.Public, share.Visibility);
    }

    // ── RemoveShare ───────────────────────────────────────────────────────────

    [Fact]
    public async Task RemoveShare_ReturnsNotFound_WhenShareMissing()
    {
        var factory = CreateFactory();
        var ctrl    = Build(factory);

        var result  = await ctrl.RemoveShare(Guid.NewGuid(), Guid.NewGuid(), default);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task RemoveShare_SoftDeletesShare_AndReturnsNoContent()
    {
        var factory  = CreateFactory();
        var fileId   = Guid.NewGuid();
        var orgId    = Guid.NewGuid();
        var shareId  = await SeedShareAsync(factory, fileId, orgId, isActive: true);
        var ctrl     = Build(factory);

        var result   = await ctrl.RemoveShare(shareId, Guid.NewGuid(), default);

        Assert.IsType<NoContentResult>(result);

        await using var db   = await factory.CreateDbContextAsync();
        var saved            = await db.UploadFileOrganizationShares.FindAsync(shareId);
        Assert.NotNull(saved);
        Assert.False(saved!.IsActive);       // soft-deleted, not hard-deleted
        Assert.NotNull(saved.RemovedByAppUserId);
        Assert.NotNull(saved.RemovalDate);
    }
}
