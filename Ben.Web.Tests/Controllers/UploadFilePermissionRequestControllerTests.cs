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

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Tests for <see cref="UploadFilePermissionRequestController"/>:
/// GetForFile (ordered by date desc), GetPendingForReviewer (only Pending, only owned files).
/// </summary>
public class UploadFilePermissionRequestControllerTests
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
        m.Setup(x => x.Map<IEnumerable<UploadFilePermissionRequestRecord>>(It.IsAny<object>()))
         .Returns<object>(o =>
         {
             if (o is not IEnumerable<UploadFilePermissionRequest> list) return [];
             return list.Select(r => new UploadFilePermissionRequestRecord
             {
                 Id = r.Id, UploadFileId = r.UploadFileId,
                 RequestedByAppUserId = r.RequestedByAppUserId,
                 PermissionType = r.PermissionType,
                 RequestStatus = r.RequestStatus,
             });
         });
        return m.Object;
    }

    private static UploadFilePermissionRequestController Build(
        IDbContextFactory<BenDataContext> factory)
    {
        var ctrl = new UploadFilePermissionRequestController(factory, CreateMapper());
        ctrl.ControllerContext = new ControllerContext
            { HttpContext = new DefaultHttpContext() };
        return ctrl;
    }

    private static async Task<Guid> SeedFileAsync(IDbContextFactory<BenDataContext> factory,
        Guid ownerId)
    {
        var fileId = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        db.UploadFiles.Add(new UploadFile
        {
            Id = fileId, UploadFileTypeId = Guid.NewGuid(), AppUserId = ownerId,
            FileName = "f.mp3", StoredFileName = "s.mp3", ContentType = "audio/mpeg",
            FileSize = 1, FileData = new byte[1],
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
        });
        await db.SaveChangesAsync();
        return fileId;
    }

    private static async Task<Guid> SeedRequestAsync(
        IDbContextFactory<BenDataContext> factory,
        Guid fileId, Guid requesterId,
        FilePermissionRequestStatus status = FilePermissionRequestStatus.Pending,
        DateTime? created = null)
    {
        var reqId = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        db.UploadFilePermissionRequests.Add(new UploadFilePermissionRequest
        {
            Id = reqId, UploadFileId = fileId, RequestedByAppUserId = requesterId,
            PermissionType = FilePermissionType.Use, RequestStatus = status,
            DateCreated = created ?? DateTime.UtcNow, CreatedByAppUserId = requesterId,
        });
        await db.SaveChangesAsync();
        return reqId;
    }

    // ── GetForFile ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetForFile_ReturnsEmpty_WhenNoRequests()
    {
        var factory = CreateFactory();
        var ctrl    = Build(factory);

        var result  = await ctrl.GetForFile(Guid.NewGuid(), default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Empty(Assert.IsAssignableFrom<IEnumerable<UploadFilePermissionRequestRecord>>(ok.Value));
    }

    [Fact]
    public async Task GetForFile_ReturnsAllStatuses_ForThatFile()
    {
        var factory     = CreateFactory();
        var ownerId     = Guid.NewGuid();
        var fileId      = await SeedFileAsync(factory, ownerId);
        var requesterId = Guid.NewGuid();
        await SeedRequestAsync(factory, fileId, requesterId, FilePermissionRequestStatus.Pending);
        await SeedRequestAsync(factory, fileId, requesterId, FilePermissionRequestStatus.Approved);
        var ctrl = Build(factory);

        var result = await ctrl.GetForFile(fileId, default);

        var ok   = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsAssignableFrom<IEnumerable<UploadFilePermissionRequestRecord>>(ok.Value)
                         .ToList();
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public async Task GetForFile_DoesNotIncludeRequestsForOtherFiles()
    {
        var factory = CreateFactory();
        var ownerId = Guid.NewGuid();
        var fileA   = await SeedFileAsync(factory, ownerId);
        var fileB   = await SeedFileAsync(factory, ownerId);
        await SeedRequestAsync(factory, fileB, Guid.NewGuid()); // request on fileB
        var ctrl = Build(factory);

        var result = await ctrl.GetForFile(fileA, default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Empty(Assert.IsAssignableFrom<IEnumerable<UploadFilePermissionRequestRecord>>(ok.Value));
    }

    // ── GetPendingForReviewer ─────────────────────────────────────────────────

    [Fact]
    public async Task GetPendingForReviewer_ReturnsEmpty_WhenOwnerHasNoFiles()
    {
        var factory    = CreateFactory();
        var reviewerId = Guid.NewGuid();
        var ctrl       = Build(factory);

        var result = await ctrl.GetPendingForReviewer(reviewerId, default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Empty(Assert.IsAssignableFrom<IEnumerable<UploadFilePermissionRequestRecord>>(ok.Value));
    }

    [Fact]
    public async Task GetPendingForReviewer_ReturnsPendingRequests_OnOwnedFiles()
    {
        var factory    = CreateFactory();
        var ownerId    = Guid.NewGuid();
        var fileId     = await SeedFileAsync(factory, ownerId);
        var requesterId = Guid.NewGuid();
        await SeedRequestAsync(factory, fileId, requesterId, FilePermissionRequestStatus.Pending);
        await SeedRequestAsync(factory, fileId, requesterId, FilePermissionRequestStatus.Approved); // not pending
        var ctrl = Build(factory);

        var result = await ctrl.GetPendingForReviewer(ownerId, default);

        var ok   = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsAssignableFrom<IEnumerable<UploadFilePermissionRequestRecord>>(ok.Value)
                         .ToList();
        Assert.Single(list);
        Assert.Equal(FilePermissionRequestStatus.Pending, list[0].RequestStatus);
    }

    [Fact]
    public async Task GetPendingForReviewer_DoesNotIncludeRequests_OnFilesOwnedByOthers()
    {
        var factory      = CreateFactory();
        var ownerA       = Guid.NewGuid();
        var ownerB       = Guid.NewGuid();
        var fileB        = await SeedFileAsync(factory, ownerB); // owned by B
        await SeedRequestAsync(factory, fileB, Guid.NewGuid(), FilePermissionRequestStatus.Pending);
        var ctrl = Build(factory);

        // Reviewer is A — should see no requests because they own no files
        var result = await ctrl.GetPendingForReviewer(ownerA, default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Empty(Assert.IsAssignableFrom<IEnumerable<UploadFilePermissionRequestRecord>>(ok.Value));
    }
}
