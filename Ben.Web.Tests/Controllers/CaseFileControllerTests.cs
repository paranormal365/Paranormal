using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
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
/// Tests for CaseFileController — the case's general Files/Evidence tab.
/// </summary>
public class CaseFileControllerTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var options = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PooledDbContextFactory<BenDataContext>(options);
    }

    private static CaseFileController BuildController(
        IDbContextFactory<BenDataContext> factory, Guid userId, Mock<IFileStorageService>? storageMock = null)
    {
        var storage = storageMock ?? new Mock<IFileStorageService>();
        storage.Setup(s => s.CaseFilePath(It.IsAny<Guid>(), It.IsAny<string>())).Returns("fake/path");
        storage.Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
               .Returns(Task.CompletedTask);

        var ctrl = new CaseFileController(factory, storage.Object, new Mock<IAuditLogService>().Object);
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

    private static IFormFile MakeFile(string fileName = "evidence.jpg", string contentType = "image/jpeg", long size = 256)
    {
        var fileMock = new Mock<IFormFile>();
        var bytes    = new byte[size];
        fileMock.Setup(f => f.FileName).Returns(fileName);
        fileMock.Setup(f => f.Length).Returns(size);
        fileMock.Setup(f => f.ContentType).Returns(contentType);
        fileMock.Setup(f => f.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                .Returns<Stream, CancellationToken>((s, _) =>
                {
                    s.Write(bytes, 0, bytes.Length);
                    return Task.CompletedTask;
                });
        return fileMock.Object;
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
        return (factory, orgId, caseId, userId);
    }

    // ── GetAll ────────────────────────────────────────────────────────────────

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
        var list = Assert.IsAssignableFrom<IEnumerable<CaseFileRecord>>(ok.Value);
        Assert.Empty(list);
    }

    // ── Upload ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Upload_ReturnsOkWithRecord()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var ctrl = BuildController(factory, userId);

        var result = await ctrl.Upload(orgId, caseId, "Front porch photo", MakeFile(), default);

        var ok  = Assert.IsType<OkObjectResult>(result.Result);
        var rec = Assert.IsType<CaseFileRecord>(ok.Value);
        Assert.Equal("evidence.jpg", rec.FileName);
        Assert.Equal("image/jpeg", rec.ContentType);
        Assert.Equal("Front porch photo", rec.Description);
        Assert.Equal(caseId, rec.CaseId);
    }

    [Fact]
    public async Task Upload_CreatesUnderlyingUploadFile()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var ctrl = BuildController(factory, userId);

        var result = await ctrl.Upload(orgId, caseId, null, MakeFile(), default);
        var rec = (CaseFileRecord)((OkObjectResult)result.Result!).Value!;

        await using var db = await factory.CreateDbContextAsync();
        Assert.True(await db.UploadFiles.AnyAsync(f => f.Id == rec.UploadFileId));
    }

    [Fact]
    public async Task Upload_EmptyFile_ReturnsBadRequest()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var ctrl = BuildController(factory, userId);

        var result = await ctrl.Upload(orgId, caseId, null, MakeFile(size: 0), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Upload_NonMember_ReturnsForbid()
    {
        var (factory, orgId, caseId, _) = await SeedAsync();
        var ctrl = BuildController(factory, Guid.NewGuid());

        var result = await ctrl.Upload(orgId, caseId, null, MakeFile(), default);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task Upload_CaseNotFound_ReturnsNotFound()
    {
        var (factory, orgId, _, userId) = await SeedAsync();
        var ctrl = BuildController(factory, userId);

        var result = await ctrl.Upload(orgId, Guid.NewGuid(), null, MakeFile(), default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_ExistingFile_ReturnsNoContent_ButKeepsUploadFile()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var ctrl = BuildController(factory, userId);
        var uploaded = (CaseFileRecord)((OkObjectResult)(await ctrl.Upload(orgId, caseId, null, MakeFile(), default)).Result!).Value!;

        var result = await ctrl.Delete(orgId, caseId, uploaded.Id, default);

        Assert.IsType<NoContentResult>(result);
        await using var db = await factory.CreateDbContextAsync();
        Assert.False(await db.CaseFiles.AnyAsync(f => f.Id == uploaded.Id));
        Assert.True(await db.UploadFiles.AnyAsync(f => f.Id == uploaded.UploadFileId)); // not deleted — chain of custody
    }

    [Fact]
    public async Task Delete_MissingFile_ReturnsNotFound()
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
        var uploaded = (CaseFileRecord)((OkObjectResult)(await ctrl.Upload(orgId, caseId, null, MakeFile(), default)).Result!).Value!;

        var nonMemberCtrl = BuildController(factory, Guid.NewGuid());
        var result = await nonMemberCtrl.Delete(orgId, caseId, uploaded.Id, default);

        Assert.IsType<ForbidResult>(result);
    }

    // ── Link (copy-on-attach, item #6 phase 2) ──────────────────────────────────

    private static Mock<IFileStorageService> MakeReadableStorageMock(byte[] sourceBytes)
    {
        var storage = new Mock<IFileStorageService>();
        storage.Setup(s => s.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(() => new MemoryStream(sourceBytes));
        return storage;
    }

    private static async Task<UploadFile> SeedSourceFileAsync(
        IDbContextFactory<BenDataContext> factory, Guid ownerId,
        bool isPublic = false, string? storagePath = "users/owner/source.jpg", byte[]? fileData = null)
    {
        var file = new UploadFile
        {
            Id = Guid.NewGuid(), UploadFileTypeId = Guid.NewGuid(), AppUserId = ownerId,
            FileName = "source.jpg", StoredFileName = "source-stored.jpg", ContentType = "image/jpeg",
            FileSize = 4, StoragePath = storagePath, FileData = fileData, IsPublic = isPublic,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
        };
        await using var db = await factory.CreateDbContextAsync();
        db.UploadFiles.Add(file);
        await db.SaveChangesAsync();
        return file;
    }

    [Fact]
    public async Task Link_OwnFile_ReturnsOkWithNewCopyRecord()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var source = await SeedSourceFileAsync(factory, userId);
        var ctrl = BuildController(factory, userId, MakeReadableStorageMock([1, 2, 3, 4]));

        var result = await ctrl.Link(orgId, caseId, source.Id, null, default);

        var ok  = Assert.IsType<OkObjectResult>(result.Result);
        var rec = Assert.IsType<CaseFileRecord>(ok.Value);
        Assert.NotEqual(source.Id, rec.UploadFileId);
    }

    [Fact]
    public async Task Link_CreatesIndependentUploadFileCopy_WithLineageToSource()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var source = await SeedSourceFileAsync(factory, userId);
        var ctrl = BuildController(factory, userId, MakeReadableStorageMock([1, 2, 3, 4]));

        var result = await ctrl.Link(orgId, caseId, source.Id, null, default);
        var rec = (CaseFileRecord)((OkObjectResult)result.Result!).Value!;

        await using var db = await factory.CreateDbContextAsync();
        var copy = await db.UploadFiles.FirstAsync(f => f.Id == rec.UploadFileId);
        Assert.Equal(source.Id, copy.CaseCopyOfUploadFileId);
        Assert.Null(copy.ParentFileId); // distinct lineage field — does not alias clip lineage
        Assert.Equal(userId, copy.AppUserId); // owned by the linking user, not the source's owner
        Assert.True(await db.UploadFiles.AnyAsync(f => f.Id == source.Id)); // source untouched
    }

    [Fact]
    public async Task Link_CopyUsesFixedCaseEvidenceFileType()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var source = await SeedSourceFileAsync(factory, userId);
        var ctrl = BuildController(factory, userId, MakeReadableStorageMock([1, 2, 3, 4]));

        var result = await ctrl.Link(orgId, caseId, source.Id, null, default);
        var rec = (CaseFileRecord)((OkObjectResult)result.Result!).Value!;

        await using var db = await factory.CreateDbContextAsync();
        var copy = await db.UploadFiles.FirstAsync(f => f.Id == rec.UploadFileId);
        Assert.NotEqual(source.UploadFileTypeId, copy.UploadFileTypeId);
        Assert.Equal(new Guid("20000000-0000-0000-0000-000000000001"), copy.UploadFileTypeId);
    }

    [Fact]
    public async Task Link_SourceNotVisibleToLinkingUser_ReturnsForbid()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        // Private file owned by a stranger, not shared/public/case-linked anywhere.
        var source = await SeedSourceFileAsync(factory, Guid.NewGuid(), isPublic: false);
        var ctrl = BuildController(factory, userId, MakeReadableStorageMock([1, 2, 3, 4]));

        var result = await ctrl.Link(orgId, caseId, source.Id, null, default);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task Link_PublicSourceVisibleToLinkingUser_Succeeds()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var source = await SeedSourceFileAsync(factory, Guid.NewGuid(), isPublic: true);
        var ctrl = BuildController(factory, userId, MakeReadableStorageMock([1, 2, 3, 4]));

        var result = await ctrl.Link(orgId, caseId, source.Id, null, default);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task Link_AlreadyLinkedToThisCase_ReturnsConflict()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var source = await SeedSourceFileAsync(factory, userId);
        var ctrl = BuildController(factory, userId, MakeReadableStorageMock([1, 2, 3, 4]));
        await ctrl.Link(orgId, caseId, source.Id, null, default);

        var second = await ctrl.Link(orgId, caseId, source.Id, null, default);

        Assert.IsType<ConflictObjectResult>(second.Result);
    }

    [Fact]
    public async Task Link_LegacyFileDataFallback_UsesFileDataWhenStoragePathMissing()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var source = await SeedSourceFileAsync(factory, userId, storagePath: null, fileData: [9, 9, 9]);
        var ctrl = BuildController(factory, userId, new Mock<IFileStorageService>());

        var result = await ctrl.Link(orgId, caseId, source.Id, null, default);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task Link_NoStoredContentAtAll_ReturnsUnprocessableEntity()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var source = await SeedSourceFileAsync(factory, userId, storagePath: null, fileData: null);
        var ctrl = BuildController(factory, userId, new Mock<IFileStorageService>());

        var result = await ctrl.Link(orgId, caseId, source.Id, null, default);

        Assert.IsType<UnprocessableEntityObjectResult>(result.Result);
    }

    [Fact]
    public async Task Link_NonMember_ReturnsForbid()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var source = await SeedSourceFileAsync(factory, userId, isPublic: true);
        var ctrl = BuildController(factory, Guid.NewGuid(), MakeReadableStorageMock([1, 2, 3, 4]));

        var result = await ctrl.Link(orgId, caseId, source.Id, null, default);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task Link_SourceFileNotFound_ReturnsNotFound()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var ctrl = BuildController(factory, userId, MakeReadableStorageMock([1, 2, 3, 4]));

        var result = await ctrl.Link(orgId, caseId, Guid.NewGuid(), null, default);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }
}
