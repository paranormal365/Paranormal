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
using Ben.Data.WebApi.Services.Access;
using Ben.Data.WebApi.Services.Audio;

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
            new Mock<IAuditLogService>().Object, Ben.Web.Tests.TestMedia.Ingest());
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

    /// <summary>Like <see cref="Build"/>, but captures the bytes written via <c>IFileStorageService.WriteAsync</c> so correctness tests can decode the derived file.</summary>
    private static (UploadFileAudioEditController Ctrl, Func<byte[]?> GetWrittenBytes) BuildCapturing(
        IDbContextFactory<BenDataContext> factory, Guid userId)
    {
        byte[]? written = null;
        var storage = new Mock<Ben.Data.Common.Interfaces.IFileStorageService>();
        storage.Setup(s => s.UserFilePath(It.IsAny<Guid>(), It.IsAny<string>()))
               .Returns((Guid uid, string name) => $"{uid}/{name}");
        storage.Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<System.IO.Stream>(), It.IsAny<CancellationToken>()))
               .Callback<string, System.IO.Stream, CancellationToken>((_, stream, _) =>
               {
                   using var ms = new System.IO.MemoryStream();
                   stream.CopyTo(ms);
                   written = ms.ToArray();
               })
               .Returns(Task.CompletedTask);

        var ctrl = new UploadFileAudioEditController(factory, CreateMapper(), storage.Object, new Mock<IAuditLogService>().Object, Ben.Web.Tests.TestMedia.Ingest());
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim(ClaimTypes.NameIdentifier, userId.ToString())
                ], "Bearer"))
            }
        };
        return (ctrl, () => written);
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

    /// <summary>Builds a PCM WAV containing a pure sine tone, for pitch/speed frequency assertions.</summary>
    private static byte[] CreateSineWav(double frequencyHz, double seconds, int sampleRate = 22050)
    {
        int numSamples = (int)(sampleRate * seconds);
        int dataSize   = numSamples * 2;
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
        for (var i = 0; i < numSamples; i++)
        {
            var value = (short)(0.8 * short.MaxValue * Math.Sin(2 * Math.PI * frequencyHz * i / sampleRate));
            w.Write(value);
        }
        return ms.ToArray();
    }

    /// <summary>Parses a mono 16-bit PCM WAV (as produced by <c>AudioEditor.WriteWav</c>) back into samples.</summary>
    private static (short[] Samples, int SampleRate) ReadWavPcm16(byte[] wavBytes)
    {
        using var ms = new System.IO.MemoryStream(wavBytes);
        using var r  = new System.IO.BinaryReader(ms);
        r.ReadBytes(4);  // RIFF
        r.ReadInt32();   // file size
        r.ReadBytes(4);  // WAVE

        var sampleRate = 0;
        short[]? data = null;
        while (ms.Position < ms.Length)
        {
            var chunkId   = new string(r.ReadChars(4));
            var chunkSize = r.ReadInt32();
            if (chunkId == "fmt ")
            {
                r.ReadInt16();          // audio format
                r.ReadInt16();          // channels
                sampleRate = r.ReadInt32();
                r.ReadBytes(chunkSize - 8);
            }
            else if (chunkId == "data")
            {
                var raw = r.ReadBytes(chunkSize);
                data = new short[raw.Length / 2];
                Buffer.BlockCopy(raw, 0, data, 0, raw.Length);
            }
            else
            {
                r.ReadBytes(chunkSize);
            }
        }
        return (data ?? [], sampleRate);
    }

    /// <summary>Estimates dominant frequency of a near-pure tone via positive-going zero-crossing rate.</summary>
    private static double EstimateFrequencyHz(short[] samples, int sampleRate)
    {
        var crossings = 0;
        for (var i = 1; i < samples.Length; i++)
            if (samples[i - 1] < 0 && samples[i] >= 0) crossings++;
        var durationSeconds = samples.Length / (double)sampleRate;
        return crossings / durationSeconds;
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

    /// <summary>
    /// Seeds a private UploadFile and returns it with its owner. Callers must act as the owner
    /// (or an audience the file is shared with) — Edit requires
    /// <c>FileAudienceAccess.CanViewFileAsync</c> on the source.
    /// </summary>
    private static async Task<(Guid FileId, Guid OwnerId)> SeedFileAsync(
        IDbContextFactory<BenDataContext> factory,
        byte[]? fileData = null, string contentType = "audio/wav", bool isPublic = false)
    {
        var fileId  = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        db.UploadFiles.Add(new UploadFile
        {
            Id = fileId, UploadFileTypeId = Guid.NewGuid(), AppUserId = ownerId,
            FileName = "audio.wav", StoredFileName = "s.wav", ContentType = contentType,
            FileSize = fileData?.Length ?? 4, FileData = fileData ?? new byte[4],
            IsPublic = isPublic,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
        });
        await db.SaveChangesAsync();
        return (fileId, ownerId);
    }

    private static AudioEditRequest Request(
        AudioEditOperation op, Guid typeId,
        double? start = null, double? end = null, double? gainDb = null,
        double? fadeIn = null, double? fadeOut = null,
        double? speedRatio = null, double? pitchSemitones = null,
        string? label = null, bool isPublic = false)
        => new(op, start, end, gainDb, fadeIn, fadeOut, label, isPublic, typeId, speedRatio, pitchSemitones);

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
        var (fileId, ownerId) = await SeedFileAsync(factory, CreateSilentWav());
        var ctrl    = Build(factory, ownerId);

        var result  = await ctrl.Edit(fileId, Request(AudioEditOperation.Normalize, Guid.NewGuid()), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Edit_ReturnsBadRequest_ForUnsupportedContentType()
    {
        var factory = CreateFactory();
        var typeId  = await SeedTypeAsync(factory);
        var (fileId, ownerId) = await SeedFileAsync(factory, new byte[100], "audio/ogg");
        var ctrl    = Build(factory, ownerId);

        var result  = await ctrl.Edit(fileId, Request(AudioEditOperation.Reverse, typeId), default);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("WAV", bad.Value?.ToString());
    }

    // ── File-audience access ──────────────────────────────────────────────────

    [Fact]
    public async Task Edit_UnrelatedCaller_ReturnsForbid()
    {
        // Edit had no check on the source file at all: because the result is persisted as a
        // brand new file the caller owns, any authenticated user could launder someone else's
        // private audio into their own library by "normalizing" it. Same exfiltration path
        // UploadFileAudioClipController was already fixed for.
        var factory = CreateFactory();
        var typeId  = await SeedTypeAsync(factory);
        var (fileId, _) = await SeedFileAsync(factory, CreateSilentWav());
        var ctrl    = Build(factory, Guid.NewGuid());

        var result  = await ctrl.Edit(fileId, Request(AudioEditOperation.Normalize, typeId), default);

        Assert.IsType<ForbidResult>(result.Result);

        // and nothing was persisted for the caller
        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(1, await db.UploadFiles.CountAsync());   // only the seeded source
    }

    [Fact]
    public async Task Edit_PublicSourceFile_AllowsAnyAuthenticatedCaller()
    {
        // The guard must not over-reach: deriving from a public file stays allowed.
        var factory = CreateFactory();
        var typeId  = await SeedTypeAsync(factory);
        var fileId  = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFiles.Add(new UploadFile
            {
                Id = fileId, UploadFileTypeId = Guid.NewGuid(), AppUserId = Guid.NewGuid(),
                FileName = "public.wav", StoredFileName = "p.wav", ContentType = "audio/wav",
                FileSize = 100, FileData = CreateSilentWav(seconds: 1), IsPublic = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid(),
            });
            await db.SaveChangesAsync();
        }
        var ctrl = Build(factory, Guid.NewGuid());

        var result = await ctrl.Edit(fileId, Request(AudioEditOperation.Normalize, typeId), default);

        Assert.IsType<CreatedAtActionResult>(result.Result);
    }

    // ── Success — one per operation ─────────────────────────────────────────────

    [Theory]
    [InlineData(AudioEditOperation.Normalize)]
    [InlineData(AudioEditOperation.Reverse)]
    public async Task Edit_Returns201_ForWholeFileOperations(AudioEditOperation op)
    {
        var factory = CreateFactory();
        var typeId  = await SeedTypeAsync(factory);
        var (fileId, ownerId) = await SeedFileAsync(factory, CreateSilentWav(seconds: 1));
        var ctrl    = Build(factory, ownerId);

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
        var (fileId, ownerId) = await SeedFileAsync(factory, CreateSilentWav(seconds: 2));
        var ctrl    = Build(factory, ownerId);

        var result  = await ctrl.Edit(fileId, Request(AudioEditOperation.Cut, typeId, start: 0.5, end: 1.0), default);

        Assert.IsType<CreatedAtActionResult>(result.Result);
    }

    [Fact]
    public async Task Edit_Returns201_ForSilenceRegion()
    {
        var factory = CreateFactory();
        var typeId  = await SeedTypeAsync(factory);
        var (fileId, ownerId) = await SeedFileAsync(factory, CreateSilentWav(seconds: 2));
        var ctrl    = Build(factory, ownerId);

        var result  = await ctrl.Edit(fileId, Request(AudioEditOperation.Silence, typeId, start: 0.5, end: 1.0), default);

        Assert.IsType<CreatedAtActionResult>(result.Result);
    }

    [Fact]
    public async Task Edit_Returns201_ForGain()
    {
        var factory = CreateFactory();
        var typeId  = await SeedTypeAsync(factory);
        var (fileId, ownerId) = await SeedFileAsync(factory, CreateSilentWav(seconds: 1));
        var ctrl    = Build(factory, ownerId);

        var result  = await ctrl.Edit(fileId, Request(AudioEditOperation.Gain, typeId, gainDb: 6.0), default);

        Assert.IsType<CreatedAtActionResult>(result.Result);
    }

    [Fact]
    public async Task Edit_Returns201_ForFade()
    {
        var factory = CreateFactory();
        var typeId  = await SeedTypeAsync(factory);
        var (fileId, ownerId) = await SeedFileAsync(factory, CreateSilentWav(seconds: 2));
        var ctrl    = Build(factory, ownerId);

        var result  = await ctrl.Edit(fileId, Request(AudioEditOperation.Fade, typeId, fadeIn: 0.5, fadeOut: 0.5), default);

        Assert.IsType<CreatedAtActionResult>(result.Result);
    }

    // ── Speed / Pitch validation ─────────────────────────────────────────────

    [Fact]
    public async Task Edit_ReturnsBadRequest_ForSpeed_WhenRatioMissing()
    {
        var factory = CreateFactory();
        var ctrl    = Build(factory, Guid.NewGuid());

        var result  = await ctrl.Edit(Guid.NewGuid(), Request(AudioEditOperation.Speed, Guid.NewGuid()), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Edit_ReturnsBadRequest_ForSpeed_WhenRatioOutOfRange()
    {
        var factory = CreateFactory();
        var ctrl    = Build(factory, Guid.NewGuid());

        var result  = await ctrl.Edit(Guid.NewGuid(), Request(AudioEditOperation.Speed, Guid.NewGuid(), speedRatio: 10), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Edit_ReturnsBadRequest_ForPitch_WhenSemitonesMissing()
    {
        var factory = CreateFactory();
        var ctrl    = Build(factory, Guid.NewGuid());

        var result  = await ctrl.Edit(Guid.NewGuid(), Request(AudioEditOperation.Pitch, Guid.NewGuid()), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Edit_ReturnsBadRequest_ForPitch_WhenSemitonesOutOfRange()
    {
        var factory = CreateFactory();
        var ctrl    = Build(factory, Guid.NewGuid());

        var result  = await ctrl.Edit(Guid.NewGuid(), Request(AudioEditOperation.Pitch, Guid.NewGuid(), pitchSemitones: 30), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    // ── Speed / Pitch correctness (via a pure sine tone) ─────────────────────

    [Fact]
    public async Task Edit_Pitch_ShiftsFrequencyUpOneOctave()
    {
        var factory = CreateFactory();
        var typeId  = await SeedTypeAsync(factory);
        var (fileId, ownerId) = await SeedFileAsync(factory, CreateSineWav(440.0, seconds: 1.5));
        var (ctrl, getBytes) = BuildCapturing(factory, ownerId);

        var result  = await ctrl.Edit(fileId, Request(AudioEditOperation.Pitch, typeId, pitchSemitones: 12), default);

        Assert.IsType<CreatedAtActionResult>(result.Result);
        var (samples, sampleRate) = ReadWavPcm16(getBytes()!);
        var freq = EstimateFrequencyHz(samples, sampleRate);

        Assert.InRange(freq, 880.0 * 0.9, 880.0 * 1.1); // +12 semitones = octave up = 2x frequency
    }

    [Fact]
    public async Task Edit_Pitch_ShiftsFrequencyDownOneOctave()
    {
        var factory = CreateFactory();
        var typeId  = await SeedTypeAsync(factory);
        var (fileId, ownerId) = await SeedFileAsync(factory, CreateSineWav(440.0, seconds: 1.5));
        var (ctrl, getBytes) = BuildCapturing(factory, ownerId);

        var result  = await ctrl.Edit(fileId, Request(AudioEditOperation.Pitch, typeId, pitchSemitones: -12), default);

        Assert.IsType<CreatedAtActionResult>(result.Result);
        var (samples, sampleRate) = ReadWavPcm16(getBytes()!);
        var freq = EstimateFrequencyHz(samples, sampleRate);

        Assert.InRange(freq, 220.0 * 0.9, 220.0 * 1.1); // -12 semitones = octave down = half frequency
    }

    [Fact]
    public async Task Edit_Speed_HalvesDuration_AndPreservesPitch()
    {
        var factory = CreateFactory();
        var typeId  = await SeedTypeAsync(factory);
        var (fileId, ownerId) = await SeedFileAsync(factory, CreateSineWav(440.0, seconds: 2.0));
        var (ctrl, getBytes) = BuildCapturing(factory, ownerId);

        var result  = await ctrl.Edit(fileId, Request(AudioEditOperation.Speed, typeId, speedRatio: 2.0), default);

        Assert.IsType<CreatedAtActionResult>(result.Result);
        var (samples, sampleRate) = ReadWavPcm16(getBytes()!);

        var durationSeconds = samples.Length / (double)sampleRate;
        Assert.InRange(durationSeconds, 0.9, 1.1); // 2s source at 2x speed ≈ 1s

        var freq = EstimateFrequencyHz(samples, sampleRate);
        Assert.InRange(freq, 440.0 * 0.9, 440.0 * 1.1); // pitch preserved despite the speed change
    }

    // ── Bounds that were missing (2026-09-06 audio walk, findings 2, 13) ──────

    /// <summary>
    /// <c>SpeedRatio</c> was bounded above at 4 and not at all below.
    /// </summary>
    /// <remarks>
    /// A ratio of 0.001 asks for a thousand times the samples — the resampler allocates that array,
    /// and then a phase vocoder runs over every one of them to put the pitch back. On a recording
    /// of any length that is not slow, it is an out-of-memory kill that takes every other request
    /// with it.
    /// </remarks>
    [Fact]
    public async Task Edit_Speed_BelowTheFloor_IsRefused()
    {
        var factory = CreateFactory();
        var ctrl    = Build(factory, Guid.NewGuid());

        var result  = await ctrl.Edit(Guid.NewGuid(),
            Request(AudioEditOperation.Speed, Guid.NewGuid(), speedRatio: 0.001), default);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("0.25", bad.Value?.ToString());
    }

    /// <summary>
    /// NaN survives every comparison, so a range check written the obvious way lets it through.
    /// </summary>
    /// <remarks>
    /// It then multiplies every sample into NaN, which writes as zero — a file of pure silence,
    /// answered 201, indistinguishable from a successful edit until somebody plays it (finding 13).
    /// </remarks>
    [Fact]
    public async Task Edit_Gain_OfNaN_IsRefused()
    {
        var factory = CreateFactory();
        var typeId  = await SeedTypeAsync(factory);
        var (fileId, ownerId) = await SeedFileAsync(factory, CreateSilentWav());
        var ctrl    = Build(factory, ownerId);

        var result  = await ctrl.Edit(fileId,
            Request(AudioEditOperation.Gain, typeId, gainDb: double.NaN), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        await using var db = await factory.CreateDbContextAsync();
        Assert.False(await db.UploadFiles.AnyAsync(f => f.ParentFileId == fileId));
    }

    [Fact]
    public async Task Edit_Gain_BeyondTheRange_IsRefused()
    {
        var factory = CreateFactory();
        var ctrl    = Build(factory, Guid.NewGuid());

        var result  = await ctrl.Edit(Guid.NewGuid(),
            Request(AudioEditOperation.Gain, Guid.NewGuid(), gainDb: 500), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Edit_Fade_OfNaN_IsRefused()
    {
        var factory = CreateFactory();
        var typeId  = await SeedTypeAsync(factory);
        var (fileId, ownerId) = await SeedFileAsync(factory, CreateSilentWav());
        var ctrl    = Build(factory, ownerId);

        var result  = await ctrl.Edit(fileId,
            Request(AudioEditOperation.Fade, typeId, fadeIn: double.NaN, fadeOut: 1.0), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Edit_Fade_OfANegativeLength_IsRefused()
    {
        var factory = CreateFactory();
        var ctrl    = Build(factory, Guid.NewGuid());

        var result  = await ctrl.Edit(Guid.NewGuid(),
            Request(AudioEditOperation.Fade, Guid.NewGuid(), fadeIn: -3.0), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    // ── The source's visibility is a ceiling (finding 6) ──────────────────────

    /// <summary>
    /// Editing requires only that the caller can SEE the source. The result is a new file the
    /// caller owns, and its <c>IsPublic</c> came straight from the request — so "edit it, then
    /// publish the edit" published somebody else's private recording.
    /// </summary>
    [Fact]
    public async Task Edit_OfAPrivateRecording_CannotBeMadePublic()
    {
        var factory = CreateFactory();
        var typeId  = await SeedTypeAsync(factory);
        var (fileId, ownerId) = await SeedFileAsync(factory, CreateSilentWav(), isPublic: false);
        var ctrl    = Build(factory, ownerId);

        var result  = await ctrl.Edit(fileId,
            Request(AudioEditOperation.Normalize, typeId, isPublic: true), default);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("private", bad.Value?.ToString(), StringComparison.OrdinalIgnoreCase);

        await using var db = await factory.CreateDbContextAsync();
        Assert.False(await db.UploadFiles.AnyAsync(f => f.ParentFileId == fileId));
    }

    [Fact]
    public async Task Edit_OfAPublicRecording_MayStayPublic()
    {
        var factory = CreateFactory();
        var typeId  = await SeedTypeAsync(factory);
        var (fileId, ownerId) = await SeedFileAsync(factory, CreateSilentWav(), isPublic: true);
        var ctrl    = Build(factory, ownerId);

        var result  = await ctrl.Edit(fileId,
            Request(AudioEditOperation.Normalize, typeId, isPublic: true), default);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.True(Assert.IsType<UploadFileRecord>(created.Value).IsPublic);
    }

    // ── Nothing is written that cannot be saved (finding 7) ───────────────────

    /// <summary>
    /// The label becomes the derived file's name, and the name column holds 500 characters.
    /// </summary>
    /// <remarks>
    /// A longer one threw inside <c>SaveChanges</c> — after the WAV had already been written to
    /// storage — leaving bytes on disk that no row would ever point at, and a 500 for the person
    /// who typed a long name.
    /// </remarks>
    [Fact]
    public async Task Edit_WithAnOverLongLabel_WritesNothing()
    {
        var factory = CreateFactory();
        var typeId  = await SeedTypeAsync(factory);
        var (fileId, ownerId) = await SeedFileAsync(factory, CreateSilentWav());
        var (ctrl, getBytes)  = BuildCapturing(factory, ownerId);

        var result = await ctrl.Edit(fileId,
            Request(AudioEditOperation.Normalize, typeId, label: new string('x', 600)), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Null(getBytes());
    }

    // ── Corrupt audio answers the same way everywhere (finding 5) ─────────────

    /// <summary>
    /// Bytes that are not the audio they claim to be were a 400 on the EVP scan and a 500 on edit
    /// and clip: the same file, the same cause, and one of the two answers reads as "the site is
    /// broken".
    /// </summary>
    [Fact]
    public async Task Edit_OfCorruptAudio_IsARefusalAndNotACrash()
    {
        var factory = CreateFactory();
        var typeId  = await SeedTypeAsync(factory);
        var corrupt = "RIFF"u8.ToArray().Concat(new byte[64]).ToArray();
        var (fileId, ownerId) = await SeedFileAsync(factory, corrupt);
        var ctrl    = Build(factory, ownerId);

        var result  = await ctrl.Edit(fileId, Request(AudioEditOperation.Normalize, typeId), default);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("audio", bad.Value?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    // ── What the derived file says about itself (findings 11 and F) ───────────

    /// <summary>
    /// A Cut records the region it was about; every edited file used to show "0:00–0:00" on its
    /// Saved Clips card, next to clips that showed their real range (finding F).
    /// </summary>
    [Fact]
    public async Task Edit_Cut_RecordsTheRegionItWasAbout()
    {
        var factory = CreateFactory();
        var typeId  = await SeedTypeAsync(factory);
        var (fileId, ownerId) = await SeedFileAsync(factory, CreateSilentWav(seconds: 4));
        var ctrl    = Build(factory, ownerId);

        var result  = await ctrl.Edit(fileId,
            Request(AudioEditOperation.Cut, typeId, start: 1.0, end: 2.0), default);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var record  = Assert.IsType<UploadFileRecord>(created.Value);

        await using var db = await factory.CreateDbContextAsync();
        var derived = await db.UploadFiles.FirstAsync(f => f.Id == record.Id);
        Assert.Equal(1.0, derived.RegionStart);
        Assert.Equal(2.0, derived.RegionEnd);
    }

    /// <summary>Normalize is about the whole recording, so it claims no region.</summary>
    [Fact]
    public async Task Edit_Normalize_ClaimsNoRegion()
    {
        var factory = CreateFactory();
        var typeId  = await SeedTypeAsync(factory);
        var (fileId, ownerId) = await SeedFileAsync(factory, CreateSilentWav());
        var ctrl    = Build(factory, ownerId);

        var result  = await ctrl.Edit(fileId, Request(AudioEditOperation.Normalize, typeId), default);

        var record = Assert.IsType<UploadFileRecord>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);
        await using var db = await factory.CreateDbContextAsync();
        var derived = await db.UploadFiles.FirstAsync(f => f.Id == record.Id);
        Assert.Null(derived.RegionStart);
        Assert.Null(derived.RegionEnd);
    }

    /// <summary>
    /// Derived audio carried no duration at all: the metadata row is only created when the SOURCE
    /// has one to inherit, and no audio upload on any path gets one (finding 11). Measuring it off
    /// the bytes just produced is the part nobody can inherit.
    /// </summary>
    [Fact]
    public async Task Edit_RecordsHowLongTheResultIs()
    {
        var factory = CreateFactory();
        var typeId  = await SeedTypeAsync(factory);
        var (fileId, ownerId) = await SeedFileAsync(factory, CreateSilentWav(seconds: 3));
        var ctrl    = Build(factory, ownerId);

        var result  = await ctrl.Edit(fileId, Request(AudioEditOperation.Reverse, typeId), default);

        var record = Assert.IsType<UploadFileRecord>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);
        await using var db = await factory.CreateDbContextAsync();
        var metadata = await db.UploadFileMetadata.FirstOrDefaultAsync(m => m.UploadFileId == record.Id);

        Assert.NotNull(metadata);
        Assert.Equal(3.0, metadata!.DurationSeconds!.Value, 1);
        Assert.Equal(8000, metadata.SampleRateHz);
        Assert.Equal(1, metadata.Channels);
    }

    /// <summary>A region that begins after the recording ends is a mistake, not an edit.</summary>
    [Fact]
    public async Task Edit_Cut_StartingPastTheEnd_IsRefused()
    {
        var factory = CreateFactory();
        var typeId  = await SeedTypeAsync(factory);
        var (fileId, ownerId) = await SeedFileAsync(factory, CreateSilentWav(seconds: 2));
        var ctrl    = Build(factory, ownerId);

        var result  = await ctrl.Edit(fileId,
            Request(AudioEditOperation.Cut, typeId, start: 60.0, end: 61.0), default);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("only 2", bad.Value?.ToString());
    }
}
