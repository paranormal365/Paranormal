using AutoMapper;
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
/// Tests for <see cref="UploadFileAudioEditController"/> — verifies input validation,
/// parent-file tracking, and unsupported-format rejection for each destructive edit operation.
/// </summary>
public class UploadFileAudioEditControllerTests
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
        m.Setup(x => x.Map<UploadFileRecord>(It.IsAny<object>()))
         .Returns<object>(o =>
         {
             if (o is not UploadFile e) return new UploadFileRecord
                 { FileName = "", StoredFileName = "", ContentType = "" };
             return new UploadFileRecord
             {
                 Id             = e.Id,
                 FileName       = e.FileName,
                 StoredFileName = e.StoredFileName,
                 ContentType    = e.ContentType,
                 FileSize       = e.FileSize,
                 Description    = e.Description,
                 IsPublic       = e.IsPublic,
                 ParentFileId   = e.ParentFileId,
                 CreatedByAppUserId = e.CreatedByAppUserId,
             };
         });
        return m.Object;
    }

    private static UploadFileAudioEditController Build(
        IDbContextFactory<BenDataContext> factory, Guid userId)
    {
        var ctrl = new UploadFileAudioEditController(factory, CreateMapper(),
            new Mock<Ben.Data.Common.Interfaces.IFileStorageService>().Object,
            new Mock<IAuditLogService>().Object);
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim(ClaimTypes.NameIdentifier, userId.ToString())
                ], "Bearer"))
            }
        };
        return ctrl;
    }

    /// <summary>Builds a valid 2-second silent PCM WAV as a byte array.</summary>
    private static byte[] CreateSilentWav(int seconds = 2, int sampleRate = 8000)
    {
        int numSamples = sampleRate * seconds;
        int dataSize   = numSamples * 2;          // 16-bit mono = 2 bytes/sample
        using var ms   = new System.IO.MemoryStream();
        using var w    = new System.IO.BinaryWriter(ms);

        w.Write(new[] { 'R', 'I', 'F', 'F' });
        w.Write(36 + dataSize);
        w.Write(new[] { 'W', 'A', 'V', 'E' });

        w.Write(new[] { 'f', 'm', 't', ' ' });
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(sampleRate);
        w.Write(sampleRate * 2);
        w.Write((short)2);
        w.Write((short)16);

        w.Write(new[] { 'd', 'a', 't', 'a' });
        w.Write(dataSize);
        w.Write(new byte[dataSize]);
        return ms.ToArray();
    }

    private static async Task<Guid> SeedTypeAsync(IDbContextFactory<BenDataContext> factory)
    {
        var typeId = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        db.UploadFileTypes.Add(new UploadFileType
        {
            Id = typeId, Name = "Audio", IsActive = true, IsPublic = true,
            AllowAllExtensions = true, SortOrder = 1,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid(),
        });
        await db.SaveChangesAsync();
        return typeId;
    }

    private static async Task<Guid> SeedFileAsync(
        IDbContextFactory<BenDataContext> factory,
        byte[]? fileData = null, string contentType = "audio/wav")
    {
        var fileId = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        db.UploadFiles.Add(new UploadFile
        {
            Id = fileId, UploadFileTypeId = Guid.NewGuid(), AppUserId = Guid.NewGuid(),
            FileName = "audio.wav", StoredFileName = "s.wav", ContentType = contentType,
            FileSize = fileData?.Length ?? 4, FileData = fileData ?? new byte[4],
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid(),
        });
        await db.SaveChangesAsync();
        return fileId;
    }

    private static AudioEditRequest Request(
        AudioEditOperation op, Guid typeId,
        double? start = null, double? end = null, double? gainDb = null,
        double? fadeIn = null, double? fadeOut = null)
        => new(op, start, end, gainDb, fadeIn, fadeOut, null, false, typeId);

    // ── Validation ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Edit_ReturnsUnauthorized_WhenNoUserClaim()
    {
        var factory = CreateFactory();
        var ctrl    = Build(factory, Guid.Empty);

        var result  = await ctrl.Edit(Guid.NewGuid(), Request(AudioEditOperation.Normalize, Guid.NewGuid()), default);

        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public async Task Edit_ReturnsBadRequest_ForCut_WhenStartOrEndMissing()
    {
        var factory = CreateFactory();
        var ctrl    = Build(factory, Guid.NewGuid());

        var result  = await ctrl.Edit(Guid.NewGuid(), Request(AudioEditOperation.Cut, Guid.NewGuid()), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Edit_ReturnsBadRequest_ForSilence_WhenEndNotGreaterThanStart()
    {
        var factory = CreateFactory();
        var ctrl    = Build(factory, Guid.NewGuid());

        var result  = await ctrl.Edit(Guid.NewGuid(),
            Request(AudioEditOperation.Silence, Guid.NewGuid(), start: 5, end: 5), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Edit_ReturnsBadRequest_ForGain_WhenGainDbMissing()
    {
        var factory = CreateFactory();
        var ctrl    = Build(factory, Guid.NewGuid());

        var result  = await ctrl.Edit(Guid.NewGuid(), Request(AudioEditOperation.Gain, Guid.NewGuid()), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Edit_ReturnsNotFound_WhenFileNotFound()
    {
        var factory = CreateFactory();
        var ctrl    = Build(factory, Guid.NewGuid());

        var result  = await ctrl.Edit(Guid.NewGuid(), Request(AudioEditOperation.Normalize, Guid.NewGuid()), default);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task Edit_ReturnsBadRequest_WhenFileTypeNotFound()
    {
        var factory = CreateFactory();
        var fileId  = await SeedFileAsync(factory, CreateSilentWav());
        var ctrl    = Build(factory, Guid.NewGuid());

        var result  = await ctrl.Edit(fileId, Request(AudioEditOperation.Normalize, Guid.NewGuid()), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Edit_ReturnsBadRequest_ForUnsupportedContentType()
    {
        var factory = CreateFactory();
        var typeId  = await SeedTypeAsync(factory);
        var fileId  = await SeedFileAsync(factory, new byte[100], "audio/ogg");
        var ctrl    = Build(factory, Guid.NewGuid());

        var result  = await ctrl.Edit(fileId, Request(AudioEditOperation.Reverse, typeId), default);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("WAV", bad.Value?.ToString());
    }

    // ── Success — one per operation ─────────────────────────────────────────────

    [Theory]
    [InlineData(AudioEditOperation.Normalize)]
    [InlineData(AudioEditOperation.Reverse)]
    public async Task Edit_Returns201_ForWholeFileOperations(AudioEditOperation op)
    {
        var factory = CreateFactory();
        var typeId  = await SeedTypeAsync(factory);
        var fileId  = await SeedFileAsync(factory, CreateSilentWav(seconds: 1));
        var ctrl    = Build(factory, Guid.NewGuid());

        var result  = await ctrl.Edit(fileId, Request(op, typeId), default);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var record  = Assert.IsType<UploadFileRecord>(created.Value);
        Assert.Equal(fileId, record.ParentFileId);
    }

    [Fact]
    public async Task Edit_Returns201_ForCutRegion()
    {
        var factory = CreateFactory();
        var typeId  = await SeedTypeAsync(factory);
        var fileId  = await SeedFileAsync(factory, CreateSilentWav(seconds: 2));
        var ctrl    = Build(factory, Guid.NewGuid());

        var result  = await ctrl.Edit(fileId, Request(AudioEditOperation.Cut, typeId, start: 0.5, end: 1.0), default);

        Assert.IsType<CreatedAtActionResult>(result.Result);
    }

    [Fact]
    public async Task Edit_Returns201_ForSilenceRegion()
    {
        var factory = CreateFactory();
        var typeId  = await SeedTypeAsync(factory);
        var fileId  = await SeedFileAsync(factory, CreateSilentWav(seconds: 2));
        var ctrl    = Build(factory, Guid.NewGuid());

        var result  = await ctrl.Edit(fileId, Request(AudioEditOperation.Silence, typeId, start: 0.5, end: 1.0), default);

        Assert.IsType<CreatedAtActionResult>(result.Result);
    }

    [Fact]
    public async Task Edit_Returns201_ForGain()
    {
        var factory = CreateFactory();
        var typeId  = await SeedTypeAsync(factory);
        var fileId  = await SeedFileAsync(factory, CreateSilentWav(seconds: 1));
        var ctrl    = Build(factory, Guid.NewGuid());

        var result  = await ctrl.Edit(fileId, Request(AudioEditOperation.Gain, typeId, gainDb: 6.0), default);

        Assert.IsType<CreatedAtActionResult>(result.Result);
    }

    [Fact]
    public async Task Edit_Returns201_ForFade()
    {
        var factory = CreateFactory();
        var typeId  = await SeedTypeAsync(factory);
        var fileId  = await SeedFileAsync(factory, CreateSilentWav(seconds: 2));
        var ctrl    = Build(factory, Guid.NewGuid());

        var result  = await ctrl.Edit(fileId, Request(AudioEditOperation.Fade, typeId, fadeIn: 0.5, fadeOut: 0.5), default);

        Assert.IsType<CreatedAtActionResult>(result.Result);
    }
}
