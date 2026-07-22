using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ben.Service.RepositoryService.Tests;

/// <summary>
/// Tests for OrganizationFile publish tracking and OrganizationFileDeleteLog.
/// </summary>
public class OrganizationFilePublishAndDeleteLogTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<(BenDataContext db, AppUser user, Organization org, UploadFileType ft)>
        SeedAsync()
    {
        var db   = TestDbFactory.Create().CreateDbContext();
        var user = new AppUser { Id = Guid.NewGuid(), UserName = "u@u.com", Email = "u@u.com", DisplayName = "User One", DateCreated = DateTime.UtcNow };
        db.AppUsers.Add(user);
        var org = new Organization { Id = Guid.NewGuid(), Name = "Test Org", UrlName = "test-org", DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id };
        db.Organizations.Add(org);
        var ft = new UploadFileType { Id = Guid.NewGuid(), Name = "Docs", IsActive = true, IsPublic = true, SortOrder = 1, DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id };
        db.UploadFileTypes.Add(ft);
        await db.SaveChangesAsync();
        return (db, user, org, ft);
    }

    private static OrganizationFile MakeFile(Guid orgId, Guid ftId, Guid userId, bool isPublic = false) =>
        new()
        {
            Id                 = Guid.NewGuid(),
            OrganizationId     = orgId,
            UploadFileTypeId   = ftId,
            FileName           = "test.pdf",
            StoredFileName     = "t.pdf",
            ContentType        = "application/pdf",
            FileSize           = 2048,
            StoragePath        = $"orgs/{orgId}/t.pdf",
            IsPublic           = isPublic,
            SortOrder          = 0,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = userId,
        };

    // ── Publish tracking ──────────────────────────────────────────────────────

    [Fact]
    public async Task OrganizationFile_DefaultsToNotPublic()
    {
        var (db, user, org, ft) = await SeedAsync();
        await using var _ = db;
        var file = MakeFile(org.Id, ft.Id, user.Id);
        db.OrganizationFiles.Add(file);
        await db.SaveChangesAsync();

        var loaded = await db.OrganizationFiles.AsNoTracking().FirstAsync(f => f.Id == file.Id);
        Assert.False(loaded.IsPublic);
        Assert.Null(loaded.PublishedByAppUserId);
        Assert.Null(loaded.DatePublished);
    }

    [Fact]
    public async Task OrganizationFile_CanBePublishedWithAudit()
    {
        var (db, user, org, ft) = await SeedAsync(); await using var _d = db;
        var file = MakeFile(org.Id, ft.Id, user.Id);
        db.OrganizationFiles.Add(file);
        await db.SaveChangesAsync();

        var now = DateTime.UtcNow;
        file.IsPublic             = true;
        file.PublishedByAppUserId = user.Id;
        file.DatePublished        = now;
        file.DateUpdated          = now;
        file.UpdatedByAppUserId   = user.Id;
        await db.SaveChangesAsync();

        var loaded = await db.OrganizationFiles.AsNoTracking().FirstAsync(f => f.Id == file.Id);
        Assert.True(loaded.IsPublic);
        Assert.Equal(user.Id, loaded.PublishedByAppUserId);
        Assert.NotNull(loaded.DatePublished);
    }

    [Fact]
    public async Task OrganizationFile_UnpublishingClearsAuditFields()
    {
        var (db, user, org, ft) = await SeedAsync(); await using var _d = db;
        var file = MakeFile(org.Id, ft.Id, user.Id, isPublic: true);
        file.PublishedByAppUserId = user.Id;
        file.DatePublished        = DateTime.UtcNow;
        db.OrganizationFiles.Add(file);
        await db.SaveChangesAsync();

        file.IsPublic             = false;
        file.PublishedByAppUserId = null;
        file.DatePublished        = null;
        await db.SaveChangesAsync();

        var loaded = await db.OrganizationFiles.AsNoTracking().FirstAsync(f => f.Id == file.Id);
        Assert.False(loaded.IsPublic);
        Assert.Null(loaded.PublishedByAppUserId);
        Assert.Null(loaded.DatePublished);
    }

    [Fact]
    public async Task OrganizationFile_PublishNavPropLoads()
    {
        var (db, user, org, ft) = await SeedAsync(); await using var _d = db;
        var file = MakeFile(org.Id, ft.Id, user.Id, isPublic: true);
        file.PublishedByAppUserId = user.Id;
        file.DatePublished        = DateTime.UtcNow;
        db.OrganizationFiles.Add(file);
        await db.SaveChangesAsync();

        var loaded = await db.OrganizationFiles
            .Include(f => f.PublishedByAppUser)
            .AsNoTracking().FirstAsync(f => f.Id == file.Id);

        Assert.NotNull(loaded.PublishedByAppUser);
        Assert.Equal("User One", loaded.PublishedByAppUser!.DisplayName);
    }

    [Fact]
    public async Task OrganizationFile_SourceUploadFileIdTrackedForSharedCopy()
    {
        var (db, user, org, ft) = await SeedAsync(); await using var _d = db;
        var userFile = new UploadFile
        {
            Id = Guid.NewGuid(), AppUserId = user.Id, UploadFileTypeId = ft.Id,
            FileName = "original.pdf", StoredFileName = "o.pdf", ContentType = "application/pdf",
            FileSize = 1024, StoragePath = $"users/{user.Id}/o.pdf", IsPublic = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        };
        db.UploadFiles.Add(userFile);
        await db.SaveChangesAsync();

        var copy = MakeFile(org.Id, ft.Id, user.Id);
        copy.SourceUploadFileId = userFile.Id;
        db.OrganizationFiles.Add(copy);
        await db.SaveChangesAsync();

        var loaded = await db.OrganizationFiles.AsNoTracking().FirstAsync(f => f.Id == copy.Id);
        Assert.Equal(userFile.Id, loaded.SourceUploadFileId);
        Assert.False(loaded.IsPublic);   // defaults non-public even though source was public
    }

    // ── OrganizationFileDeleteLog ─────────────────────────────────────────────

    [Fact]
    public async Task DeleteLog_CanBeCreatedWithFullSnapshot()
    {
        var (db, user, org, ft) = await SeedAsync(); await using var _d = db;
        var file = MakeFile(org.Id, ft.Id, user.Id, isPublic: true);
        file.PublishedByAppUserId = user.Id;
        file.DatePublished        = DateTime.UtcNow;
        db.OrganizationFiles.Add(file);
        await db.SaveChangesAsync();

        // Simulate deletion: write log, then delete
        var deleteTime = DateTime.UtcNow;
        db.OrganizationFileDeleteLogs.Add(new OrganizationFileDeleteLog
        {
            Id                        = Guid.NewGuid(),
            OrganizationId            = org.Id,
            OrganizationName          = org.Name,
            OriginalFileId            = file.Id,
            FileName                  = file.FileName,
            ContentType               = file.ContentType,
            FileSize                  = file.FileSize,
            StoragePath               = file.StoragePath,
            SourceUploadFileId        = file.SourceUploadFileId,
            WasPublic                 = file.IsPublic,
            WasPublishedByAppUserId   = file.PublishedByAppUserId,
            WasPublishedByDisplayName = user.DisplayName,
            WasDatePublished          = file.DatePublished,
            DeletedByAppUserId        = user.Id,
            DeletedByDisplayName      = user.DisplayName,
            DateDeleted               = deleteTime,
        });
        db.OrganizationFiles.Remove(file);
        await db.SaveChangesAsync();

        // File should be gone
        Assert.False(await db.OrganizationFiles.AnyAsync(f => f.Id == file.Id));

        // Log should persist
        var log = await db.OrganizationFileDeleteLogs.AsNoTracking()
            .FirstAsync(l => l.OriginalFileId == file.Id);

        Assert.Equal(org.Id,       log.OrganizationId);
        Assert.Equal("Test Org",   log.OrganizationName);
        Assert.Equal("test.pdf",   log.FileName);
        Assert.Equal(2048,         log.FileSize);
        Assert.True(log.WasPublic);
        Assert.Equal(user.Id,      log.WasPublishedByAppUserId);
        Assert.Equal("User One",   log.WasPublishedByDisplayName);
        Assert.Equal(user.Id,      log.DeletedByAppUserId);
        Assert.Equal("User One",   log.DeletedByDisplayName);
        Assert.Equal(deleteTime,   log.DateDeleted);
    }

    [Fact]
    public async Task DeleteLog_PreservesLogWhenFileIsNotPublic()
    {
        var (db, user, org, ft) = await SeedAsync(); await using var _d = db;
        var file = MakeFile(org.Id, ft.Id, user.Id, isPublic: false);
        db.OrganizationFiles.Add(file);
        await db.SaveChangesAsync();

        db.OrganizationFileDeleteLogs.Add(new OrganizationFileDeleteLog
        {
            Id                   = Guid.NewGuid(),
            OrganizationId       = org.Id,
            OrganizationName     = org.Name,
            OriginalFileId       = file.Id,
            FileName             = file.FileName,
            ContentType          = file.ContentType,
            FileSize             = file.FileSize,
            WasPublic            = false,
            DeletedByAppUserId   = user.Id,
            DeletedByDisplayName = user.DisplayName,
            DateDeleted          = DateTime.UtcNow,
        });
        db.OrganizationFiles.Remove(file);
        await db.SaveChangesAsync();

        var log = await db.OrganizationFileDeleteLogs.AsNoTracking()
            .FirstAsync(l => l.OriginalFileId == file.Id);

        Assert.False(log.WasPublic);
        Assert.Null(log.WasPublishedByAppUserId);
        Assert.Null(log.WasPublishedByDisplayName);
        Assert.Null(log.WasDatePublished);
    }

    [Fact]
    public async Task DeleteLog_CanQueryByOrganizationId()
    {
        var (db, user, org, ft) = await SeedAsync(); await using var _d = db;

        for (int i = 0; i < 3; i++)
        {
            db.OrganizationFileDeleteLogs.Add(new OrganizationFileDeleteLog
            {
                Id = Guid.NewGuid(), OrganizationId = org.Id, OrganizationName = org.Name,
                OriginalFileId = Guid.NewGuid(), FileName = $"file{i}.pdf",
                ContentType = "application/pdf", FileSize = 512,
                DeletedByAppUserId = user.Id, DeletedByDisplayName = user.DisplayName,
                DateDeleted = DateTime.UtcNow,
            });
        }
        await db.SaveChangesAsync();

        var count = await db.OrganizationFileDeleteLogs
            .CountAsync(l => l.OrganizationId == org.Id);
        Assert.Equal(3, count);
    }

    [Fact]
    public async Task DeleteLog_NoForeignKeyToOrgFile_SurvivesDeletion()
    {
        // The log is intentionally schema-independent so it doesn't cascade.
        var (db, user, org, ft) = await SeedAsync(); await using var _d = db;
        var file = MakeFile(org.Id, ft.Id, user.Id);
        db.OrganizationFiles.Add(file);
        await db.SaveChangesAsync();

        db.OrganizationFileDeleteLogs.Add(new OrganizationFileDeleteLog
        {
            Id = Guid.NewGuid(), OrganizationId = org.Id, OrganizationName = org.Name,
            OriginalFileId = file.Id, FileName = file.FileName,
            ContentType = file.ContentType, FileSize = file.FileSize,
            DeletedByAppUserId = user.Id, DeletedByDisplayName = user.DisplayName,
            DateDeleted = DateTime.UtcNow,
        });
        db.OrganizationFiles.Remove(file);
        await db.SaveChangesAsync();

        // Log still exists even though the file is gone
        Assert.True(await db.OrganizationFileDeleteLogs.AnyAsync(l => l.OriginalFileId == file.Id));
        Assert.False(await db.OrganizationFiles.AnyAsync(f => f.Id == file.Id));
    }
}
