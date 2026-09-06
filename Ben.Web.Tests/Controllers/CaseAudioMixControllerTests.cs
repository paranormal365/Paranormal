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

/// <summary>Tests for <see cref="CaseAudioMixController"/> — the Phase E mixer export endpoint.</summary>
public class CaseAudioMixControllerTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var options = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PooledDbContextFactory<BenDataContext>(options);
    }

    private static byte[] CreateSineWav(double frequencyHz, double seconds, int sampleRate = 8000)
    {
        var numSamples = (int)(sampleRate * seconds);
        var dataSize = numSamples * 2;
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
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
            w.Write((short)(0.5 * short.MaxValue * Math.Sin(2 * Math.PI * frequencyHz * i / sampleRate)));
        return ms.ToArray();
    }

    private static CaseAudioMixController BuildController(
        IDbContextFactory<BenDataContext> factory, Guid userId, Dictionary<string, byte[]> store, Action<byte[]>? onExportWritten = null,
        bool isSuperAdmin = false)
    {
        var storage = new Mock<IFileStorageService>();
        storage.Setup(s => s.CaseFilePath(It.IsAny<Guid>(), It.IsAny<string>())).Returns("export/mix.wav");
        storage.Setup(s => s.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .Returns<string, CancellationToken>((path, _) => Task.FromResult<Stream>(new MemoryStream(store[path])));
        storage.Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
               .Returns<string, Stream, CancellationToken>((_, stream, _) =>
               {
                   using var ms = new MemoryStream();
                   stream.CopyTo(ms);
                   onExportWritten?.Invoke(ms.ToArray());
                   return Task.CompletedTask;
               });

        var ctrl = new CaseAudioMixController(factory, storage.Object, Ben.Web.Tests.TestMedia.Ingest(), new Ben.Service.RepositoryService.Services.OrganizationSecurityService(factory));
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    isSuperAdmin
                        ? [new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                           new Claim(ClaimTypes.Role, Ben.Data.Common.Constants.RoleNames.SuperAdmin)]
                        : [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
                    "Bearer", ClaimTypes.NameIdentifier, ClaimTypes.Role))
            }
        };
        return ctrl;
    }

    private static async Task<(IDbContextFactory<BenDataContext> Factory, Guid OrgId, Guid CaseId, Guid UserId)> SeedAsync()
    {
        var factory = CreateFactory();
        var orgId = Guid.NewGuid();
        var caseId = Guid.NewGuid();
        var userId = Guid.NewGuid();

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
        await TestSeeds.BridgeAsync(factory, orgId, TestSeeds.CaseWork);
        return (factory, orgId, caseId, userId);
    }

    private static async Task<CaseFile> SeedCaseFileAsync(
        IDbContextFactory<BenDataContext> factory, Guid caseId, Guid userId,
        Dictionary<string, byte[]> store, string storagePath, byte[] bytes, string contentType = "audio/wav")
    {
        store[storagePath] = bytes;
        await using var db = await factory.CreateDbContextAsync();
        var uploadFile = new UploadFile
        {
            Id = Guid.NewGuid(), UploadFileTypeId = Guid.NewGuid(), AppUserId = userId,
            FileName = "clip.wav", StoredFileName = "clip.wav", ContentType = contentType,
            FileSize = bytes.Length, StoragePath = storagePath,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        };
        db.UploadFiles.Add(uploadFile);
        var caseFile = new CaseFile
        {
            Id = Guid.NewGuid(), CaseId = caseId, UploadFileId = uploadFile.Id,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        };
        db.CaseFiles.Add(caseFile);
        await db.SaveChangesAsync();
        caseFile.UploadFile = uploadFile;
        return caseFile;
    }

    private static ExportAudioMixRequest RequestFor(params (Guid CaseFileId, bool Muted, bool Solo)[] tracks)
        => new(tracks.Select(t => new MixTrackExportInput(t.CaseFileId, 0, 0, 0, t.Muted, t.Solo)).ToList());

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Export_NoTracks_ReturnsBadRequest()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var ctrl = BuildController(factory, userId, []);

        var result = await ctrl.Export(orgId, caseId, new ExportAudioMixRequest([]), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Export_SuperAdminNonMember_IsNotForbidden()
    {
        // 2026-08-22: the SuperAdmin could open a case's audio mixer but every fetch under it
        // 403'd — IsOrgMember lacked the bypass the case endpoint itself already had.
        var (factory, orgId, caseId, _) = await SeedAsync();
        var store = new Dictionary<string, byte[]>();
        var caseFile = await SeedCaseFileAsync(factory, caseId, Guid.NewGuid(), store, "a.wav", CreateSineWav(440, 1));
        var ctrl = BuildController(factory, Guid.NewGuid(), store, isSuperAdmin: true);

        var result = await ctrl.Export(orgId, caseId, RequestFor((caseFile.Id, false, false)), default);

        Assert.IsNotType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task Export_NonMember_ReturnsForbid()
    {
        var (factory, orgId, caseId, _) = await SeedAsync();
        var store = new Dictionary<string, byte[]>();
        var caseFile = await SeedCaseFileAsync(factory, caseId, Guid.NewGuid(), store, "a.wav", CreateSineWav(440, 1));
        var ctrl = BuildController(factory, Guid.NewGuid(), store);

        var result = await ctrl.Export(orgId, caseId, RequestFor((caseFile.Id, false, false)), default);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task Export_CaseNotFound_ReturnsNotFound()
    {
        var (factory, orgId, _, userId) = await SeedAsync();
        var ctrl = BuildController(factory, userId, []);

        var result = await ctrl.Export(orgId, Guid.NewGuid(), RequestFor((Guid.NewGuid(), false, false)), default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Export_UnknownCaseFileId_ReturnsBadRequest()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var ctrl = BuildController(factory, userId, []);

        var result = await ctrl.Export(orgId, caseId, RequestFor((Guid.NewGuid(), false, false)), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Export_NonAudioFile_ReturnsBadRequest()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var store = new Dictionary<string, byte[]>();
        var caseFile = await SeedCaseFileAsync(factory, caseId, userId, store, "a.jpg", [1, 2, 3], "image/jpeg");
        var ctrl = BuildController(factory, userId, store);

        var result = await ctrl.Export(orgId, caseId, RequestFor((caseFile.Id, false, false)), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Export_AllTracksMuted_ReturnsBadRequest()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var store = new Dictionary<string, byte[]>();
        var caseFile = await SeedCaseFileAsync(factory, caseId, userId, store, "a.wav", CreateSineWav(440, 1));
        var ctrl = BuildController(factory, userId, store);

        var result = await ctrl.Export(orgId, caseId, RequestFor((caseFile.Id, true, false)), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Export_SoloedTrack_ExcludesNonSoloedTracks()
    {
        // This asserted only that the request came back 200, which it would have done with both
        // tracks in the mix — the one thing solo is for was not checked at all (2026-09-06 audio
        // audit). Two clearly different tones, and the un-soloed one must not be in the result.
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var store = new Dictionary<string, byte[]>();
        var soloed = await SeedCaseFileAsync(factory, caseId, userId, store, "a.wav", CreateSineWav(300, 1));
        var other  = await SeedCaseFileAsync(factory, caseId, userId, store, "b.wav", CreateSineWav(1200, 1));

        byte[]? exported = null;
        var ctrl = BuildController(factory, userId, store, bytes => exported = bytes);

        var result = await ctrl.Export(orgId, caseId,
            RequestFor((soloed.Id, false, true), (other.Id, false, false)), default);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.NotNull(exported);

        // 300 Hz alone crosses zero 300 times a second. With the 1.2 kHz track mixed in as well the
        // count rises to about 600, which is what this used to let through unnoticed.
        Assert.InRange(DominantFrequencyHz(exported!), 260, 360);
    }

    /// <summary>
    /// Estimates the dominant frequency of the left channel of a stereo 16-bit WAV, by counting
    /// upward zero crossings.
    /// </summary>
    private static double DominantFrequencyHz(byte[] wavBytes)
    {
        using var ms = new MemoryStream(wavBytes);
        using var r  = new BinaryReader(ms);
        r.ReadBytes(4); r.ReadInt32(); r.ReadBytes(4);

        var sampleRate = 0;
        short[] interleaved = [];
        while (ms.Position < ms.Length)
        {
            var chunkId   = new string(r.ReadChars(4));
            var chunkSize = r.ReadInt32();
            if (chunkId == "fmt ")
            {
                r.ReadInt16(); r.ReadInt16();
                sampleRate = r.ReadInt32();
                r.ReadBytes(chunkSize - 8);
            }
            else if (chunkId == "data")
            {
                var raw = r.ReadBytes(chunkSize);
                interleaved = new short[raw.Length / 2];
                Buffer.BlockCopy(raw, 0, interleaved, 0, raw.Length);
            }
            else r.ReadBytes(chunkSize);
        }

        var left = new short[interleaved.Length / 2];
        for (var i = 0; i < left.Length; i++) left[i] = interleaved[i * 2];

        var crossings = 0;
        for (var i = 1; i < left.Length; i++)
            if (left[i - 1] < 0 && left[i] >= 0) crossings++;

        return left.Length == 0 ? 0 : crossings / (left.Length / (double)sampleRate);
    }

    [Fact]
    public async Task Export_Success_CreatesNewUploadFileAndCaseFile()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var store = new Dictionary<string, byte[]>();
        var caseFile = await SeedCaseFileAsync(factory, caseId, userId, store, "a.wav", CreateSineWav(440, 1));
        byte[]? exported = null;
        var ctrl = BuildController(factory, userId, store, bytes => exported = bytes);

        var result = await ctrl.Export(orgId, caseId, RequestFor((caseFile.Id, false, false)), default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var record = Assert.IsType<CaseFileRecord>(ok.Value);
        Assert.Equal(caseId, record.CaseId);
        Assert.Equal("audio/wav", record.ContentType);
        Assert.NotNull(exported);
        Assert.True(exported!.Length > 0);

        await using var db = await factory.CreateDbContextAsync();
        Assert.True(await db.UploadFiles.AnyAsync(f => f.Id == record.UploadFileId));
        Assert.True(await db.CaseFiles.AnyAsync(f => f.Id == record.Id && f.CaseId == caseId));
    }

    // ── Bounds and fallbacks the mixer was missing (findings 3, 4, 14, 15) ────

    /// <summary>
    /// Offsets were unbounded, and the mix buffer is sized from the largest of them.
    /// </summary>
    /// <remarks>
    /// One track at ten million seconds is 44.1 kHz × 10,000,000 × 4 bytes of float array, twice,
    /// before a single sample is mixed. Larger still and the frame count overflows an <c>int</c>
    /// and the allocation is negative. Both arrive as a 500 (2026-09-06 audio walk, finding 3).
    /// </remarks>
    [Fact]
    public async Task Export_WithAnOffsetPastTheCeiling_IsRefused()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var store = new Dictionary<string, byte[]>();
        var caseFile = await SeedCaseFileAsync(factory, caseId, userId, store, "a.wav", CreateSineWav(440, 1));
        var ctrl = BuildController(factory, userId, store);

        var request = new ExportAudioMixRequest(
            [new MixTrackExportInput(caseFile.Id, 10_000_000, 0, 0, false, false)]);

        var result = await ctrl.Export(orgId, caseId, request, default);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("3600", bad.Value?.ToString());
    }

    [Fact]
    public async Task Export_WithANaNGain_IsRefused()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var store = new Dictionary<string, byte[]>();
        var caseFile = await SeedCaseFileAsync(factory, caseId, userId, store, "a.wav", CreateSineWav(440, 1));
        var ctrl = BuildController(factory, userId, store);

        var request = new ExportAudioMixRequest(
            [new MixTrackExportInput(caseFile.Id, 0, double.NaN, 0, false, false)]);

        Assert.IsType<BadRequestObjectResult>((await ctrl.Export(orgId, caseId, request, default)).Result);
    }

    /// <summary>The mixer offers eight lanes; a ninth was accepted and simply stacked.</summary>
    [Fact]
    public async Task Export_WithMoreTracksThanTheMixerHolds_IsRefused()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var store = new Dictionary<string, byte[]>();
        var tracks = new List<MixTrackExportInput>();
        for (var i = 0; i < 9; i++)
        {
            var file = await SeedCaseFileAsync(factory, caseId, userId, store, $"t{i}.wav", CreateSineWav(440, 1));
            tracks.Add(new MixTrackExportInput(file.Id, 0, 0, 0, false, false));
        }
        var ctrl = BuildController(factory, userId, store);

        var result = await ctrl.Export(orgId, caseId, new ExportAudioMixRequest(tracks), default);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("8", bad.Value?.ToString());
    }

    /// <summary>
    /// Legacy rows keep their bytes in the database and have no storage path at all. The mixer
    /// dereferenced <c>StoragePath!</c>, so one of them reaching the mixer was a 500 — while the
    /// edit and clip endpoints had always had the fallback (finding 4).
    /// </summary>
    [Fact]
    public async Task Export_MixesALegacyRowThatHasItsBytesInTheDatabase()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var store = new Dictionary<string, byte[]>();

        Guid caseFileId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var uploadFile = new UploadFile
            {
                Id = Guid.NewGuid(), UploadFileTypeId = Guid.NewGuid(), AppUserId = userId,
                FileName = "legacy.wav", StoredFileName = "legacy.wav", ContentType = "audio/wav",
                FileData = CreateSineWav(440, 1), StoragePath = null,
                FileSize = 1, DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
            };
            uploadFile.FileSize = uploadFile.FileData!.Length;
            db.UploadFiles.Add(uploadFile);
            var caseFile = new CaseFile
            {
                Id = Guid.NewGuid(), CaseId = caseId, UploadFileId = uploadFile.Id,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
            };
            db.CaseFiles.Add(caseFile);
            await db.SaveChangesAsync();
            caseFileId = caseFile.Id;
        }

        var ctrl = BuildController(factory, userId, store);

        var result = await ctrl.Export(orgId, caseId, RequestFor((caseFileId, false, false)), default);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    /// <summary>
    /// The mix row carries an <c>AppUserId</c>, so an unknown claim failed as a foreign-key
    /// violation — after the WAV had been rendered and written to storage (finding 14).
    /// </summary>
    [Fact]
    public async Task Export_WithNoUserClaim_WritesNothing()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var store = new Dictionary<string, byte[]>();
        var caseFile = await SeedCaseFileAsync(factory, caseId, userId, store, "a.wav", CreateSineWav(440, 1));

        byte[]? written = null;
        var ctrl = BuildController(factory, Guid.Empty, store, bytes => written = bytes);

        var result = await ctrl.Export(orgId, caseId, RequestFor((caseFile.Id, false, false)), default);

        Assert.IsType<UnauthorizedResult>(result.Result);
        Assert.Null(written);
    }

    /// <summary>
    /// Every other derived audio file records where it came from. A mix recorded none, so a case
    /// file plainly made of other case files looked like an original upload (finding 15).
    /// </summary>
    [Fact]
    public async Task Export_RecordsWhichRecordingTheMixCameFrom()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var store = new Dictionary<string, byte[]>();
        var first  = await SeedCaseFileAsync(factory, caseId, userId, store, "a.wav", CreateSineWav(440, 1));
        var second = await SeedCaseFileAsync(factory, caseId, userId, store, "b.wav", CreateSineWav(880, 1));
        var ctrl = BuildController(factory, userId, store);

        var result = await ctrl.Export(orgId, caseId,
            RequestFor((first.Id, false, false), (second.Id, false, false)), default);

        var record = Assert.IsType<CaseFileRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);
        await using var db = await factory.CreateDbContextAsync();
        var mixFile = await db.UploadFiles.FirstAsync(f => f.Id == record.UploadFileId);

        Assert.Equal(first.UploadFileId, mixFile.ParentFileId);
    }

    /// <summary>
    /// The mixer draws each clip's width from its duration, and a mix had none — so a mix dropped
    /// back into the mixer was drawn at whatever the fallback width is (finding 11).
    /// </summary>
    [Fact]
    public async Task Export_RecordsHowLongTheMixIs()
    {
        var (factory, orgId, caseId, userId) = await SeedAsync();
        var store = new Dictionary<string, byte[]>();
        var caseFile = await SeedCaseFileAsync(factory, caseId, userId, store, "a.wav", CreateSineWav(440, 2));
        var ctrl = BuildController(factory, userId, store);

        var result = await ctrl.Export(orgId, caseId, RequestFor((caseFile.Id, false, false)), default);

        var record = Assert.IsType<CaseFileRecord>(Assert.IsType<OkObjectResult>(result.Result).Value);
        await using var db = await factory.CreateDbContextAsync();
        var metadata = await db.UploadFileMetadata.FirstOrDefaultAsync(m => m.UploadFileId == record.UploadFileId);

        Assert.NotNull(metadata);
        Assert.Equal(2.0, metadata!.DurationSeconds!.Value, 1);
        Assert.Equal(2, metadata.Channels);
    }
}
