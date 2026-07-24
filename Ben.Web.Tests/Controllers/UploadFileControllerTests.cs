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
            new Moq.Mock<IAuditLogService>().Object);
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
}
