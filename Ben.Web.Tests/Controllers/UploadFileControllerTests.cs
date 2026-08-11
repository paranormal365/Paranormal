using AutoMapper;
using System.Security.Claims;
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
            new Ben.Data.WebApi.Services.FileMetadataExtractorService());
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
            new Ben.Data.WebApi.Services.FileMetadataExtractorService());

        var result = await ctrl.GetChildClips(sourceId, default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var clips = Assert.IsAssignableFrom<IEnumerable<UploadFileRecord>>(ok.Value);
        var clipList = clips.ToList();
        Assert.Single(clipList);
        Assert.Equal(realClipId, clipList[0].Id);
    }
}
