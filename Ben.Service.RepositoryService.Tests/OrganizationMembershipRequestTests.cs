using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ben.Service.RepositoryService.Tests;

/// <summary>
/// Tests for OrganizationMembershipRequest and OrganizationFile entities via the DbContext.
/// Verifies EF model config, status transitions, and file copy tracking.
/// </summary>
public class OrganizationMembershipRequestTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IDbContextFactory<BenDataContext> CreateFactory() => TestDbFactory.Create();

    private static async Task<(AppUser user, Organization org)>
        SeedBasicAsync(BenDataContext db)
    {
        var user = new AppUser
        {
            Id          = Guid.NewGuid(),
            UserName    = "test@example.com",
            Email       = "test@example.com",
            DisplayName = "Test User",
            DateCreated = DateTime.UtcNow,
        };
        db.AppUsers.Add(user);

        var org = new Organization
        {
            Id                       = Guid.NewGuid(),
            Name                     = "Test Org",
            UrlName                  = "test-org",
            IsAcceptingApplications  = true,
            DateCreated              = DateTime.UtcNow,
            CreatedByAppUserId       = user.Id,
        };
        db.Organizations.Add(org);
        await db.SaveChangesAsync();
        return (user, org);
    }

    // ── Organization.IsAcceptingApplications ──────────────────────────────────

    [Fact]
    public async Task Organization_IsAcceptingApplications_DefaultsFalse()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var user = new AppUser { Id = Guid.NewGuid(), UserName = "a@b.com", Email = "a@b.com", DisplayName = "A", DateCreated = DateTime.UtcNow };
        db.AppUsers.Add(user);
        var org = new Organization { Id = Guid.NewGuid(), Name = "Org", UrlName = "org", DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id };
        db.Organizations.Add(org);
        await db.SaveChangesAsync();

        var loaded = await db.Organizations.AsNoTracking().FirstAsync(o => o.Id == org.Id);
        Assert.False(loaded.IsAcceptingApplications);
    }

    [Fact]
    public async Task Organization_CanToggleIsAcceptingApplications()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, org) = await SeedBasicAsync(db);

        Assert.True(org.IsAcceptingApplications);

        org.IsAcceptingApplications = false;
        await db.SaveChangesAsync();

        var loaded = await db.Organizations.AsNoTracking().FirstAsync(o => o.Id == org.Id);
        Assert.False(loaded.IsAcceptingApplications);
    }

    // ── OrganizationMembershipRequest — CRUD ─────────────────────────────────

    [Fact]
    public async Task MembershipRequest_CanCreatePendingRequest()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, org) = await SeedBasicAsync(db);

        var request = new OrganizationMembershipRequest
        {
            Id                 = Guid.NewGuid(),
            OrganizationId     = org.Id,
            AppUserId          = user.Id,
            RequestMessage     = "I would like to join.",
            Status             = OrganizationMembershipRequestStatus.Pending,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = user.Id,
        };
        db.OrganizationMembershipRequests.Add(request);
        await db.SaveChangesAsync();

        var loaded = await db.OrganizationMembershipRequests.AsNoTracking()
            .FirstAsync(r => r.Id == request.Id);

        Assert.Equal(OrganizationMembershipRequestStatus.Pending, loaded.Status);
        Assert.Equal("I would like to join.", loaded.RequestMessage);
        Assert.Equal(user.Id, loaded.AppUserId);
        Assert.Null(loaded.UpdatedByAppUserId);
        Assert.Null(loaded.DateUpdated);
    }

    [Fact]
    public async Task MembershipRequest_CanBeAccepted()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, org) = await SeedBasicAsync(db);

        var request = new OrganizationMembershipRequest
        {
            Id                 = Guid.NewGuid(),
            OrganizationId     = org.Id,
            AppUserId          = user.Id,
            Status             = OrganizationMembershipRequestStatus.Pending,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = user.Id,
        };
        db.OrganizationMembershipRequests.Add(request);
        await db.SaveChangesAsync();

        request.Status             = OrganizationMembershipRequestStatus.Accepted;
        request.DateUpdated        = DateTime.UtcNow;
        request.UpdatedByAppUserId = user.Id;
        await db.SaveChangesAsync();

        var loaded = await db.OrganizationMembershipRequests.AsNoTracking()
            .FirstAsync(r => r.Id == request.Id);
        Assert.Equal(OrganizationMembershipRequestStatus.Accepted, loaded.Status);
        Assert.NotNull(loaded.DateUpdated);
        Assert.Equal(user.Id, loaded.UpdatedByAppUserId);
    }

    [Fact]
    public async Task MembershipRequest_CanBeWithdrawn()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, org) = await SeedBasicAsync(db);

        var request = new OrganizationMembershipRequest
        {
            Id                 = Guid.NewGuid(),
            OrganizationId     = org.Id,
            AppUserId          = user.Id,
            Status             = OrganizationMembershipRequestStatus.Pending,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = user.Id,
        };
        db.OrganizationMembershipRequests.Add(request);
        await db.SaveChangesAsync();

        request.Status             = OrganizationMembershipRequestStatus.Withdrawn;
        request.DateUpdated        = DateTime.UtcNow;
        request.UpdatedByAppUserId = user.Id;
        await db.SaveChangesAsync();

        var loaded = await db.OrganizationMembershipRequests.AsNoTracking()
            .FirstAsync(r => r.Id == request.Id);
        Assert.Equal(OrganizationMembershipRequestStatus.Withdrawn, loaded.Status);
    }

    [Fact]
    public async Task MembershipRequest_CascadeDeletesWithOrg()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, org) = await SeedBasicAsync(db);

        db.OrganizationMembershipRequests.Add(new OrganizationMembershipRequest
        {
            Id = Guid.NewGuid(), OrganizationId = org.Id, AppUserId = user.Id,
            Status = OrganizationMembershipRequestStatus.Pending,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        });
        await db.SaveChangesAsync();

        db.Organizations.Remove(org);
        await db.SaveChangesAsync();

        var count = await db.OrganizationMembershipRequests.CountAsync(r => r.OrganizationId == org.Id);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task MembershipRequest_RequestMessage_IsOptional()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, org) = await SeedBasicAsync(db);

        var request = new OrganizationMembershipRequest
        {
            Id                 = Guid.NewGuid(),
            OrganizationId     = org.Id,
            AppUserId          = user.Id,
            RequestMessage     = null,
            Status             = OrganizationMembershipRequestStatus.Pending,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = user.Id,
        };
        db.OrganizationMembershipRequests.Add(request);
        await db.SaveChangesAsync(); // should not throw

        var loaded = await db.OrganizationMembershipRequests.AsNoTracking()
            .FirstAsync(r => r.Id == request.Id);
        Assert.Null(loaded.RequestMessage);
    }

    // ── OrganizationFile ──────────────────────────────────────────────────────

    [Fact]
    public async Task OrganizationFile_CanBeCreated()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, org) = await SeedBasicAsync(db);

        var fileType = new UploadFileType
        {
            Id = Guid.NewGuid(), Name = "Documents", IsActive = true, IsPublic = true,
            SortOrder = 1, DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        };
        db.UploadFileTypes.Add(fileType);
        await db.SaveChangesAsync();

        var orgFile = new OrganizationFile
        {
            Id                 = Guid.NewGuid(),
            OrganizationId     = org.Id,
            UploadFileTypeId   = fileType.Id,
            FileName           = "policy.pdf",
            StoredFileName     = "abc123.pdf",
            ContentType        = "application/pdf",
            FileSize           = 10240,
            StoragePath        = "orgs/" + org.Id + "/abc123.pdf",
            Description        = "Company policy document",
            IsPublic           = true,
            SortOrder          = 1,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = user.Id,
        };
        db.OrganizationFiles.Add(orgFile);
        await db.SaveChangesAsync();

        var loaded = await db.OrganizationFiles.AsNoTracking()
            .FirstAsync(f => f.Id == orgFile.Id);
        Assert.Equal("policy.pdf", loaded.FileName);
        Assert.Equal(org.Id, loaded.OrganizationId);
        Assert.Null(loaded.SourceUploadFileId);
    }

    [Fact]
    public async Task OrganizationFile_CanTrackSourceUploadFile()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, org) = await SeedBasicAsync(db);

        var fileType = new UploadFileType
        {
            Id = Guid.NewGuid(), Name = "Images", IsActive = true, IsPublic = true,
            SortOrder = 1, DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        };
        db.UploadFileTypes.Add(fileType);

        var userFile = new UploadFile
        {
            Id = Guid.NewGuid(), AppUserId = user.Id, UploadFileTypeId = fileType.Id,
            FileName = "photo.jpg", StoredFileName = "xyz.jpg",
            ContentType = "image/jpeg", FileSize = 50000,
            StoragePath = "users/" + user.Id + "/xyz.jpg",
            IsPublic = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        };
        db.UploadFiles.Add(userFile);
        await db.SaveChangesAsync();

        var orgFile = new OrganizationFile
        {
            Id = Guid.NewGuid(), OrganizationId = org.Id, UploadFileTypeId = fileType.Id,
            FileName = "photo.jpg", StoredFileName = "orgcopy.jpg",
            ContentType = "image/jpeg", FileSize = 50000,
            StoragePath = "orgs/" + org.Id + "/orgcopy.jpg",
            IsPublic = false, SortOrder = 0,
            SourceUploadFileId = userFile.Id,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        };
        db.OrganizationFiles.Add(orgFile);
        await db.SaveChangesAsync();

        var loaded = await db.OrganizationFiles
            .Include(f => f.SourceUploadFile)
            .AsNoTracking()
            .FirstAsync(f => f.Id == orgFile.Id);

        Assert.NotNull(loaded.SourceUploadFile);
        Assert.Equal(userFile.Id, loaded.SourceUploadFile!.Id);
        Assert.Equal("photo.jpg", loaded.FileName);
    }

    [Fact]
    public async Task OrganizationFile_CascadeDeletesWithOrg()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var (user, org) = await SeedBasicAsync(db);

        var fileType = new UploadFileType
        {
            Id = Guid.NewGuid(), Name = "Docs", IsActive = true, IsPublic = true,
            SortOrder = 1, DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        };
        db.UploadFileTypes.Add(fileType);
        db.OrganizationFiles.Add(new OrganizationFile
        {
            Id = Guid.NewGuid(), OrganizationId = org.Id, UploadFileTypeId = fileType.Id,
            FileName = "test.pdf", StoredFileName = "t.pdf", ContentType = "application/pdf",
            FileSize = 1024, IsPublic = false, SortOrder = 0,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = user.Id,
        });
        await db.SaveChangesAsync();

        db.Organizations.Remove(org);
        await db.SaveChangesAsync();

        var count = await db.OrganizationFiles.CountAsync(f => f.OrganizationId == org.Id);
        Assert.Equal(0, count);
    }

    // ── OrganizationMembershipRequestStatus enum ─────────────────────────────

    [Theory]
    [InlineData(OrganizationMembershipRequestStatus.Pending,   0)]
    [InlineData(OrganizationMembershipRequestStatus.Accepted,  1)]
    [InlineData(OrganizationMembershipRequestStatus.Denied,    2)]
    [InlineData(OrganizationMembershipRequestStatus.Withdrawn, 3)]
    public void OrganizationMembershipRequestStatus_HasExpectedIntegerValues(
        OrganizationMembershipRequestStatus status, int expected)
    {
        Assert.Equal(expected, (int)status);
    }
}
