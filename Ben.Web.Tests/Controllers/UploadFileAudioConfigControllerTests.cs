using AutoMapper;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Service.Models.Entities;
using Ben.Web.Website.Library.Manage.Audio;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

public class UploadFileAudioConfigControllerTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static BenDataContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new BenDataContext(opts);
    }

    /// <summary>
    /// Creates a mapper mock that projects UploadFileAudioConfig → UploadFileAudioConfigRecord
    /// by copying the fields used in assertions.
    /// </summary>
    private static IMapper CreateMapper()
    {
        var m = new Mock<IMapper>();
        m.Setup(x => x.Map<UploadFileAudioConfigRecord>(It.IsAny<object>()))
         .Returns<object>(o =>
         {
             if (o is not UploadFileAudioConfig e) return new UploadFileAudioConfigRecord();
             return new UploadFileAudioConfigRecord
             {
                 Id                    = e.Id,
                 UploadFileId          = e.UploadFileId,
                 WaveColor             = e.WaveColor,
                 ProgressColor         = e.ProgressColor,
                 CursorColor           = e.CursorColor,
                 Height                = e.Height,
                 EnableHover           = e.EnableHover,
                 EnableTimeline        = e.EnableTimeline,
                 EnableZoom            = e.EnableZoom,
                 EnableMinimap         = e.EnableMinimap,
                 EnableSpectrogram     = e.EnableSpectrogram,
                 EnableSpectrogramWindowed = e.EnableSpectrogramWindowed,
                 EnableEnvelope        = e.EnableEnvelope,
                 EnableRegions         = e.EnableRegions,
                 InitialHeight         = e.InitialHeight,
                 MinHeight             = e.MinHeight,
                 MaxHeight             = e.MaxHeight,
                 ShowControls          = e.ShowControls,
                 MinZoom               = e.MinZoom,
                 MaxZoom               = e.MaxZoom,
                 DateCreated           = e.DateCreated,
                 CreatedByAppUserId    = e.CreatedByAppUserId,
             };
         });
        return m.Object;
    }

    private static UploadFileAudioConfigController Build(
        BenDataContext db,
        ClaimsPrincipal? user = null)
    {
        var ctrl = new UploadFileAudioConfigController(db, CreateMapper(), new Mock<IAuditLogService>().Object);
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user ?? Anonymous() }
        };
        return ctrl;
    }

    private static ClaimsPrincipal Anonymous() =>
        new(new ClaimsIdentity());

    private static ClaimsPrincipal AuthUser(Guid userId) =>
        new(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, userId.ToString())
        ], "Bearer"));

    private static async Task<(Guid FileId, Guid OwnerId)> SeedFileAsync(BenDataContext db)
    {
        var fileId  = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        db.UploadFiles.Add(new UploadFile
        {
            Id              = fileId,
            UploadFileTypeId = Guid.NewGuid(),
            AppUserId       = ownerId,
            FileName        = "audio.mp3",
            StoredFileName  = "stored.mp3",
            ContentType     = "audio/mpeg",
            FileSize        = 1024,
            FileData        = new byte[4],
            DateCreated     = DateTime.UtcNow,
            CreatedByAppUserId = ownerId,
        });
        await db.SaveChangesAsync();
        return (fileId, ownerId);
    }

    /// <summary>A file anybody may view, so that "can view" and "can manage" pull apart.</summary>
    private static async Task<(Guid FileId, Guid OwnerId)> SeedPublicFileAsync(BenDataContext db)
    {
        var (fileId, ownerId) = await SeedFileAsync(db);
        var file = await db.UploadFiles.FirstAsync(f => f.Id == fileId);
        file.IsPublic = true;
        await db.SaveChangesAsync();
        return (fileId, ownerId);
    }

    private static async Task<Guid> SeedConfigAsync(BenDataContext db, Guid fileId, Guid userId)
    {
        var id = Guid.NewGuid();
        db.UploadFileAudioConfigs.Add(new UploadFileAudioConfig
        {
            Id              = id,
            UploadFileId    = fileId,
            WaveColor       = "#FF6358",
            ProgressColor   = "#D9534F",
            EnableHover     = true,
            EnableTimeline  = false,
            InitialHeight   = "250px",
            MinHeight       = "80px",
            MaxHeight       = "800px",
            ShowControls    = true,
            MinZoom         = 10,
            MaxZoom         = 1000,
            DateCreated     = DateTime.UtcNow,
            CreatedByAppUserId = userId,
        });
        await db.SaveChangesAsync();
        return id;
    }

    // ── GET ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_ReturnsNull_WhenFileExistsButNoConfigSaved()
    {
        await using var db    = CreateDb();
        var (fileId, ownerId) = await SeedFileAsync(db);
        var ctrl              = Build(db, AuthUser(ownerId));

        var result            = await ctrl.Get(fileId);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Null(ok.Value);
    }

    /// <summary>
    /// Reading had no per-file check at all — any signed-in caller could read the settings saved
    /// against any recording (2026-09-06 audio walk, finding 9).
    /// </summary>
    [Fact]
    public async Task Get_UnrelatedCaller_ReturnsForbid()
    {
        await using var db    = CreateDb();
        var (fileId, ownerId) = await SeedFileAsync(db);
        await SeedConfigAsync(db, fileId, ownerId);
        var ctrl              = Build(db, AuthUser(Guid.NewGuid()));

        var result            = await ctrl.Get(fileId);

        Assert.IsType<ForbidResult>(result.Result);
    }

    /// <summary>
    /// Seeing a recording is not owning it.
    /// </summary>
    /// <remarks>
    /// <para>PUT and DELETE asked <c>CanViewFileAsync</c>, so everyone the recording reached could
    /// overwrite or delete the owner's saved view of it — zoom, colours, spectrogram, the whole
    /// listening chain — and the owner would simply find it changed with no sign of who did it
    /// (finding 9).</para>
    ///
    /// <para>A PUBLIC file is what makes this visible: the unrelated-caller tests already here pass
    /// either way, because a stranger cannot view a private file at all. Everyone can view this
    /// one, and only its owner may change it.</para>
    /// </remarks>
    [Fact]
    public async Task Upsert_SomeoneWhoCanOnlyViewTheFile_ReturnsForbid()
    {
        await using var db    = CreateDb();
        var (fileId, ownerId) = await SeedPublicFileAsync(db);
        var ctrl              = Build(db, AuthUser(Guid.NewGuid()));

        var result = await ctrl.Upsert(fileId, new UpsertAudioConfigRequest { WaveColor = "#000000" });

        Assert.IsType<ForbidResult>(result.Result);
        Assert.Empty(db.UploadFileAudioConfigs);
        Assert.NotEqual(Guid.Empty, ownerId);
    }

    [Fact]
    public async Task Delete_SomeoneWhoCanOnlyViewTheFile_ReturnsForbid()
    {
        await using var db    = CreateDb();
        var (fileId, ownerId) = await SeedPublicFileAsync(db);
        await SeedConfigAsync(db, fileId, ownerId);
        var ctrl              = Build(db, AuthUser(Guid.NewGuid()));

        var result = await ctrl.Delete(fileId);

        Assert.IsType<ForbidResult>(result);
        Assert.Single(db.UploadFileAudioConfigs);
    }

    [Fact]
    public async Task Get_ReturnsRecord_WhenConfigExists()
    {
        await using var db  = CreateDb();
        var (fileId, ownerId) = await SeedFileAsync(db);
        await SeedConfigAsync(db, fileId, ownerId);
        var ctrl            = Build(db, AuthUser(ownerId));

        var result          = await ctrl.Get(fileId);

        var ok      = Assert.IsType<OkObjectResult>(result.Result);
        var record  = Assert.IsType<UploadFileAudioConfigRecord>(ok.Value);
        Assert.Equal(fileId, record.UploadFileId);
        Assert.Equal("#FF6358", record.WaveColor);
        Assert.True(record.EnableHover);
        Assert.False(record.EnableTimeline);
    }

    [Fact]
    public async Task Get_ReturnsNotFound_WhenFileDoesNotExist()
    {
        await using var db  = CreateDb();
        var ctrl            = Build(db, AuthUser(Guid.NewGuid()));

        var result          = await ctrl.Get(Guid.NewGuid());

        Assert.IsType<NotFoundResult>(result.Result);
    }

    // ── PUT (upsert) ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Upsert_Creates_WhenNoConfigExists()
    {
        await using var db  = CreateDb();
        var (fileId, ownerId) = await SeedFileAsync(db);
        var ctrl            = Build(db, AuthUser(ownerId));
        var req             = new UpsertAudioConfigRequest
        {
            WaveColor       = "#3B82F6",
            EnableHover     = true,
            EnableTimeline  = true,
            InitialHeight   = "220px",
            MinHeight       = "80px",
            MaxHeight       = "800px",
            ShowControls    = true,
            MinZoom         = 10,
            MaxZoom         = 500,
        };

        var result          = await ctrl.Upsert(fileId, req);

        var ok      = Assert.IsType<OkObjectResult>(result.Result);
        var record  = Assert.IsType<UploadFileAudioConfigRecord>(ok.Value);
        Assert.Equal(fileId, record.UploadFileId);
        Assert.Equal("#3B82F6", record.WaveColor);
        Assert.True(record.EnableHover);
        Assert.Equal("220px", record.InitialHeight);
        Assert.Equal(500, record.MaxZoom);
        Assert.Equal(1, await db.UploadFileAudioConfigs.CountAsync());
    }

    [Fact]
    public async Task Upsert_Updates_WhenConfigAlreadyExists()
    {
        await using var db  = CreateDb();
        var (fileId, ownerId) = await SeedFileAsync(db);
        await SeedConfigAsync(db, fileId, ownerId);
        var ctrl            = Build(db, AuthUser(ownerId));
        var req             = new UpsertAudioConfigRequest
        {
            WaveColor       = "#00FF00",
            EnableHover     = false,
            EnableZoom      = true,
            InitialHeight   = "300px",
            MinHeight       = "80px",
            MaxHeight       = "800px",
            ShowControls    = false,
            MinZoom         = 10,
            MaxZoom         = 1000,
        };

        var result          = await ctrl.Upsert(fileId, req);

        var ok      = Assert.IsType<OkObjectResult>(result.Result);
        var record  = Assert.IsType<UploadFileAudioConfigRecord>(ok.Value);
        Assert.Equal("#00FF00", record.WaveColor);
        Assert.False(record.EnableHover);
        Assert.True(record.EnableZoom);
        Assert.Equal("300px", record.InitialHeight);
        // Only one config row should exist (upsert, not insert)
        Assert.Equal(1, await db.UploadFileAudioConfigs.CountAsync());
    }

    [Fact]
    public async Task Upsert_ReturnsNotFound_WhenFileDoesNotExist()
    {
        await using var db  = CreateDb();
        var ctrl            = Build(db, AuthUser(Guid.NewGuid()));
        var req             = new UpsertAudioConfigRequest { InitialHeight = "200px", MinHeight = "80px", MaxHeight = "800px" };

        var result          = await ctrl.Upsert(Guid.NewGuid(), req);

        Assert.IsType<NotFoundResult>(result.Result);
        Assert.Equal(0, await db.UploadFileAudioConfigs.CountAsync());
    }

    [Fact]
    public async Task Upsert_ThrowsUnauthorized_WhenNoUserIdentityClaim()
    {
        await using var db  = CreateDb();
        var (fileId, _)     = await SeedFileAsync(db);
        var ctrl            = Build(db, Anonymous());  // no NameIdentifier claim
        var req             = new UpsertAudioConfigRequest { InitialHeight = "200px", MinHeight = "80px", MaxHeight = "800px" };

        var act = async () => await ctrl.Upsert(fileId, req);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(act);
    }

    [Fact]
    public async Task Upsert_UnrelatedCaller_ReturnsForbid()
    {
        // The core of the fix: this used to let any authenticated user overwrite the player
        // config for any file, regardless of ownership/visibility.
        await using var db  = CreateDb();
        var (fileId, _)     = await SeedFileAsync(db);
        var ctrl            = Build(db, AuthUser(Guid.NewGuid()));
        var req             = new UpsertAudioConfigRequest { InitialHeight = "200px", MinHeight = "80px", MaxHeight = "800px" };

        var result          = await ctrl.Upsert(fileId, req);

        Assert.IsType<ForbidResult>(result.Result);
    }

    // ── DELETE ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_RemovesConfig_AndReturnsNoContent()
    {
        await using var db  = CreateDb();
        var (fileId, ownerId) = await SeedFileAsync(db);
        await SeedConfigAsync(db, fileId, ownerId);
        var ctrl            = Build(db, AuthUser(ownerId));

        var result          = await ctrl.Delete(fileId);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal(0, await db.UploadFileAudioConfigs.CountAsync());
    }

    [Fact]
    public async Task Delete_ReturnsNoContent_WhenFileExistsButNoConfigSaved()
    {
        // Idempotent delete: the file exists (so the caller's ownership can be checked) but has
        // no config row yet.
        await using var db  = CreateDb();
        var (fileId, ownerId) = await SeedFileAsync(db);
        var ctrl            = Build(db, AuthUser(ownerId));

        var result          = await ctrl.Delete(fileId);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task Delete_ReturnsNotFound_WhenFileDoesNotExist()
    {
        await using var db  = CreateDb();
        var ctrl            = Build(db, AuthUser(Guid.NewGuid()));

        var result          = await ctrl.Delete(Guid.NewGuid());

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Delete_UnrelatedCaller_ReturnsForbid()
    {
        // The core of the fix: this used to let any authenticated user reset/delete another
        // user's saved audio-player config.
        await using var db  = CreateDb();
        var (fileId, ownerId) = await SeedFileAsync(db);
        await SeedConfigAsync(db, fileId, ownerId);
        var ctrl            = Build(db, AuthUser(Guid.NewGuid()));

        var result          = await ctrl.Delete(fileId);

        Assert.IsType<ForbidResult>(result);
    }

    // ── The race two saves a second apart create (2026-09-06 audio audit, phase 6) ──
    //
    // The editor saves the view automatically as controls are used, and the colour-ramp picker
    // does it without being awaited — so toggling the spectrogram and then choosing a ramp puts
    // two requests in the air at once. On a recording with no saved row yet both found nothing,
    // both inserted, and the second hit the one-to-one unique index and came back as an unhandled
    // 500 that the editor reported as "this recording isn't yours to change".
    //
    // THERE IS NO UNIT TEST HERE, deliberately. Reproducing the interleave needs one request to
    // read after another has read and before it has written, and a controller test can only call
    // Upsert start-to-finish: a sequential pair simply updates, and passes against the broken code
    // just as happily. A test that cannot fail is worse than none.
    //
    // What is covered instead: AudioFilePreview serialises its saves so two are never in flight
    // (the actual prevention), and the browser test that found this — How_you_set_the_editor_up —
    // exercises exactly the two-controls-in-a-row gesture that produced it. The server's recovery
    // is defence in depth for any other client.
}
