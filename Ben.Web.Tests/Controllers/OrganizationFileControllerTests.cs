using AutoMapper;
using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Tests for OrganizationFileController — file list, publish toggle, metadata update,
/// delete-with-audit-log, and access-control checks.
/// </summary>
public class OrganizationFileControllerTests
{
    // Non-pooled: Publish/Update/Delete use FirstAsync with required Include(UploadFileType, CreatedByAppUser)
    private sealed class SimpleFactory(DbContextOptions<BenDataContext> options) : IDbContextFactory<BenDataContext>
    {
        public BenDataContext CreateDbContext() => new(options);
        public Task<BenDataContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(new BenDataContext(options));
    }

    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var opts = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SimpleFactory(opts);
    }

    private static IMapper CreateMapper()
    {
        var m = new Mock<IMapper>();
        m.Setup(x => x.Map<OrganizationFileRecord>(It.IsAny<object>()))
            .Returns<object>(o => o is OrganizationFile f
                ? new OrganizationFileRecord { Id = f.Id, OrganizationId = f.OrganizationId, UploadFileTypeId = f.UploadFileTypeId, FileTypeName = "", FileName = f.FileName, ContentType = f.ContentType, FileSize = f.FileSize, Description = f.Description, IsPublic = f.IsPublic, SortOrder = f.SortOrder, PublishedByDisplayName = null, DatePublished = f.DatePublished, CreatedByDisplayName = "", DateCreated = f.DateCreated }
                : new OrganizationFileRecord { FileTypeName = "", FileName = "", ContentType = "", CreatedByDisplayName = "", DateCreated = DateTime.UtcNow });
        m.Setup(x => x.Map<List<OrganizationFileRecord>>(It.IsAny<object>()))
            .Returns<object>(o => o is IEnumerable<OrganizationFile> list
                ? list.Select(f => new OrganizationFileRecord { Id = f.Id, OrganizationId = f.OrganizationId, UploadFileTypeId = f.UploadFileTypeId, FileTypeName = "", FileName = f.FileName, ContentType = f.ContentType, FileSize = f.FileSize, IsPublic = f.IsPublic, SortOrder = f.SortOrder, CreatedByDisplayName = "", DateCreated = f.DateCreated }).ToList()
                : []);
        m.Setup(x => x.Map<List<OrganizationFileDeleteLogRecord>>(It.IsAny<object>()))
            .Returns<object>(_ => []);
        return m.Object;
    }

    private static (Mock<IOrganizationSecurityService>, Mock<IFileStorageService>, Mock<IAuditLogService>) CreateMocks(bool hasPermission = true)
    {
        var security = new Mock<IOrganizationSecurityService>();
        security.Setup(s => s.HasAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid>(),
            It.IsAny<OrganizationSecurityTable>(), It.IsAny<OrganizationSecurityAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(hasPermission);

        var storage = new Mock<IFileStorageService>();
        storage.Setup(s => s.OrgFilePath(It.IsAny<Guid>(), It.IsAny<string>())).Returns("org/file.bin");
        storage.Setup(s => s.Exists(It.IsAny<string>())).Returns(true);
        storage.Setup(s => s.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(() => new MemoryStream([0x00, 0x01, 0x02]));
        storage.Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
               .Returns(Task.CompletedTask);
        storage.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .Returns(Task.CompletedTask);

        var audit = new Mock<IAuditLogService>();
        audit.Setup(a => a.LogCreateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<object>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        audit.Setup(a => a.LogUpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<object>(), It.IsAny<object>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        audit.Setup(a => a.LogDeleteAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<object>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        return (security, storage, audit);
    }

    private static OrganizationFileController Build(
        IDbContextFactory<BenDataContext> factory, Guid userId,
        Mock<IOrganizationSecurityService>? security = null,
        Mock<IFileStorageService>? storage = null,
        Mock<IAuditLogService>? audit = null)
    {
        var (defSec, defStore, defAudit) = CreateMocks();
        var ctrl = new OrganizationFileController(factory, CreateMapper(),
            (security ?? defSec).Object,
            (storage ?? defStore).Object,
            (audit ?? defAudit).Object);
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Bearer"))
            }
        };
        return ctrl;
    }

    private static async Task<(IDbContextFactory<BenDataContext>, Guid orgId, Guid userId, Guid fileTypeId)> SeedAsync()
    {
        var factory    = CreateFactory();
        var orgId      = Guid.NewGuid();
        var userId     = Guid.NewGuid();
        var fileTypeId = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();
        db.Users.Add(new AppUser { Id = userId, UserName = "u@t.com", NormalizedUserName = "U@T.COM", Email = "u@t.com", NormalizedEmail = "U@T.COM", DateCreated = DateTime.UtcNow });
        db.Organizations.Add(new Organization { Id = orgId, Name = "Test Org", UrlName = "test", DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId });
        db.UploadFileTypes.Add(new UploadFileType { Id = fileTypeId, Name = "Docs", AllowAllExtensions = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId });
        await db.SaveChangesAsync();
        return (factory, orgId, userId, fileTypeId);
    }

    /// <summary>Seeds an OrganizationFile row directly (bypasses Upload endpoint).</summary>
    private static async Task<Guid> SeedFileAsync(IDbContextFactory<BenDataContext> factory, Guid orgId, Guid userId, Guid fileTypeId, bool isPublic = false)
    {
        var fileId = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        db.OrganizationFiles.Add(new OrganizationFile
        {
            Id                   = fileId,
            OrganizationId       = orgId,
            UploadFileTypeId     = fileTypeId,
            FileName             = "test.pdf",
            StoredFileName       = "abc.pdf",
            ContentType          = "application/pdf",
            FileSize             = 1024,
            StoragePath          = "org/abc.pdf",
            IsPublic             = isPublic,
            PublishedByAppUserId = isPublic ? userId : null,
            DatePublished        = isPublic ? DateTime.UtcNow : null,
            SortOrder            = 1,
            DateCreated          = DateTime.UtcNow,
            CreatedByAppUserId   = userId,
        });
        await db.SaveChangesAsync();
        return fileId;
    }

    // ── GetAll ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_Unauthenticated_ReturnsUnauthorized()
    {
        var factory = CreateFactory();
        var ctrl = new OrganizationFileController(factory, CreateMapper(),
            new Mock<IOrganizationSecurityService>().Object,
            new Mock<IFileStorageService>().Object,
            new Mock<IAuditLogService>().Object);
        ctrl.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        Assert.IsType<UnauthorizedResult>((await ctrl.GetAll(Guid.NewGuid(), default)).Result);
    }

    [Fact]
    public async Task GetAll_NoPermission_ReturnsForbid()
    {
        var (factory, orgId, userId, _) = await SeedAsync();
        var (noPermSecurity, _, _) = CreateMocks(hasPermission: false);
        var ctrl = Build(factory, userId, security: noPermSecurity);
        Assert.IsType<ForbidResult>((await ctrl.GetAll(orgId, default)).Result);
    }

    [Fact]
    public async Task GetAll_WithPermission_ReturnsEmptyList()
    {
        var (factory, orgId, userId, _) = await SeedAsync();
        var ctrl = Build(factory, userId);
        var ok   = Assert.IsType<OkObjectResult>((await ctrl.GetAll(orgId, default)).Result);
        Assert.Empty((List<OrganizationFileRecord>)ok.Value!);
    }

    [Fact]
    public async Task GetAll_WithPermission_ReturnsSeededFile()
    {
        var (factory, orgId, userId, fileTypeId) = await SeedAsync();
        await SeedFileAsync(factory, orgId, userId, fileTypeId);
        var ctrl = Build(factory, userId);
        var ok   = Assert.IsType<OkObjectResult>((await ctrl.GetAll(orgId, default)).Result);
        Assert.Single((List<OrganizationFileRecord>)ok.Value!);
    }

    // ── Download ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Download_FileExists_ReturnsFileResult()
    {
        var (factory, orgId, userId, fileTypeId) = await SeedAsync();
        var fileId = await SeedFileAsync(factory, orgId, userId, fileTypeId);
        var ctrl   = Build(factory, userId);
        var result = await ctrl.Download(orgId, fileId, default);
        Assert.IsType<FileStreamResult>(result);
    }

    [Fact]
    public async Task Download_MissingFile_ReturnsNotFound()
    {
        var (factory, orgId, userId, _) = await SeedAsync();
        var ctrl = Build(factory, userId);
        Assert.IsType<NotFoundResult>(await ctrl.Download(orgId, Guid.NewGuid(), default));
    }

    // ── GetDeleteLog ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetDeleteLog_NoPermission_ReturnsForbid()
    {
        var (factory, orgId, userId, _) = await SeedAsync();
        var (noPermSecurity, _, _) = CreateMocks(hasPermission: false);
        var ctrl = Build(factory, userId, security: noPermSecurity);
        Assert.IsType<ForbidResult>((await ctrl.GetDeleteLog(orgId, default)).Result);
    }

    [Fact]
    public async Task GetDeleteLog_WithPermission_ReturnsEmptyList()
    {
        var (factory, orgId, userId, _) = await SeedAsync();
        var ctrl = Build(factory, userId);
        var ok   = Assert.IsType<OkObjectResult>((await ctrl.GetDeleteLog(orgId, default)).Result);
        Assert.Empty((List<OrganizationFileDeleteLogRecord>)ok.Value!);
    }

    // ── Publish ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Publish_SetPublic_SetsPublishFieldsAndTimestamp()
    {
        var (factory, orgId, userId, fileTypeId) = await SeedAsync();
        var fileId = await SeedFileAsync(factory, orgId, userId, fileTypeId, isPublic: false);
        var ctrl   = Build(factory, userId);

        var result = await ctrl.Publish(orgId, fileId, new PublishOrgFileRequest(true), default);

        Assert.IsType<OkObjectResult>(result.Result);
        await using var db = await factory.CreateDbContextAsync();
        var f = await db.OrganizationFiles.FindAsync(fileId);
        Assert.True(f!.IsPublic);
        Assert.Equal(userId, f.PublishedByAppUserId);
        Assert.NotNull(f.DatePublished);
    }

    [Fact]
    public async Task Publish_RevokePublic_ClearsPublishFields()
    {
        var (factory, orgId, userId, fileTypeId) = await SeedAsync();
        var fileId = await SeedFileAsync(factory, orgId, userId, fileTypeId, isPublic: true);
        var ctrl   = Build(factory, userId);

        await ctrl.Publish(orgId, fileId, new PublishOrgFileRequest(false), default);

        await using var db = await factory.CreateDbContextAsync();
        var f = await db.OrganizationFiles.FindAsync(fileId);
        Assert.False(f!.IsPublic);
        Assert.Null(f.PublishedByAppUserId);
        Assert.Null(f.DatePublished);
    }

    [Fact]
    public async Task Publish_MissingFile_ReturnsNotFound()
    {
        var (factory, orgId, userId, _) = await SeedAsync();
        Assert.IsType<NotFoundResult>((await Build(factory, userId).Publish(orgId, Guid.NewGuid(), new PublishOrgFileRequest(true), default)).Result);
    }

    // ── Update ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_MetadataOnly_UpdatesDescriptionAndSortOrder()
    {
        var (factory, orgId, userId, fileTypeId) = await SeedAsync();
        var fileId = await SeedFileAsync(factory, orgId, userId, fileTypeId);
        var ctrl   = Build(factory, userId);

        var result = await ctrl.Update(orgId, fileId, new OrgFileUpdateRequest("New description", 5), default);

        Assert.IsType<OkObjectResult>(result.Result);
        await using var db = await factory.CreateDbContextAsync();
        var f = await db.OrganizationFiles.FindAsync(fileId);
        Assert.Equal("New description", f!.Description);
        Assert.Equal(5, f.SortOrder);
    }

    [Fact]
    public async Task Update_MissingFile_ReturnsNotFound()
    {
        var (factory, orgId, userId, _) = await SeedAsync();
        Assert.IsType<NotFoundResult>((await Build(factory, userId).Update(orgId, Guid.NewGuid(), new OrgFileUpdateRequest(null, 0), default)).Result);
    }

    [Fact]
    public async Task Update_NoPermission_ReturnsForbid()
    {
        var (factory, orgId, userId, fileTypeId) = await SeedAsync();
        var fileId = await SeedFileAsync(factory, orgId, userId, fileTypeId);
        var (noPermSecurity, _, _) = CreateMocks(hasPermission: false);
        Assert.IsType<ForbidResult>((await Build(factory, userId, security: noPermSecurity).Update(orgId, fileId, new OrgFileUpdateRequest(null, 0), default)).Result);
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_WritesAuditLogAndRemovesFile()
    {
        var (factory, orgId, userId, fileTypeId) = await SeedAsync();
        var fileId     = await SeedFileAsync(factory, orgId, userId, fileTypeId);
        var (_, storageMock, _) = CreateMocks();
        var ctrl = Build(factory, userId, storage: storageMock);

        var result = await ctrl.Delete(orgId, fileId, default);

        Assert.IsType<NoContentResult>(result);
        await using var db = await factory.CreateDbContextAsync();
        Assert.False(await db.OrganizationFiles.AnyAsync(f => f.Id == fileId));
        Assert.True(await db.OrganizationFileDeleteLogs.AnyAsync(l => l.OriginalFileId == fileId));
        storageMock.Verify(s => s.DeleteAsync("org/abc.pdf", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Delete_PublishedFile_LogPreservesPublishMetadata()
    {
        var (factory, orgId, userId, fileTypeId) = await SeedAsync();
        var fileId = await SeedFileAsync(factory, orgId, userId, fileTypeId, isPublic: true);
        var ctrl   = Build(factory, userId);

        await ctrl.Delete(orgId, fileId, default);

        await using var db = await factory.CreateDbContextAsync();
        var log = await db.OrganizationFileDeleteLogs.FirstAsync(l => l.OriginalFileId == fileId);
        Assert.True(log.WasPublic);
        Assert.Equal(userId, log.WasPublishedByAppUserId);
    }

    [Fact]
    public async Task Delete_MissingFile_ReturnsNotFound()
    {
        var (factory, orgId, userId, _) = await SeedAsync();
        Assert.IsType<NotFoundResult>(await Build(factory, userId).Delete(orgId, Guid.NewGuid(), default));
    }

    [Fact]
    public async Task Delete_NoPermission_ReturnsForbid()
    {
        var (factory, orgId, userId, fileTypeId) = await SeedAsync();
        var fileId = await SeedFileAsync(factory, orgId, userId, fileTypeId);
        var (noPermSecurity, _, _) = CreateMocks(hasPermission: false);
        Assert.IsType<ForbidResult>(await Build(factory, userId, security: noPermSecurity).Delete(orgId, fileId, default));
    }
}
