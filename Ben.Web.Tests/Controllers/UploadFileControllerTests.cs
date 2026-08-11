using AutoMapper;
using System.Security.Claims;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Helpers;
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
/// Unit tests for the extension validation logic added to UploadFileController.Upload.
/// The file is rejected (400) when AllowAllExtensions=false and no pattern matches.
/// </summary>
public class UploadFileControllerTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var options = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PooledDbContextFactory<BenDataContext>(options);
    }

    private static UploadFileController BuildController(IDbContextFactory<BenDataContext> factory)
    {
        var mapperMock = new Mock<IMapper>();
        mapperMock
            .Setup(m => m.Map<UploadFileRecord>(It.IsAny<object>()))
            .Returns(new UploadFileRecord
            {
                FileName = "test.txt",
                StoredFileName = "stored.txt",
                ContentType = "text/plain"
            });
        var ctrl = new UploadFileController(factory, mapperMock.Object,
            new Moq.Mock<Ben.Data.Common.Interfaces.IFileStorageService>().Object,
            new Moq.Mock<IAuditLogService>().Object,
            new Ben.Data.WebApi.Services.FileMetadataExtractorService(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<UploadFileController>.Instance);
        ctrl.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "Bearer"))
            }
        };
        return ctrl;
    }

    private static UploadFileController BuildController(IDbContextFactory<BenDataContext> factory, Guid userId,
        Mock<Ben.Data.Common.Interfaces.IFileStorageService>? storageMock = null)
    {
        var storage = storageMock ?? new Mock<Ben.Data.Common.Interfaces.IFileStorageService>();
        storage.Setup(s => s.UserFilePath(It.IsAny<Guid>(), It.IsAny<string>()))
               .Returns<Guid, string>((uid, name) => $"users/{uid}/{name}");
        storage.Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
               .Returns(Task.CompletedTask);

        var mapperMock = new Mock<IMapper>();
        mapperMock.Setup(m => m.Map<UploadFileRecord>(It.IsAny<object>()))
            .Returns<object>(o => o is UploadFile f
                ? new UploadFileRecord
                {
                    Id = f.Id, AppUserId = f.AppUserId, FileName = f.FileName, StoredFileName = f.StoredFileName,
                    ContentType = f.ContentType, FileSize = f.FileSize, StoragePath = f.StoragePath,
                    ArchivedFromUploadFileId = f.ArchivedFromUploadFileId, CaseCopyOfUploadFileId = f.CaseCopyOfUploadFileId,
                }
                : new UploadFileRecord { FileName = "test.txt", StoredFileName = "stored.txt", ContentType = "text/plain" });
        mapperMock.Setup(m => m.Map<IEnumerable<UploadFileRecord>>(It.IsAny<object>()))
            .Returns<object>(o => ((IEnumerable<UploadFile>)o).Select(f => new UploadFileRecord
            {
                Id = f.Id, AppUserId = f.AppUserId, FileName = f.FileName, StoredFileName = f.StoredFileName,
                ContentType = f.ContentType, FileSize = f.FileSize, ArchivedFromUploadFileId = f.ArchivedFromUploadFileId,
            }));

        var ctrl = new UploadFileController(factory, mapperMock.Object, storage.Object,
            new Mock<IAuditLogService>().Object, new Ben.Data.WebApi.Services.FileMetadataExtractorService(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<UploadFileController>.Instance);
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

    /// <summary>An unauthenticated caller — Download is [AllowAnonymous], so it needs its own builder.</summary>
    private static UploadFileController BuildAnonymousController(IDbContextFactory<BenDataContext> factory,
        Mock<Ben.Data.Common.Interfaces.IFileStorageService>? storageMock = null)
    {
        var storage = storageMock ?? new Mock<Ben.Data.Common.Interfaces.IFileStorageService>();
        var ctrl = new UploadFileController(factory, new Mock<IMapper>().Object, storage.Object,
            new Mock<IAuditLogService>().Object, new Ben.Data.WebApi.Services.FileMetadataExtractorService(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<UploadFileController>.Instance);
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity()) // no authenticationType → IsAuthenticated == false
            }
        };
        return ctrl;
    }

    private static IFormFile MakeFile(string fileName, long size = 256)
    {
        var fileMock = new Mock<IFormFile>();
        var bytes    = new byte[size];
        fileMock.Setup(f => f.FileName).Returns(fileName);
        fileMock.Setup(f => f.Length).Returns(size);
        fileMock.Setup(f => f.ContentType).Returns("application/octet-stream");
        fileMock.Setup(f => f.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                .Returns<Stream, CancellationToken>((s, _) =>
                {
                    s.Write(bytes, 0, bytes.Length);
                    return Task.CompletedTask;
                });
        return fileMock.Object;
    }

    private static async Task<Guid> SeedFileType(
        IDbContextFactory<BenDataContext> factory,
        bool allowAll,
        string[]? patterns = null)
    {
        var creatorId = Guid.NewGuid();
        var typeId    = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();

        db.UploadFileTypes.Add(new UploadFileType
        {
            Id = typeId,
            Name = "Test Type",
            IsActive = true,
            IsPublic = true,
            SortOrder = 1,
            AllowAllExtensions = allowAll,
            DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = creatorId
        });

        if (patterns is not null)
        {
            foreach (var pattern in patterns)
            {
                db.UploadFileTypeExtensions.Add(new UploadFileTypeExtension
                {
                    Id = Guid.NewGuid(),
                    UploadFileTypeId = typeId,
                    Pattern = pattern,
                    DateCreated = DateTime.UtcNow,
                    CreatedByAppUserId = creatorId
                });
            }
        }

        await db.SaveChangesAsync();
        return typeId;
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Upload_WhenFileTypeNotFound_ReturnsBadRequest()
    {
        var factory    = CreateFactory();
        var controller = BuildController(factory);

        var result = await controller.Upload(
            Guid.NewGuid(), Guid.NewGuid(), null, false,
            MakeFile("test.txt"), default);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("not found", bad.Value?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Upload_WhenAllowAllExtensions_AcceptsAnyExtension()
    {
        var factory    = CreateFactory();
        var typeId     = await SeedFileType(factory, allowAll: true);
        var controller = BuildController(factory);

        var result = await controller.Upload(
            typeId, Guid.NewGuid(), null, false,
            MakeFile("archive.xyz"), default);

        Assert.IsType<CreatedAtActionResult>(result.Result);
    }

    [Fact]
    public async Task Upload_WhenExtensionMatchesPattern_ReturnsCreated()
    {
        var factory    = CreateFactory();
        var typeId     = await SeedFileType(factory, allowAll: false, patterns: [".txt", ".doc*"]);
        var controller = BuildController(factory);

        var result = await controller.Upload(
            typeId, Guid.NewGuid(), null, false,
            MakeFile("report.docx"), default);

        Assert.IsType<CreatedAtActionResult>(result.Result);
    }

    [Fact]
    public async Task Upload_WhenExtensionNotAllowed_ReturnsBadRequest()
    {
        var factory    = CreateFactory();
        var typeId     = await SeedFileType(factory, allowAll: false, patterns: [".txt", ".pdf"]);
        var controller = BuildController(factory);

        var result = await controller.Upload(
            typeId, Guid.NewGuid(), null, false,
            MakeFile("photo.png"), default);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains(".png", bad.Value?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    // ── GetChildClips regression (item #6 phase 2) ──────────────────────────────
    // A case-copy (CaseCopyOfUploadFileId set, item #6 phase 2's copy-on-attach) must NOT show up
    // as a "child clip" of its source file — ParentFileId and CaseCopyOfUploadFileId are
    // deliberately separate fields specifically so this endpoint's existing, unfiltered
    // `Where(f => f.ParentFileId == id)` query stays untouched by the new copy-on-attach feature.

    [Fact]
    public async Task GetChildClips_DoesNotIncludeCaseCopies()
    {
        var factory = CreateFactory();
        var ownerId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var realClipId = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFiles.Add(new UploadFile
            {
                Id = sourceId, UploadFileTypeId = Guid.NewGuid(), AppUserId = ownerId,
                FileName = "source.mp3", StoredFileName = "s.mp3", ContentType = "audio/mpeg", FileSize = 1,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            db.UploadFiles.Add(new UploadFile // a real region-clip of the source
            {
                Id = realClipId, UploadFileTypeId = Guid.NewGuid(), AppUserId = ownerId,
                FileName = "clip.mp3", StoredFileName = "c.mp3", ContentType = "audio/mpeg", FileSize = 1,
                ParentFileId = sourceId, RegionStart = 0, RegionEnd = 5,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            db.UploadFiles.Add(new UploadFile // a case-copy of the source — must NOT appear below
            {
                Id = Guid.NewGuid(), UploadFileTypeId = Guid.NewGuid(), AppUserId = Guid.NewGuid(),
                FileName = "source.mp3", StoredFileName = "copy.mp3", ContentType = "audio/mpeg", FileSize = 1,
                CaseCopyOfUploadFileId = sourceId,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            await db.SaveChangesAsync();
        }

        var mapperMock = new Mock<IMapper>();
        mapperMock.Setup(m => m.Map<IEnumerable<UploadFileRecord>>(It.IsAny<object>()))
            .Returns<object>(o => ((IEnumerable<UploadFile>)o).Select(f => new UploadFileRecord
            {
                Id = f.Id, FileName = f.FileName, StoredFileName = f.StoredFileName, ContentType = f.ContentType,
            }));
        var ctrl = new UploadFileController(factory, mapperMock.Object,
            new Mock<Ben.Data.Common.Interfaces.IFileStorageService>().Object,
            new Mock<IAuditLogService>().Object,
            new Ben.Data.WebApi.Services.FileMetadataExtractorService(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<UploadFileController>.Instance);

        var result = await ctrl.GetChildClips(sourceId, default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var clips = Assert.IsAssignableFrom<IEnumerable<UploadFileRecord>>(ok.Value);
        var clipList = clips.ToList();
        Assert.Single(clipList);
        Assert.Equal(realClipId, clipList[0].Id);
    }

    // ── Replace (item #6 phase 3) ────────────────────────────────────────────────
    // Note: the metadata delete-then-add refresh in Replace is fire-and-forget (Task.Run, matching
    // the pre-existing initial-upload extraction pattern) so replace latency is unaffected — like
    // that pre-existing pattern, it isn't asserted here; a synchronous test would race the
    // background task rather than prove anything.

    [Fact]
    public async Task Replace_KeepsSourceId_UpdatesBytesAndArchivesOldVersion()
    {
        var factory  = CreateFactory();
        var ownerId  = Guid.NewGuid();
        var typeId   = await SeedFileType(factory, allowAll: true);
        var fileId   = Guid.NewGuid();
        const string oldStoragePath = "users/owner/old.jpg";

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFiles.Add(new UploadFile
            {
                Id = fileId, UploadFileTypeId = typeId, AppUserId = ownerId,
                FileName = "photo.jpg", StoredFileName = "old.jpg", ContentType = "image/jpeg",
                FileSize = 100, StoragePath = oldStoragePath,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            await db.SaveChangesAsync();
        }

        var ctrl   = BuildController(factory, ownerId);
        var result = await ctrl.Replace(fileId, MakeFile("photo.jpg", 999), default);

        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var record = Assert.IsType<UploadFileRecord>(ok.Value);
        Assert.Equal(fileId, record.Id); // same Id — existing comments/votes/shares/case-links stay attached
        Assert.Equal(999, record.FileSize);
        Assert.NotEqual(oldStoragePath, record.StoragePath); // moved to a fresh path

        await using var verifyDb = await factory.CreateDbContextAsync();
        var all = await verifyDb.UploadFiles.ToListAsync();
        Assert.Equal(2, all.Count); // source + archive

        var archive = all.Single(f => f.Id != fileId);
        Assert.Equal(fileId, archive.ArchivedFromUploadFileId);
        Assert.Equal(oldStoragePath, archive.StoragePath); // inherits the OLD path — no byte copy
        Assert.Equal("old.jpg", archive.StoredFileName);
        Assert.Equal(100, archive.FileSize);
    }

    [Fact]
    public async Task Replace_NonOwner_ReturnsForbid()
    {
        var factory = CreateFactory();
        var ownerId = Guid.NewGuid();
        var typeId  = await SeedFileType(factory, allowAll: true);
        var fileId  = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFiles.Add(new UploadFile
            {
                Id = fileId, UploadFileTypeId = typeId, AppUserId = ownerId,
                FileName = "photo.jpg", StoredFileName = "old.jpg", ContentType = "image/jpeg",
                FileSize = 100, StoragePath = "users/owner/old.jpg",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            await db.SaveChangesAsync();
        }

        var ctrl   = BuildController(factory, Guid.NewGuid()); // different user
        var result = await ctrl.Replace(fileId, MakeFile("photo.jpg"), default);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task Replace_ExtensionMismatch_ReturnsBadRequest()
    {
        var factory = CreateFactory();
        var ownerId = Guid.NewGuid();
        var typeId  = await SeedFileType(factory, allowAll: true);
        var fileId  = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFiles.Add(new UploadFile
            {
                Id = fileId, UploadFileTypeId = typeId, AppUserId = ownerId,
                FileName = "photo.jpg", StoredFileName = "old.jpg", ContentType = "image/jpeg",
                FileSize = 100, StoragePath = "users/owner/old.jpg",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            await db.SaveChangesAsync();
        }

        var ctrl   = BuildController(factory, ownerId);
        var result = await ctrl.Replace(fileId, MakeFile("photo.png"), default); // different extension

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("extension", bad.Value?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Replace_PropagatesToCaseCopies_WithoutArchivingThem()
    {
        var factory    = CreateFactory();
        var ownerId    = Guid.NewGuid();
        var typeId     = await SeedFileType(factory, allowAll: true);
        var sourceId   = Guid.NewGuid();
        var copyId     = Guid.NewGuid();
        var caseFileId = Guid.NewGuid();
        var caseId     = Guid.NewGuid();
        var orgId      = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Organizations.Add(new Organization
            {
                Id = orgId, Name = "Test Org", UrlName = "test",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            db.Cases.Add(new Case
            {
                Id = caseId, OrganizationId = orgId, Title = "Test Case",
                CaseYear = 2026, OrgCaseNumber = 1,
                StreetAddress1 = "1 Main", City = "Nashville", State = "TN", ZipCode = "37201", Country = "US",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            db.UploadFiles.Add(new UploadFile
            {
                Id = sourceId, UploadFileTypeId = typeId, AppUserId = ownerId,
                FileName = "photo.jpg", StoredFileName = "old.jpg", ContentType = "image/jpeg",
                FileSize = 100, StoragePath = "users/owner/old.jpg",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            db.UploadFiles.Add(new UploadFile
            {
                Id = copyId, UploadFileTypeId = typeId, AppUserId = ownerId,
                FileName = "photo.jpg", StoredFileName = "copy.jpg", ContentType = "image/jpeg",
                FileSize = 100, StoragePath = "cases/case1/files/copy.jpg", CaseCopyOfUploadFileId = sourceId,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            db.CaseFiles.Add(new CaseFile
            {
                Id = caseFileId, CaseId = caseId, UploadFileId = copyId,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            await db.SaveChangesAsync();
        }

        var ctrl   = BuildController(factory, ownerId);
        var result = await ctrl.Replace(sourceId, MakeFile("photo.jpg", 777), default);
        Assert.IsType<OkObjectResult>(result.Result);

        await using var verifyDb = await factory.CreateDbContextAsync();
        var copy = await verifyDb.UploadFiles.SingleAsync(f => f.Id == copyId);
        Assert.Equal(777, copy.FileSize);
        Assert.Equal("cases/case1/files/copy.jpg", copy.StoragePath); // overwritten AT its existing path
        Assert.Null(copy.ArchivedFromUploadFileId); // only the source gets an archive row, not the copy

        var caseFile = await verifyDb.CaseFiles.SingleAsync(cf => cf.Id == caseFileId);
        Assert.Equal(copyId, caseFile.UploadFileId); // CaseFile pointer untouched
    }

    // ── GetAll archived-row exclusion (item #6 phase 3) ──────────────────────────

    [Fact]
    public async Task GetAll_ExcludesArchivedRows()
    {
        var factory = CreateFactory();
        var ownerId = Guid.NewGuid();
        var liveId  = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFiles.Add(new UploadFile
            {
                Id = liveId, UploadFileTypeId = Guid.NewGuid(), AppUserId = ownerId,
                FileName = "photo.jpg", StoredFileName = "live.jpg", ContentType = "image/jpeg", FileSize = 1,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            db.UploadFiles.Add(new UploadFile
            {
                Id = Guid.NewGuid(), UploadFileTypeId = Guid.NewGuid(), AppUserId = ownerId,
                FileName = "photo.jpg", StoredFileName = "archived.jpg", ContentType = "image/jpeg", FileSize = 1,
                ArchivedFromUploadFileId = liveId,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            await db.SaveChangesAsync();
        }

        var ctrl   = BuildController(factory, ownerId);
        var result = await ctrl.GetAll(default);

        var ok    = Assert.IsType<OkObjectResult>(result.Result);
        var files = Assert.IsAssignableFrom<IEnumerable<UploadFileRecord>>(ok.Value).ToList();
        Assert.Single(files);
        Assert.Equal(liveId, files[0].Id);
    }

    [Fact]
    public async Task GetChildClips_DoesNotIncludeArchivedVersions()
    {
        var factory    = CreateFactory();
        var ownerId    = Guid.NewGuid();
        var sourceId   = Guid.NewGuid();
        var realClipId = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFiles.Add(new UploadFile
            {
                Id = sourceId, UploadFileTypeId = Guid.NewGuid(), AppUserId = ownerId,
                FileName = "source.mp3", StoredFileName = "s.mp3", ContentType = "audio/mpeg", FileSize = 1,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            db.UploadFiles.Add(new UploadFile // a real region-clip of the source
            {
                Id = realClipId, UploadFileTypeId = Guid.NewGuid(), AppUserId = ownerId,
                FileName = "clip.mp3", StoredFileName = "c.mp3", ContentType = "audio/mpeg", FileSize = 1,
                ParentFileId = sourceId, RegionStart = 0, RegionEnd = 5,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            db.UploadFiles.Add(new UploadFile // an archived prior version of the source — must NOT appear below
            {
                Id = Guid.NewGuid(), UploadFileTypeId = Guid.NewGuid(), AppUserId = ownerId,
                FileName = "source.mp3", StoredFileName = "old.mp3", ContentType = "audio/mpeg", FileSize = 1,
                ArchivedFromUploadFileId = sourceId,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            await db.SaveChangesAsync();
        }

        var ctrl   = BuildController(factory, ownerId);
        var result = await ctrl.GetChildClips(sourceId, default);

        var ok    = Assert.IsType<OkObjectResult>(result.Result);
        var clips = Assert.IsAssignableFrom<IEnumerable<UploadFileRecord>>(ok.Value).ToList();
        Assert.Single(clips);
        Assert.Equal(realClipId, clips[0].Id);
    }

    // ── GetReplaceImpact (item #6 phase 3) ────────────────────────────────────────

    [Fact]
    public async Task GetReplaceImpact_ReturnsCasesWithCommentAndVoteCounts()
    {
        var factory  = CreateFactory();
        var ownerId  = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var copyId   = Guid.NewGuid();
        var caseId   = Guid.NewGuid();
        var orgId    = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Organizations.Add(new Organization
            {
                Id = orgId, Name = "Test Org", UrlName = "test",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            db.Cases.Add(new Case
            {
                Id = caseId, OrganizationId = orgId, Title = "Test Case",
                CaseYear = 2026, OrgCaseNumber = 1,
                StreetAddress1 = "1 Main", City = "Nashville", State = "TN", ZipCode = "37201", Country = "US",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            db.UploadFiles.Add(new UploadFile
            {
                Id = sourceId, UploadFileTypeId = Guid.NewGuid(), AppUserId = ownerId,
                FileName = "photo.jpg", StoredFileName = "old.jpg", ContentType = "image/jpeg", FileSize = 1,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            db.UploadFiles.Add(new UploadFile
            {
                Id = copyId, UploadFileTypeId = Guid.NewGuid(), AppUserId = ownerId,
                FileName = "photo.jpg", StoredFileName = "copy.jpg", ContentType = "image/jpeg", FileSize = 1,
                CaseCopyOfUploadFileId = sourceId,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            db.CaseFiles.Add(new CaseFile
            {
                Id = Guid.NewGuid(), CaseId = caseId, UploadFileId = copyId,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            db.UploadFileComments.Add(new UploadFileComment
            {
                Id = Guid.NewGuid(), UploadFileId = copyId, AuthorAppUserId = Guid.NewGuid(), Text = "Looks real",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            db.EvidenceVotes.Add(new EvidenceVote
            {
                Id = Guid.NewGuid(), UploadFileId = copyId, VoterAppUserId = Guid.NewGuid(),
                VoteType = EvidenceVoteType.Confirms, DateVoted = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var ctrl   = BuildController(factory, ownerId);
        var result = await ctrl.GetReplaceImpact(sourceId, default);

        var ok         = Assert.IsType<OkObjectResult>(result.Result);
        var impact     = Assert.IsType<ReplaceImpactRecord>(ok.Value);
        var caseImpact = Assert.Single(impact.Cases);
        Assert.Equal(caseId, caseImpact.CaseId);
        Assert.Equal("Test Case", caseImpact.CaseTitle);
        Assert.Equal("Test Org", caseImpact.OrganizationName);
        Assert.Equal(1, caseImpact.CommentCount);
        Assert.Equal(1, caseImpact.VoteCount);
    }

    [Fact]
    public async Task GetReplaceImpact_NonOwner_ReturnsForbid()
    {
        var factory = CreateFactory();
        var ownerId = Guid.NewGuid();
        var fileId  = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFiles.Add(new UploadFile
            {
                Id = fileId, UploadFileTypeId = Guid.NewGuid(), AppUserId = ownerId,
                FileName = "photo.jpg", StoredFileName = "old.jpg", ContentType = "image/jpeg", FileSize = 1,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            await db.SaveChangesAsync();
        }

        var ctrl   = BuildController(factory, Guid.NewGuid());
        var result = await ctrl.GetReplaceImpact(fileId, default);

        Assert.IsType<ForbidResult>(result.Result);
    }

    // ── GetAll / GetById / Download authorization gap fix ────────────────────────
    // GetAll previously had no owner filter (returned every UploadFile row in the system);
    // GetById had no visibility check at all; Download only checked IsPublic. All three now
    // require FileAudienceAccess.CanViewFileAsync (GetAll additionally scopes to the caller's own
    // files, since it backs a personal "my files" page, not a browse-everything view).

    [Fact]
    public async Task GetAll_OnlyReturnsCallersOwnFiles()
    {
        var factory  = CreateFactory();
        var ownerId  = Guid.NewGuid();
        var otherId  = Guid.NewGuid();
        var ownFileId = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFiles.Add(new UploadFile
            {
                Id = ownFileId, UploadFileTypeId = Guid.NewGuid(), AppUserId = ownerId,
                FileName = "mine.jpg", StoredFileName = "mine.jpg", ContentType = "image/jpeg", FileSize = 1,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            db.UploadFiles.Add(new UploadFile // someone else's file — must NOT appear
            {
                Id = Guid.NewGuid(), UploadFileTypeId = Guid.NewGuid(), AppUserId = otherId,
                FileName = "theirs.jpg", StoredFileName = "theirs.jpg", ContentType = "image/jpeg", FileSize = 1,
                IsPublic = true, // even public files aren't "mine" — this page is the caller's own file cabinet
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = otherId,
            });
            await db.SaveChangesAsync();
        }

        var ctrl   = BuildController(factory, ownerId);
        var result = await ctrl.GetAll(default);

        var ok    = Assert.IsType<OkObjectResult>(result.Result);
        var files = Assert.IsAssignableFrom<IEnumerable<UploadFileRecord>>(ok.Value).ToList();
        Assert.Single(files);
        Assert.Equal(ownFileId, files[0].Id);
    }

    [Fact]
    public async Task GetById_NonOwnerWithoutAccess_ReturnsNotFound()
    {
        var factory = CreateFactory();
        var ownerId = Guid.NewGuid();
        var fileId  = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFiles.Add(new UploadFile
            {
                Id = fileId, UploadFileTypeId = Guid.NewGuid(), AppUserId = ownerId,
                FileName = "private.jpg", StoredFileName = "private.jpg", ContentType = "image/jpeg", FileSize = 1,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            await db.SaveChangesAsync();
        }

        var ctrl   = BuildController(factory, Guid.NewGuid()); // unrelated user, no share/org/case link
        var result = await ctrl.GetById(fileId, default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetById_Owner_ReturnsFile()
    {
        var factory = CreateFactory();
        var ownerId = Guid.NewGuid();
        var fileId  = Guid.NewGuid();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFiles.Add(new UploadFile
            {
                Id = fileId, UploadFileTypeId = Guid.NewGuid(), AppUserId = ownerId,
                FileName = "private.jpg", StoredFileName = "private.jpg", ContentType = "image/jpeg", FileSize = 1,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            await db.SaveChangesAsync();
        }

        var ctrl   = BuildController(factory, ownerId);
        var result = await ctrl.GetById(fileId, default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(fileId, Assert.IsType<UploadFileRecord>(ok.Value).Id);
    }

    [Fact]
    public async Task Download_PrivateFile_UnrelatedAuthenticatedUser_ReturnsForbid()
    {
        var factory = CreateFactory();
        var ownerId = Guid.NewGuid();
        var fileId  = Guid.NewGuid();

        var storage = new Mock<Ben.Data.Common.Interfaces.IFileStorageService>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFiles.Add(new UploadFile
            {
                Id = fileId, UploadFileTypeId = Guid.NewGuid(), AppUserId = ownerId,
                FileName = "private.jpg", StoredFileName = "private.jpg", ContentType = "image/jpeg", FileSize = 1,
                StoragePath = "users/owner/private.jpg", IsPublic = false,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            await db.SaveChangesAsync();
        }

        var ctrl   = BuildController(factory, Guid.NewGuid(), storage); // unrelated user
        var result = await ctrl.Download(fileId, default);

        Assert.IsType<ForbidResult>(result);
        storage.Verify(s => s.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Download_PrivateFile_Anonymous_ReturnsUnauthorized()
    {
        var factory = CreateFactory();
        var ownerId = Guid.NewGuid();
        var fileId  = Guid.NewGuid();

        var storage = new Mock<Ben.Data.Common.Interfaces.IFileStorageService>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFiles.Add(new UploadFile
            {
                Id = fileId, UploadFileTypeId = Guid.NewGuid(), AppUserId = ownerId,
                FileName = "private.jpg", StoredFileName = "private.jpg", ContentType = "image/jpeg", FileSize = 1,
                StoragePath = "users/owner/private.jpg", IsPublic = false,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            await db.SaveChangesAsync();
        }

        var ctrl   = BuildAnonymousController(factory, storage);
        var result = await ctrl.Download(fileId, default);

        Assert.IsType<UnauthorizedResult>(result);
        storage.Verify(s => s.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Download_PublicFile_Anonymous_ReturnsFileContent()
    {
        var factory = CreateFactory();
        var ownerId = Guid.NewGuid();
        var fileId  = Guid.NewGuid();

        var storage = new Mock<Ben.Data.Common.Interfaces.IFileStorageService>();
        storage.Setup(s => s.OpenReadAsync("users/owner/public.jpg", It.IsAny<CancellationToken>()))
               .ReturnsAsync(new MemoryStream([1, 2, 3]));
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFiles.Add(new UploadFile
            {
                Id = fileId, UploadFileTypeId = Guid.NewGuid(), AppUserId = ownerId,
                FileName = "public.jpg", StoredFileName = "public.jpg", ContentType = "image/jpeg", FileSize = 3,
                StoragePath = "users/owner/public.jpg", IsPublic = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            await db.SaveChangesAsync();
        }

        var ctrl   = BuildAnonymousController(factory, storage);
        var result = await ctrl.Download(fileId, default);

        Assert.IsType<FileStreamResult>(result);
    }

    [Fact]
    public async Task Download_PrivateFile_Owner_ReturnsFileContent()
    {
        var factory = CreateFactory();
        var ownerId = Guid.NewGuid();
        var fileId  = Guid.NewGuid();

        var storage = new Mock<Ben.Data.Common.Interfaces.IFileStorageService>();
        storage.Setup(s => s.OpenReadAsync("users/owner/private.jpg", It.IsAny<CancellationToken>()))
               .ReturnsAsync(new MemoryStream([1, 2, 3]));
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFiles.Add(new UploadFile
            {
                Id = fileId, UploadFileTypeId = Guid.NewGuid(), AppUserId = ownerId,
                FileName = "private.jpg", StoredFileName = "private.jpg", ContentType = "image/jpeg", FileSize = 3,
                StoragePath = "users/owner/private.jpg", IsPublic = false,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            await db.SaveChangesAsync();
        }

        var ctrl   = BuildController(factory, ownerId, storage);
        var result = await ctrl.Download(fileId, default);

        Assert.IsType<FileStreamResult>(result);
    }

    // ── Update / Delete ───────────────────────────────────────────────────────

    [Fact]
    public async Task Update_PersistsChanges_AndReturnsOk()
    {
        var factory = CreateFactory();
        var ownerId = Guid.NewGuid();
        var fileId  = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFiles.Add(new UploadFile
            {
                Id = fileId, UploadFileTypeId = Guid.NewGuid(), AppUserId = ownerId,
                FileName = "a.jpg", StoredFileName = "a.jpg", ContentType = "image/jpeg", FileSize = 1,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            await db.SaveChangesAsync();
        }
        var newTypeId = Guid.NewGuid();
        var ctrl      = BuildController(factory, ownerId);

        var result = await ctrl.Update(fileId,
            new UpdateUploadFileRequest(newTypeId, "New description", true, 5, ownerId), default);

        Assert.IsType<OkObjectResult>(result.Result);
        await using var verify = await factory.CreateDbContextAsync();
        var updated = await verify.UploadFiles.FindAsync(fileId);
        Assert.Equal(newTypeId, updated!.UploadFileTypeId);
        Assert.Equal("New description", updated.Description);
    }

    [Fact]
    public async Task Update_FileNotFound_ReturnsNotFound()
    {
        var factory = CreateFactory();
        var ctrl    = BuildController(factory, Guid.NewGuid());

        var result = await ctrl.Update(Guid.NewGuid(),
            new UpdateUploadFileRequest(Guid.NewGuid(), null, false, 0, null), default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Update_ConcurrentWithDelete_NeverThrows()
    {
        // Regression: Update fetches "before" (untracked), then re-fetches the tracked row and
        // used to dereference it with `!` — if a concurrent Delete won that race, the second
        // fetch returned null and the unchecked `!` threw an unhandled NullReferenceException
        // (raw 500) instead of a clean NotFound.
        var factory = CreateFactory();
        var ownerId = Guid.NewGuid();
        var fileId  = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFiles.Add(new UploadFile
            {
                Id = fileId, UploadFileTypeId = Guid.NewGuid(), AppUserId = ownerId,
                FileName = "a.jpg", StoredFileName = "a.jpg", ContentType = "image/jpeg", FileSize = 1,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
            });
            await db.SaveChangesAsync();
        }
        var updateCtrl = BuildController(factory, ownerId);
        var deleteCtrl = BuildController(factory, ownerId);

        var updateTask = updateCtrl.Update(fileId,
            new UpdateUploadFileRequest(Guid.NewGuid(), "Renamed", true, 1, ownerId), default);
        var deleteTask = deleteCtrl.Delete(fileId, default);
        var (updateResult, deleteResult) = (await updateTask, await deleteTask);

        Assert.True(updateResult.Result is OkObjectResult or NotFoundResult);
        Assert.True(deleteResult is NoContentResult or NotFoundResult);
    }
}
