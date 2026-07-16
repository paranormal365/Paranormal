using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.Common.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Ben.Service.RepositoryService.Tests;

public class UploadFileEntityTests
{
    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var options = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PooledDbContextFactory<BenDataContext>(options);
    }

    // ── UploadFileType ────────────────────────────────────────────────────────

    [Fact]
    public async Task UploadFileType_CanBeCreatedAndRetrieved()
    {
        var factory   = CreateFactory();
        var creatorId = Guid.NewGuid();
        var typeId    = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFileTypes.Add(new UploadFileType
            {
                Id = typeId,
                Name = "Document",
                IsActive = true,
                IsPublic = true,
                SortOrder = 1,
                DateCreated = DateTime.UtcNow,
                CreatedByAppUserId = creatorId
            });
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var type = await db.UploadFileTypes.FindAsync(typeId);
            Assert.NotNull(type);
            Assert.Equal("Document", type.Name);
            Assert.True(type.IsActive);
        }
    }

    // ── UploadFile ────────────────────────────────────────────────────────────

    [Fact]
    public async Task UploadFile_CanBeCreatedAndRetrieved()
    {
        var factory  = CreateFactory();
        var fileId   = Guid.NewGuid();
        var typeId   = Guid.NewGuid();
        var userId   = Guid.NewGuid();
        var fileData = new byte[] { 1, 2, 3, 4, 5 };

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFiles.Add(new UploadFile
            {
                Id = fileId,
                UploadFileTypeId = typeId,
                AppUserId = userId,
                FileName = "test.pdf",
                StoredFileName = $"{Guid.NewGuid()}.pdf",
                ContentType = "application/pdf",
                FileSize = fileData.Length,
                FileData = fileData,
                Description = "Test file",
                IsPublic = false,
                SortOrder = 0,
                DateCreated = DateTime.UtcNow,
                CreatedByAppUserId = userId
            });
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var file = await db.UploadFiles.FindAsync(fileId);
            Assert.NotNull(file);
            Assert.Equal("test.pdf", file.FileName);
            Assert.Equal("application/pdf", file.ContentType);
            Assert.Equal(5, file.FileSize);
            Assert.Equal(fileData, file.FileData);
        }
    }

    // ── UploadFileOrganizationShare ───────────────────────────────────────────

    [Fact]
    public async Task UploadFileOrganizationShare_CanBeCreatedWithVisibility()
    {
        var factory = CreateFactory();
        var shareId = Guid.NewGuid();
        var fileId  = Guid.NewGuid();
        var orgId   = Guid.NewGuid();
        var userId  = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFileOrganizationShares.Add(new UploadFileOrganizationShare
            {
                Id = shareId,
                UploadFileId = fileId,
                OrganizationId = orgId,
                SharedByAppUserId = userId,
                Visibility = FileShareVisibility.OrgMembers,
                IsActive = true,
                DateCreated = DateTime.UtcNow,
                CreatedByAppUserId = userId
            });
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var share = await db.UploadFileOrganizationShares.FindAsync(shareId);
            Assert.NotNull(share);
            Assert.Equal(FileShareVisibility.OrgMembers, share.Visibility);
            Assert.True(share.IsActive);
        }
    }

    [Fact]
    public async Task UploadFileOrganizationShare_SoftDelete_SetsIsActiveFalse()
    {
        var factory = CreateFactory();
        var shareId = Guid.NewGuid();
        var userId  = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFileOrganizationShares.Add(new UploadFileOrganizationShare
            {
                Id = shareId,
                UploadFileId = Guid.NewGuid(),
                OrganizationId = Guid.NewGuid(),
                SharedByAppUserId = userId,
                Visibility = FileShareVisibility.OrgAdminsOnly,
                IsActive = true,
                DateCreated = DateTime.UtcNow,
                CreatedByAppUserId = userId
            });
            await db.SaveChangesAsync();
        }

        // Soft delete
        await using (var db = await factory.CreateDbContextAsync())
        {
            var share = await db.UploadFileOrganizationShares.FindAsync(shareId);
            share!.IsActive = false;
            share.RemovedByAppUserId = userId;
            share.RemovalDate = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var share = await db.UploadFileOrganizationShares.FindAsync(shareId);
            Assert.NotNull(share);
            Assert.False(share.IsActive);
            Assert.Equal(userId, share.RemovedByAppUserId);
            Assert.NotNull(share.RemovalDate);
        }
    }

    // ── UploadFilePermissionRequest ───────────────────────────────────────────

    [Fact]
    public async Task UploadFilePermissionRequest_CanBeSubmittedAndReviewed()
    {
        var factory   = CreateFactory();
        var requestId = Guid.NewGuid();
        var fileId    = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        var reviewerId = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFilePermissionRequests.Add(new UploadFilePermissionRequest
            {
                Id = requestId,
                UploadFileId = fileId,
                RequestedByAppUserId = userId,
                PermissionType = FilePermissionType.Use | FilePermissionType.Display,
                RequestStatus = FilePermissionRequestStatus.Pending,
                RequestNotes = "Please allow me to use this.",
                DateCreated = DateTime.UtcNow,
                CreatedByAppUserId = userId
            });
            await db.SaveChangesAsync();
        }

        // Approve it
        await using (var db = await factory.CreateDbContextAsync())
        {
            var req = await db.UploadFilePermissionRequests.FindAsync(requestId);
            req!.RequestStatus = FilePermissionRequestStatus.Approved;
            req.ReviewedByAppUserId = reviewerId;
            req.ReviewNotes = "Approved.";
            req.DateReviewed = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var req = await db.UploadFilePermissionRequests.FindAsync(requestId);
            Assert.NotNull(req);
            Assert.Equal(FilePermissionRequestStatus.Approved, req.RequestStatus);
            Assert.Equal(reviewerId, req.ReviewedByAppUserId);
            // Verify flags
            Assert.True(req.PermissionType.HasFlag(FilePermissionType.Use));
            Assert.True(req.PermissionType.HasFlag(FilePermissionType.Display));
            Assert.False(req.PermissionType.HasFlag(FilePermissionType.Share));
        }
    }
}
