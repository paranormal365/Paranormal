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
/// Tests for <see cref="UploadFileRegionNoteController"/> — verifies CRUD operations,
/// ordering, and proper validation of parent file existence.
/// </summary>
public class UploadFileRegionNoteControllerTests
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
        m.Setup(x => x.Map<UploadFileRegionNoteRecord>(It.IsAny<object>()))
         .Returns<object>(o =>
         {
             if (o is not UploadFileRegionNote e) return new UploadFileRegionNoteRecord { NoteHtml = "" };
             return new UploadFileRegionNoteRecord
             {
                 Id                 = e.Id,
                 UploadFileId       = e.UploadFileId,
                 RegionStart        = e.RegionStart,
                 RegionEnd          = e.RegionEnd,
                 RegionLabel        = e.RegionLabel,
                 TimeOffset         = e.TimeOffset,
                 NoteHtml           = e.NoteHtml,
                 IsPublic           = e.IsPublic,
                 DateCreated        = e.DateCreated,
                 CreatedByAppUserId = e.CreatedByAppUserId,
             };
         });
        m.Setup(x => x.Map<IEnumerable<UploadFileRegionNoteRecord>>(It.IsAny<object>()))
         .Returns<object>(o =>
         {
             if (o is not IEnumerable<UploadFileRegionNote> list) return [];
             return list.Select(e => new UploadFileRegionNoteRecord
             {
                 Id           = e.Id,
                 UploadFileId = e.UploadFileId,
                 RegionStart  = e.RegionStart,
                 RegionEnd    = e.RegionEnd,
                 RegionLabel  = e.RegionLabel,
                 TimeOffset   = e.TimeOffset,
                 NoteHtml     = e.NoteHtml,
                 IsPublic     = e.IsPublic,
                 DateCreated  = e.DateCreated,
                 CreatedByAppUserId = e.CreatedByAppUserId,
             });
         });
        return m.Object;
    }

    private static UploadFileRegionNoteController Build(
        IDbContextFactory<BenDataContext> factory,
        Guid? userId = null)
    {
        var ctrl = new UploadFileRegionNoteController(factory, CreateMapper(), new Mock<IAuditLogService>().Object);
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

    private static async Task<Guid> SeedNoteAsync(
        IDbContextFactory<BenDataContext> factory,
        Guid fileId, double regionStart = 10, double? timeOffset = null,
        string html = "<p>Note</p>", bool isPublic = false)
    {
        var noteId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        db.UploadFileRegionNotes.Add(new UploadFileRegionNote
        {
            Id = noteId, UploadFileId = fileId,
            RegionStart = regionStart, RegionEnd = regionStart + 5,
            TimeOffset = timeOffset, NoteHtml = html, IsPublic = isPublic,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        await db.SaveChangesAsync();
        return noteId;
    }

    // ── GetAll ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_ReturnsEmpty_WhenNoNotes()
    {
        var factory = CreateFactory();
        var fileId  = await SeedFileAsync(factory);
        var ctrl    = Build(factory, Guid.NewGuid());

        var result  = await ctrl.GetAll(fileId, default);

        var ok    = Assert.IsType<OkObjectResult>(result.Result);
        var notes = Assert.IsAssignableFrom<IEnumerable<UploadFileRegionNoteRecord>>(ok.Value);
        Assert.Empty(notes);
    }

    [Fact]
    public async Task GetAll_ReturnsNotes_OrderedByRegionStartThenTimeOffset()
    {
        var factory = CreateFactory();
        var fileId  = await SeedFileAsync(factory);
        // Add notes out of order
        await SeedNoteAsync(factory, fileId, regionStart: 30.0, timeOffset: null);
        await SeedNoteAsync(factory, fileId, regionStart: 10.0, timeOffset: 12.0);
        await SeedNoteAsync(factory, fileId, regionStart: 10.0, timeOffset: null);
        var ctrl    = Build(factory, Guid.NewGuid());

        var result  = await ctrl.GetAll(fileId, default);

        var ok    = Assert.IsType<OkObjectResult>(result.Result);
        var notes = Assert.IsAssignableFrom<IEnumerable<UploadFileRegionNoteRecord>>(ok.Value)
                          .ToList();
        Assert.Equal(3, notes.Count);
        Assert.Equal(10.0, notes[0].RegionStart); // region 10 first
        Assert.Equal(10.0, notes[1].RegionStart);
        Assert.Equal(30.0, notes[2].RegionStart); // region 30 last
    }

    // ── GetById ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetById_ReturnsNotFound_WhenNoteDoesNotExist()
    {
        var factory = CreateFactory();
        var fileId  = await SeedFileAsync(factory);
        var ctrl    = Build(factory, Guid.NewGuid());

        var result  = await ctrl.GetById(fileId, Guid.NewGuid(), default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetById_ReturnsRecord_WhenNoteExists()
    {
        var factory = CreateFactory();
        var fileId  = await SeedFileAsync(factory);
        var noteId  = await SeedNoteAsync(factory, fileId, regionStart: 5.0, html: "<b>Hi</b>");
        var ctrl    = Build(factory, Guid.NewGuid());

        var result  = await ctrl.GetById(fileId, noteId, default);

        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var record = Assert.IsType<UploadFileRegionNoteRecord>(ok.Value);
        Assert.Equal(noteId, record.Id);
        Assert.Equal(fileId, record.UploadFileId);
        Assert.Equal("<b>Hi</b>", record.NoteHtml);
        Assert.Equal(5.0, record.RegionStart);
    }

    [Fact]
    public async Task GetById_ReturnsNotFound_WhenNoteExistsOnDifferentFile()
    {
        var factory  = CreateFactory();
        var fileId1  = await SeedFileAsync(factory);
        var fileId2  = await SeedFileAsync(factory);
        var noteId   = await SeedNoteAsync(factory, fileId1);
        var ctrl     = Build(factory, Guid.NewGuid());

        // Ask for the note but scope it to fileId2
        var result   = await ctrl.GetById(fileId2, noteId, default);

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
            new CreateRegionNoteRequest(0, 5, null, null, "<p>x</p>", false), default);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task Create_ReturnsUnauthorized_WhenNoUserClaim()
    {
        var factory = CreateFactory();
        var fileId  = await SeedFileAsync(factory);
        var ctrl    = Build(factory, userId: null); // no user

        var result  = await ctrl.Create(fileId,
            new CreateRegionNoteRequest(0, 5, null, null, "<p>x</p>", false), default);

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
            new CreateRegionNoteRequest(10.0, 20.0, "Intro", null, "<p>Cool part</p>", true), default);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var record  = Assert.IsType<UploadFileRegionNoteRecord>(created.Value);
        Assert.Equal(fileId, record.UploadFileId);
        Assert.Equal(10.0, record.RegionStart);
        Assert.Equal(20.0, record.RegionEnd);
        Assert.Equal("Intro", record.RegionLabel);
        Assert.Null(record.TimeOffset);
        Assert.Equal("<p>Cool part</p>", record.NoteHtml);
        Assert.True(record.IsPublic);
        Assert.Equal(userId, record.CreatedByAppUserId);
    }

    [Fact]
    public async Task Create_StoresTimeOffset_WhenPointInTime()
    {
        var factory = CreateFactory();
        var fileId  = await SeedFileAsync(factory);
        var userId  = Guid.NewGuid();
        var ctrl    = Build(factory, userId);

        var result  = await ctrl.Create(fileId,
            new CreateRegionNoteRequest(10.0, 20.0, null, 14.5, "<em>here</em>", false), default);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var record  = Assert.IsType<UploadFileRegionNoteRecord>(created.Value);
        Assert.Equal(14.5, record.TimeOffset);
    }

    // ── Update ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_ReturnsNotFound_WhenNoteDoesNotExist()
    {
        var factory = CreateFactory();
        var fileId  = await SeedFileAsync(factory);
        var ctrl    = Build(factory, Guid.NewGuid());

        var result  = await ctrl.Update(fileId, Guid.NewGuid(),
            new UpdateRegionNoteRequest(null, "<p>new</p>", false), default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Update_ChangesNoteHtml_AndPublicFlag()
    {
        var factory = CreateFactory();
        var fileId  = await SeedFileAsync(factory);
        var noteId  = await SeedNoteAsync(factory, fileId, html: "<p>old</p>", isPublic: false);
        var ctrl    = Build(factory, Guid.NewGuid());

        var result  = await ctrl.Update(fileId, noteId,
            new UpdateRegionNoteRequest(7.0, "<p>new</p>", true), default);

        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var record = Assert.IsType<UploadFileRegionNoteRecord>(ok.Value);
        Assert.Equal("<p>new</p>", record.NoteHtml);
        Assert.True(record.IsPublic);
        Assert.Equal(7.0, record.TimeOffset);
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_ReturnsNoContent_WhenNoteDeleted()
    {
        var factory = CreateFactory();
        var fileId  = await SeedFileAsync(factory);
        var noteId  = await SeedNoteAsync(factory, fileId);
        var ctrl    = Build(factory, Guid.NewGuid());

        var result  = await ctrl.Delete(fileId, noteId, default);

        Assert.IsType<NoContentResult>(result);

        await using var db = await factory.CreateDbContextAsync();
        Assert.Null(await db.UploadFileRegionNotes.FindAsync(noteId));
    }

    [Fact]
    public async Task Delete_ReturnsNotFound_WhenNoteDoesNotExist()
    {
        var factory = CreateFactory();
        var fileId  = await SeedFileAsync(factory);
        var ctrl    = Build(factory, Guid.NewGuid());

        var result  = await ctrl.Delete(fileId, Guid.NewGuid(), default);

        Assert.IsType<NotFoundResult>(result);
    }
}
