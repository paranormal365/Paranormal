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
/// Tests for <see cref="UploadFileAudioClipController"/> — verifies input validation,
/// parent-file tracking, region bounds storage, and unsupported-format rejection.
/// </summary>
public class UploadFileAudioClipControllerTests
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
                 RegionStart    = e.RegionStart,
                 RegionEnd      = e.RegionEnd,
                 CreatedByAppUserId = e.CreatedByAppUserId,
             };
         });
        return m.Object;
    }

    private static UploadFileAudioClipController Build(
        IDbContextFactory<BenDataContext> factory, Guid userId)
    {
        var ctrl = new UploadFileAudioClipController(factory, CreateMapper());
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

        // RIFF header
        w.Write(new[] { 'R', 'I', 'F', 'F' });
        w.Write(36 + dataSize);                  // file size − 8
        w.Write(new[] { 'W', 'A', 'V', 'E' });

        // fmt chunk
        w.Write(new[] { 'f', 'm', 't', ' ' });
        w.Write(16);             // PCM chunk size
        w.Write((short)1);       // PCM
        w.Write((short)1);       // mono
        w.Write(sampleRate);     // sample rate
        w.Write(sampleRate * 2); // byte rate
        w.Write((short)2);       // block align
        w.Write((short)16);      // bits per sample

        // data chunk
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

    // ── Validation ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Clip_ReturnsBadRequest_WhenEndBeforeStart()
    {
        var factory = CreateFactory();
        var ctrl    = Build(factory, Guid.NewGuid());
        var request = new ClipAudioRequest(10.0, 5.0, null, false, Guid.NewGuid());

        var result  = await ctrl.Clip(Guid.NewGuid(), request, default);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("End must be greater than Start", bad.Value?.ToString());
    }

    [Fact]
    public async Task Clip_ReturnsBadRequest_WhenEndEqualsStart()
    {
        var factory = CreateFactory();
        var ctrl    = Build(factory, Guid.NewGuid());
        var request = new ClipAudioRequest(5.0, 5.0, null, false, Guid.NewGuid());

        var result  = await ctrl.Clip(Guid.NewGuid(), request, default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Clip_ReturnsNotFound_WhenFileNotFound()
    {
        var factory = CreateFactory();
        var ctrl    = Build(factory, Guid.NewGuid());

        var result  = await ctrl.Clip(Guid.NewGuid(),
            new ClipAudioRequest(0, 1, null, false, Guid.NewGuid()), default);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task Clip_ReturnsBadRequest_WhenFileTypeNotFound()
    {
        var factory = CreateFactory();
        var fileId  = await SeedFileAsync(factory, CreateSilentWav());
        var ctrl    = Build(factory, Guid.NewGuid());

        var result  = await ctrl.Clip(fileId,
            new ClipAudioRequest(0, 1, null, false, Guid.NewGuid()), default);  // random file type

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Clip_ReturnsBadRequest_ForUnsupportedContentType()
    {
        var factory  = CreateFactory();
        var typeId   = await SeedTypeAsync(factory);
        var fileId   = await SeedFileAsync(factory, new byte[100], "audio/ogg");
        var ctrl     = Build(factory, Guid.NewGuid());

        var result   = await ctrl.Clip(fileId,
            new ClipAudioRequest(0, 0.5, null, false, typeId), default);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("WAV", bad.Value?.ToString());
    }

    // ── Success — parent-file tracking ────────────────────────────────────────

    [Fact]
    public async Task Clip_Returns201_WhenWavIsClipped()
    {
        var factory = CreateFactory();
        var typeId  = await SeedTypeAsync(factory);
        var fileId  = await SeedFileAsync(factory, CreateSilentWav(seconds: 2));
        var userId  = Guid.NewGuid();
        var ctrl    = Build(factory, userId);

        var result  = await ctrl.Clip(fileId,
            new ClipAudioRequest(0.0, 1.0, null, false, typeId), default);

        Assert.IsType<CreatedAtActionResult>(result.Result);
    }

    [Fact]
    public async Task Clip_SetsParentFileId_ToSourceFileId()
    {
        var factory = CreateFactory();
        var typeId  = await SeedTypeAsync(factory);
        var fileId  = await SeedFileAsync(factory, CreateSilentWav(seconds: 2));
        var userId  = Guid.NewGuid();
        var ctrl    = Build(factory, userId);

        var result  = await ctrl.Clip(fileId,
            new ClipAudioRequest(0.0, 1.0, null, false, typeId), default);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var record  = Assert.IsType<UploadFileRecord>(created.Value);
        Assert.Equal(fileId, record.ParentFileId);
    }

    [Fact]
    public async Task Clip_SetsRegionStartAndEnd_FromRequest()
    {
        var factory = CreateFactory();
        var typeId  = await SeedTypeAsync(factory);
        var fileId  = await SeedFileAsync(factory, CreateSilentWav(seconds: 2));
        var userId  = Guid.NewGuid();
        var ctrl    = Build(factory, userId);

        var result  = await ctrl.Clip(fileId,
            new ClipAudioRequest(0.25, 0.75, null, false, typeId), default);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var record  = Assert.IsType<UploadFileRecord>(created.Value);
        Assert.Equal(0.25, record.RegionStart);
        Assert.Equal(0.75, record.RegionEnd);
    }

    [Fact]
    public async Task Clip_UsesProvidedLabel_AsDescription()
    {
        var factory = CreateFactory();
        var typeId  = await SeedTypeAsync(factory);
        var fileId  = await SeedFileAsync(factory, CreateSilentWav(seconds: 2));
        var userId  = Guid.NewGuid();
        var ctrl    = Build(factory, userId);

        var result  = await ctrl.Clip(fileId,
            new ClipAudioRequest(0.0, 1.0, "My Clip", false, typeId), default);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var record  = Assert.IsType<UploadFileRecord>(created.Value);
        Assert.Equal("My Clip", record.Description);
    }

    [Fact]
    public async Task Clip_SetsIsPublic_FromRequest()
    {
        var factory = CreateFactory();
        var typeId  = await SeedTypeAsync(factory);
        var fileId  = await SeedFileAsync(factory, CreateSilentWav(seconds: 2));
        var userId  = Guid.NewGuid();
        var ctrl    = Build(factory, userId);

        var result  = await ctrl.Clip(fileId,
            new ClipAudioRequest(0.0, 1.0, null, true, typeId), default);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var record  = Assert.IsType<UploadFileRecord>(created.Value);
        Assert.True(record.IsPublic);
    }

    [Fact]
    public async Task Clip_OutputContentType_IsAudioWav()
    {
        var factory = CreateFactory();
        var typeId  = await SeedTypeAsync(factory);
        var fileId  = await SeedFileAsync(factory, CreateSilentWav(seconds: 2));
        var userId  = Guid.NewGuid();
        var ctrl    = Build(factory, userId);

        var result  = await ctrl.Clip(fileId,
            new ClipAudioRequest(0.0, 1.0, null, false, typeId), default);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var record  = Assert.IsType<UploadFileRecord>(created.Value);
        Assert.Equal("audio/wav", record.ContentType);
    }

    [Fact]
    public async Task Clip_OutputFileSize_IsNonZero()
    {
        var factory = CreateFactory();
        var typeId  = await SeedTypeAsync(factory);
        var fileId  = await SeedFileAsync(factory, CreateSilentWav(seconds: 2));
        var userId  = Guid.NewGuid();
        var ctrl    = Build(factory, userId);

        var result  = await ctrl.Clip(fileId,
            new ClipAudioRequest(0.0, 1.0, null, false, typeId), default);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var record  = Assert.IsType<UploadFileRecord>(created.Value);
        Assert.True(record.FileSize > 0);
    }

    [Fact]
    public async Task Clip_PersistsEntity_InDatabase()
    {
        var factory = CreateFactory();
        var typeId  = await SeedTypeAsync(factory);
        var fileId  = await SeedFileAsync(factory, CreateSilentWav(seconds: 2));
        var userId  = Guid.NewGuid();
        var ctrl    = Build(factory, userId);

        var result  = await ctrl.Clip(fileId,
            new ClipAudioRequest(0.0, 1.0, null, false, typeId), default);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var record  = Assert.IsType<UploadFileRecord>(created.Value);

        await using var db = await factory.CreateDbContextAsync();
        var entity = await db.UploadFiles.FindAsync(record.Id);
        Assert.NotNull(entity);
        Assert.Equal(fileId, entity.ParentFileId);
        Assert.Equal(0.0,    entity.RegionStart);
        Assert.Equal(1.0,    entity.RegionEnd);
    }
}
