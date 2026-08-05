using AutoMapper;
using Ben.Data.Common.Enums;
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
/// Tests for <see cref="AudioMarkerController"/> — verifies CRUD operations,
/// ordering by time, and proper validation of parent file existence.
/// </summary>
public class AudioMarkerControllerTests
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
        m.Setup(x => x.Map<AudioMarkerRecord>(It.IsAny<object>()))
         .Returns<object>(o =>
         {
             if (o is not AudioMarker e) return new AudioMarkerRecord { Label = "" };
             return new AudioMarkerRecord
             {
                 Id                 = e.Id,
                 UploadFileId       = e.UploadFileId,
                 TimeSeconds        = e.TimeSeconds,
                 Label              = e.Label,
                 ConfidenceLevel    = e.ConfidenceLevel,
                 Note               = e.Note,
                 DateCreated        = e.DateCreated,
                 CreatedByAppUserId = e.CreatedByAppUserId,
             };
         });
        m.Setup(x => x.Map<IEnumerable<AudioMarkerRecord>>(It.IsAny<object>()))
         .Returns<object>(o =>
         {
             if (o is not IEnumerable<AudioMarker> list) return [];
             return list.Select(e => new AudioMarkerRecord
             {
                 Id                 = e.Id,
                 UploadFileId       = e.UploadFileId,
                 TimeSeconds        = e.TimeSeconds,
                 Label              = e.Label,
                 ConfidenceLevel    = e.ConfidenceLevel,
                 Note               = e.Note,
                 DateCreated        = e.DateCreated,
                 CreatedByAppUserId = e.CreatedByAppUserId,
             });
         });
        return m.Object;
    }

    private static AudioMarkerController Build(
        IDbContextFactory<BenDataContext> factory,
        Guid? userId = null)
    {
        var ctrl = new AudioMarkerController(factory, CreateMapper(), new Mock<IAuditLogService>().Object);
        var claims = userId.HasValue
            ? new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString())
              ], "Bearer"))
            : new ClaimsPrincipal(new ClaimsIdentity());
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = claims }
        };
        return ctrl;
    }

    private static async Task<Guid> SeedFileAsync(IDbContextFactory<BenDataContext> factory)
    {
        var fileId = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        db.UploadFiles.Add(new UploadFile
        {
            Id = fileId, UploadFileTypeId = Guid.NewGuid(), AppUserId = Guid.NewGuid(),
            FileName = "audio.mp3", StoredFileName = "s.mp3", ContentType = "audio/mpeg",
            FileSize = 100, FileData = new byte[4],
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid(),
        });
        await db.SaveChangesAsync();
        return fileId;
    }

    private static async Task<Guid> SeedMarkerAsync(
        IDbContextFactory<BenDataContext> factory,
        Guid fileId, double timeSeconds = 10,
        string label = "Whisper?", EvpConfidenceLevel confidence = EvpConfidenceLevel.Possible)
    {
        var markerId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        db.AudioMarkers.Add(new AudioMarker
        {
            Id = markerId, UploadFileId = fileId,
            TimeSeconds = timeSeconds, Label = label, ConfidenceLevel = confidence,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        await db.SaveChangesAsync();
        return markerId;
    }

    // ── GetAll ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_ReturnsEmpty_WhenNoMarkers()
    {
        var factory = CreateFactory();
        var fileId  = await SeedFileAsync(factory);
        var ctrl    = Build(factory, Guid.NewGuid());

        var result  = await ctrl.GetAll(fileId, default);

        var ok      = Assert.IsType<OkObjectResult>(result.Result);
        var markers = Assert.IsAssignableFrom<IEnumerable<AudioMarkerRecord>>(ok.Value);
        Assert.Empty(markers);
    }

    [Fact]
    public async Task GetAll_ReturnsMarkers_OrderedByTimeSeconds()
    {
        var factory = CreateFactory();
        var fileId  = await SeedFileAsync(factory);
        await SeedMarkerAsync(factory, fileId, timeSeconds: 30.0);
        await SeedMarkerAsync(factory, fileId, timeSeconds: 5.0);
        await SeedMarkerAsync(factory, fileId, timeSeconds: 15.0);
        var ctrl    = Build(factory, Guid.NewGuid());

        var result  = await ctrl.GetAll(fileId, default);

        var ok      = Assert.IsType<OkObjectResult>(result.Result);
        var markers = Assert.IsAssignableFrom<IEnumerable<AudioMarkerRecord>>(ok.Value).ToList();
        Assert.Equal(3, markers.Count);
        Assert.Equal(5.0, markers[0].TimeSeconds);
        Assert.Equal(15.0, markers[1].TimeSeconds);
        Assert.Equal(30.0, markers[2].TimeSeconds);
    }

    // ── GetById ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetById_ReturnsNotFound_WhenMarkerDoesNotExist()
    {
        var factory = CreateFactory();
        var fileId  = await SeedFileAsync(factory);
        var ctrl    = Build(factory, Guid.NewGuid());

        var result  = await ctrl.GetById(fileId, Guid.NewGuid(), default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetById_ReturnsRecord_WhenMarkerExists()
    {
        var factory  = CreateFactory();
        var fileId   = await SeedFileAsync(factory);
        var markerId = await SeedMarkerAsync(factory, fileId, timeSeconds: 42.0, label: "Name?");
        var ctrl     = Build(factory, Guid.NewGuid());

        var result   = await ctrl.GetById(fileId, markerId, default);

        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var record = Assert.IsType<AudioMarkerRecord>(ok.Value);
        Assert.Equal(markerId, record.Id);
        Assert.Equal(fileId, record.UploadFileId);
        Assert.Equal("Name?", record.Label);
        Assert.Equal(42.0, record.TimeSeconds);
    }

    [Fact]
    public async Task GetById_ReturnsNotFound_WhenMarkerExistsOnDifferentFile()
    {
        var factory  = CreateFactory();
        var fileId1  = await SeedFileAsync(factory);
        var fileId2  = await SeedFileAsync(factory);
        var markerId = await SeedMarkerAsync(factory, fileId1);
        var ctrl     = Build(factory, Guid.NewGuid());

        var result   = await ctrl.GetById(fileId2, markerId, default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    // ── Create ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_ReturnsNotFound_WhenFileDoesNotExist()
    {
        var factory = CreateFactory();
        var userId  = Guid.NewGuid();
        var ctrl    = Build(factory, userId);

        var result  = await ctrl.Create(Guid.NewGuid(),
            new CreateAudioMarkerRequest(5.0, "Whisper?", EvpConfidenceLevel.Possible, null), default);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task Create_ReturnsUnauthorized_WhenNoUserClaim()
    {
        var factory = CreateFactory();
        var fileId  = await SeedFileAsync(factory);
        var ctrl    = Build(factory, userId: null); // no user

        var result  = await ctrl.Create(fileId,
            new CreateAudioMarkerRequest(5.0, "Whisper?", EvpConfidenceLevel.Possible, null), default);

        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public async Task Create_Returns201_WithCorrectFields()
    {
        var factory = CreateFactory();
        var fileId  = await SeedFileAsync(factory);
        var userId  = Guid.NewGuid();
        var ctrl    = Build(factory, userId);

        var result  = await ctrl.Create(fileId,
            new CreateAudioMarkerRequest(12.5, "Footsteps", EvpConfidenceLevel.Confirmed, "Very clear"), default);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var record  = Assert.IsType<AudioMarkerRecord>(created.Value);
        Assert.Equal(fileId, record.UploadFileId);
        Assert.Equal(12.5, record.TimeSeconds);
        Assert.Equal("Footsteps", record.Label);
        Assert.Equal(EvpConfidenceLevel.Confirmed, record.ConfidenceLevel);
        Assert.Equal("Very clear", record.Note);
        Assert.Equal(userId, record.CreatedByAppUserId);
    }

    // ── Update ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_ReturnsNotFound_WhenMarkerDoesNotExist()
    {
        var factory = CreateFactory();
        var fileId  = await SeedFileAsync(factory);
        var ctrl    = Build(factory, Guid.NewGuid());

        var result  = await ctrl.Update(fileId, Guid.NewGuid(),
            new UpdateAudioMarkerRequest(5.0, "new", EvpConfidenceLevel.Probable, null), default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Update_ChangesLabelConfidenceAndNote()
    {
        var factory  = CreateFactory();
        var fileId   = await SeedFileAsync(factory);
        var markerId = await SeedMarkerAsync(factory, fileId, label: "old", confidence: EvpConfidenceLevel.Possible);
        var ctrl     = Build(factory, Guid.NewGuid());

        var result   = await ctrl.Update(fileId, markerId,
            new UpdateAudioMarkerRequest(20.0, "new", EvpConfidenceLevel.Confirmed, "elaborated"), default);

        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var record = Assert.IsType<AudioMarkerRecord>(ok.Value);
        Assert.Equal("new", record.Label);
        Assert.Equal(EvpConfidenceLevel.Confirmed, record.ConfidenceLevel);
        Assert.Equal("elaborated", record.Note);
        Assert.Equal(20.0, record.TimeSeconds);
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_ReturnsNoContent_WhenMarkerDeleted()
    {
        var factory  = CreateFactory();
        var fileId   = await SeedFileAsync(factory);
        var markerId = await SeedMarkerAsync(factory, fileId);
        var ctrl     = Build(factory, Guid.NewGuid());

        var result   = await ctrl.Delete(fileId, markerId, default);

        Assert.IsType<NoContentResult>(result);

        await using var db = await factory.CreateDbContextAsync();
        Assert.Null(await db.AudioMarkers.FindAsync(markerId));
    }

    [Fact]
    public async Task Delete_ReturnsNotFound_WhenMarkerDoesNotExist()
    {
        var factory = CreateFactory();
        var fileId  = await SeedFileAsync(factory);
        var ctrl    = Build(factory, Guid.NewGuid());

        var result  = await ctrl.Delete(fileId, Guid.NewGuid(), default);

        Assert.IsType<NotFoundResult>(result);
    }
}
