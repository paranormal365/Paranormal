using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Http;
using NAudio.Wave;
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
        var ctrl = new UploadFileAudioClipController(factory, CreateMapper(),
            new Moq.Mock<Ben.Data.Common.Interfaces.IFileStorageService>().Object,
            new Moq.Mock<IAuditLogService>().Object, new Ben.Data.WebApi.Services.MediaIngestService(new Moq.Mock<Ben.Data.Common.Interfaces.IFileStorageService>().Object, new Ben.Data.WebApi.Services.FileMetadataExtractorService(), new Ben.Data.WebApi.Services.MediaSanitizationService(), Microsoft.Extensions.Logging.Abstractions.NullLogger<Ben.Data.WebApi.Services.MediaIngestService>.Instance));
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

    private static async Task<(Guid FileId, Guid OwnerId)> SeedFileAsync(
        IDbContextFactory<BenDataContext> factory,
        byte[]? fileData = null, string contentType = "audio/wav")
    {
        var fileId  = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        db.UploadFiles.Add(new UploadFile
        {
            Id = fileId, UploadFileTypeId = Guid.NewGuid(), AppUserId = ownerId,
            FileName = "audio.wav", StoredFileName = "s.wav", ContentType = contentType,
            FileSize = fileData?.Length ?? 4, FileData = fileData ?? new byte[4],
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
        });
        await db.SaveChangesAsync();
        return (fileId, ownerId);
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
        var (fileId, ownerId) = await SeedFileAsync(factory, CreateSilentWav());
        var ctrl    = Build(factory, ownerId);

        var result  = await ctrl.Clip(fileId,
            new ClipAudioRequest(0, 1, null, false, Guid.NewGuid()), default);  // random file type

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Clip_ReturnsBadRequest_ForUnsupportedContentType()
    {
        var factory  = CreateFactory();
        var typeId   = await SeedTypeAsync(factory);
        var (fileId, ownerId) = await SeedFileAsync(factory, new byte[100], "audio/ogg");
        var ctrl     = Build(factory, ownerId);

        var result   = await ctrl.Clip(fileId,
            new ClipAudioRequest(0, 0.5, null, false, typeId), default);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("WAV", bad.Value?.ToString());
    }

    [Fact]
    public async Task Clip_UnrelatedCaller_ReturnsForbid()
    {
        // The core of the fix: this used to let any authenticated user extract audio from
        // someone else's private file and persist it as a new file they own.
        var factory = CreateFactory();
        var typeId  = await SeedTypeAsync(factory);
        var (fileId, _) = await SeedFileAsync(factory, CreateSilentWav());
        var ctrl    = Build(factory, Guid.NewGuid());

        var result  = await ctrl.Clip(fileId,
            new ClipAudioRequest(0.0, 1.0, null, false, typeId), default);

        Assert.IsType<ForbidResult>(result.Result);
    }

    // ── Success — parent-file tracking ────────────────────────────────────────

    [Fact]
    public async Task Clip_Returns201_WhenWavIsClipped()
    {
        var factory = CreateFactory();
        var typeId  = await SeedTypeAsync(factory);
        var (fileId, ownerId) = await SeedFileAsync(factory, CreateSilentWav(seconds: 2));
        var ctrl    = Build(factory, ownerId);

        var result  = await ctrl.Clip(fileId,
            new ClipAudioRequest(0.0, 1.0, null, false, typeId), default);

        Assert.IsType<CreatedAtActionResult>(result.Result);
    }

    [Fact]
    public async Task Clip_SetsParentFileId_ToSourceFileId()
    {
        var factory = CreateFactory();
        var typeId  = await SeedTypeAsync(factory);
        var (fileId, ownerId) = await SeedFileAsync(factory, CreateSilentWav(seconds: 2));
        var ctrl    = Build(factory, ownerId);

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
        var (fileId, ownerId) = await SeedFileAsync(factory, CreateSilentWav(seconds: 2));
        var ctrl    = Build(factory, ownerId);

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
        var (fileId, ownerId) = await SeedFileAsync(factory, CreateSilentWav(seconds: 2));
        var ctrl    = Build(factory, ownerId);

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
        var (fileId, ownerId) = await SeedFileAsync(factory, CreateSilentWav(seconds: 2));
        var ctrl    = Build(factory, ownerId);

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
        var (fileId, ownerId) = await SeedFileAsync(factory, CreateSilentWav(seconds: 2));
        var ctrl    = Build(factory, ownerId);

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
        var (fileId, ownerId) = await SeedFileAsync(factory, CreateSilentWav(seconds: 2));
        var ctrl    = Build(factory, ownerId);

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
        var (fileId, ownerId) = await SeedFileAsync(factory, CreateSilentWav(seconds: 2));
        var ctrl    = Build(factory, ownerId);

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

    // ── ClipPreview (GET — no DB write) ───────────────────────────────────────

    [Fact]
    public async Task ClipPreview_ReturnsBadRequest_WhenEndBeforeStart()
    {
        var factory = CreateFactory();
        var ctrl    = Build(factory, Guid.NewGuid());

        var result  = await ctrl.ClipPreview(Guid.NewGuid(), start: 5, end: 2, default);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task ClipPreview_ReturnsNotFound_WhenFileDoesNotExist()
    {
        var factory = CreateFactory();
        var ctrl    = Build(factory, Guid.NewGuid());

        var result  = await ctrl.ClipPreview(Guid.NewGuid(), start: 0, end: 1, default);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task ClipPreview_UnrelatedCaller_ReturnsForbid()
    {
        // The core of the fix: this used to let any authenticated user preview-clip audio out
        // of someone else's private file.
        var factory = CreateFactory();
        var (fileId, _) = await SeedFileAsync(factory, CreateSilentWav());
        var ctrl    = Build(factory, Guid.NewGuid());

        var result  = await ctrl.ClipPreview(fileId, start: 0.0, end: 1.0, default);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task ClipPreview_ReturnsBadRequest_ForUnsupportedFormat()
    {
        var factory  = CreateFactory();
        var (fileId, ownerId) = await SeedFileAsync(factory, new byte[100], "audio/ogg");
        var ctrl     = Build(factory, ownerId);

        var result   = await ctrl.ClipPreview(fileId, start: 0, end: 0.5, default);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task ClipPreview_ReturnsWavBytes_WithoutPersistingToDatabase()
    {
        var factory = CreateFactory();
        var (fileId, ownerId) = await SeedFileAsync(factory, CreateSilentWav(seconds: 2));
        var ctrl    = Build(factory, ownerId);

        var result  = await ctrl.ClipPreview(fileId, start: 0.0, end: 1.0, default);

        // Should return a file result, not a created-at-action
        var fileResult = Assert.IsType<FileContentResult>(result);
        Assert.Equal("audio/wav", fileResult.ContentType);
        Assert.NotEmpty(fileResult.FileContents);

        // Verify no new UploadFile was created
        await using var db    = await factory.CreateDbContextAsync();
        var count             = await db.UploadFiles.CountAsync();
        Assert.Equal(1, count); // only the seeded source file
    }

    // ── Clipping from an EVP marker (phase E4) ────────────────────────────────

    /// <summary>A quiet tone, so normalization has something real to scale.</summary>
    private static byte[] CreateQuietToneWav(
        double amplitude = 0.05, int seconds = 2, int sampleRate = 8000)
    {
        var numSamples = sampleRate * seconds;
        using var ms = new System.IO.MemoryStream();
        using (var w = new System.IO.BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            var dataSize = numSamples * 2;
            w.Write("RIFF"u8); w.Write(36 + dataSize); w.Write("WAVE"u8);
            w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(sampleRate); w.Write(sampleRate * 2); w.Write((short)2); w.Write((short)16);
            w.Write("data"u8); w.Write(dataSize);
            for (var i = 0; i < numSamples; i++)
            {
                var v = Math.Sin(2 * Math.PI * 440 * i / sampleRate) * amplitude;
                w.Write((short)Math.Clamp(v * 32767, short.MinValue, short.MaxValue));
            }
        }
        return ms.ToArray();
    }

    private static double PeakOf(byte[] wav)
    {
        using var reader = new WaveFileReader(new System.IO.MemoryStream(wav));
        var provider = reader.ToSampleProvider();
        var buffer = new float[4096];
        var peak = 0f;
        int read;
        while ((read = provider.Read(buffer, 0, buffer.Length)) > 0)
            for (var i = 0; i < read; i++) peak = Math.Max(peak, Math.Abs(buffer[i]));
        return peak;
    }

    private static async Task<Guid> SeedMarkerAsync(
        IDbContextFactory<BenDataContext> factory, Guid fileId, Guid createdBy,
        double start = 0.5, double end = 1.5)
    {
        var markerId = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        db.AudioMarkers.Add(new AudioMarker
        {
            Id = markerId, UploadFileId = fileId,
            TimeSeconds = start, EndSeconds = end,
            Label = "Says my name", ConfidenceLevel = EvpConfidenceLevel.Probable,
            ReviewStatus = EvpReviewStatus.Confirmed, IsAutoDetected = true, DetectionScore = 84f,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = createdBy,
        });
        await db.SaveChangesAsync();
        return markerId;
    }

    [Fact]
    public async Task Clip_WithSourceMarker_LinksTheClipBackToTheMarker()
    {
        // Without this the marker and its clip are unrelated rows, and there's no way to get from a
        // finding to the audio that evidences it.
        var factory = CreateFactory();
        var typeId  = await SeedTypeAsync(factory);
        var (fileId, ownerId) = await SeedFileAsync(factory, CreateSilentWav());
        var markerId = await SeedMarkerAsync(factory, fileId, ownerId);

        var result = await Build(factory, ownerId).Clip(fileId,
            new ClipAudioRequest(0.5, 1.5, "EVP clip", false, typeId, Normalize: false, SourceMarkerId: markerId),
            default);

        var created = Assert.IsType<UploadFileRecord>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);

        await using var db = await factory.CreateDbContextAsync();
        var marker = await db.AudioMarkers.SingleAsync(m => m.Id == markerId);
        Assert.Equal(created.Id, marker.LinkedClipUploadFileId);
        Assert.Equal(EvpReviewStatus.Confirmed, marker.ReviewStatus);   // clipping isn't a review
    }

    [Fact]
    public async Task Clip_WithAMarkerFromAnotherFile_IsRejected()
    {
        // A link claiming the clip came from a marker on a different recording would misrepresent
        // where the evidence originated.
        var factory = CreateFactory();
        var typeId  = await SeedTypeAsync(factory);
        var (fileId, ownerId)  = await SeedFileAsync(factory, CreateSilentWav());
        var (otherId, otherOwner) = await SeedFileAsync(factory, CreateSilentWav());
        var foreignMarker = await SeedMarkerAsync(factory, otherId, otherOwner);

        var result = await Build(factory, ownerId).Clip(fileId,
            new ClipAudioRequest(0.5, 1.5, "EVP clip", false, typeId, SourceMarkerId: foreignMarker),
            default);

        Assert.IsType<BadRequestObjectResult>(result.Result);

        await using var db = await factory.CreateDbContextAsync();
        Assert.Null((await db.AudioMarkers.SingleAsync(m => m.Id == foreignMarker)).LinkedClipUploadFileId);
    }

    [Fact]
    public async Task Clip_WithoutASourceMarker_StillWorks()
    {
        // The plain region-to-clip path predates EVP markers and must keep working untouched.
        var factory = CreateFactory();
        var typeId  = await SeedTypeAsync(factory);
        var (fileId, ownerId) = await SeedFileAsync(factory, CreateSilentWav());

        var result = await Build(factory, ownerId).Clip(fileId,
            new ClipAudioRequest(0.2, 1.0, null, false, typeId), default);

        Assert.IsType<CreatedAtActionResult>(result.Result);
    }

    [Fact]
    public async Task Clip_WithNormalize_RaisesAQuietClipTowardFullScale()
    {
        // The reason normalize exists: an EVP is typically far quieter than the recording around
        // it, so an un-normalized clip is close to inaudible without headphones.
        var factory = CreateFactory();
        var typeId  = await SeedTypeAsync(factory);
        var quiet   = CreateQuietToneWav(amplitude: 0.05);
        Assert.InRange(PeakOf(quiet), 0.04, 0.06);

        var (fileId, ownerId) = await SeedFileAsync(factory, quiet);

        byte[]? written = null;
        var storage = new Mock<Ben.Data.Common.Interfaces.IFileStorageService>();
        storage.Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
               .Returns<string, Stream, CancellationToken>((_, stream, _) =>
               {
                   using var ms = new MemoryStream();
                   stream.CopyTo(ms);
                   written = ms.ToArray();
                   return Task.CompletedTask;
               });

        var ctrl = new UploadFileAudioClipController(factory, CreateMapper(), storage.Object,
            new Mock<IAuditLogService>().Object, new Ben.Data.WebApi.Services.MediaIngestService(new Moq.Mock<Ben.Data.Common.Interfaces.IFileStorageService>().Object, new Ben.Data.WebApi.Services.FileMetadataExtractorService(), new Ben.Data.WebApi.Services.MediaSanitizationService(), Microsoft.Extensions.Logging.Abstractions.NullLogger<Ben.Data.WebApi.Services.MediaIngestService>.Instance));
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim(ClaimTypes.NameIdentifier, ownerId.ToString())
                ], "Bearer"))
            }
        };

        var result = await ctrl.Clip(fileId,
            new ClipAudioRequest(0.2, 1.5, "loud", false, typeId, Normalize: true), default);

        Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.NotNull(written);
        Assert.InRange(PeakOf(written!), 0.85, 0.9);   // −1 dBFS target
    }

    [Fact]
    public async Task Clip_WithNormalize_LeavesSilenceAlone()
    {
        // Silence has no peak to scale against; scaling it would just amplify nothing into noise.
        var factory = CreateFactory();
        var typeId  = await SeedTypeAsync(factory);
        var (fileId, ownerId) = await SeedFileAsync(factory, CreateSilentWav());

        byte[]? written = null;
        var storage = new Mock<Ben.Data.Common.Interfaces.IFileStorageService>();
        storage.Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
               .Returns<string, Stream, CancellationToken>((_, stream, _) =>
               {
                   using var ms = new MemoryStream();
                   stream.CopyTo(ms);
                   written = ms.ToArray();
                   return Task.CompletedTask;
               });

        var ctrl = new UploadFileAudioClipController(factory, CreateMapper(), storage.Object,
            new Mock<IAuditLogService>().Object, new Ben.Data.WebApi.Services.MediaIngestService(new Moq.Mock<Ben.Data.Common.Interfaces.IFileStorageService>().Object, new Ben.Data.WebApi.Services.FileMetadataExtractorService(), new Ben.Data.WebApi.Services.MediaSanitizationService(), Microsoft.Extensions.Logging.Abstractions.NullLogger<Ben.Data.WebApi.Services.MediaIngestService>.Instance));
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim(ClaimTypes.NameIdentifier, ownerId.ToString())
                ], "Bearer"))
            }
        };

        var result = await ctrl.Clip(fileId,
            new ClipAudioRequest(0.2, 1.0, "silent", false, typeId, Normalize: true), default);

        Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.NotNull(written);
        Assert.Equal(0.0, PeakOf(written!), precision: 3);
    }
}
