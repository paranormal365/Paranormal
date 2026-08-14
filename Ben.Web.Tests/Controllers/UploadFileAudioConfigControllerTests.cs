using AutoMapper;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Service.Models.Entities;
using Ben.Web.Library.Manage.Audio;
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
        await using var db  = CreateDb();
        var (fileId, _)     = await SeedFileAsync(db);
        var ctrl            = Build(db, AuthUser(Guid.NewGuid()));

        var result          = await ctrl.Get(fileId);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Null(ok.Value);
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
}
