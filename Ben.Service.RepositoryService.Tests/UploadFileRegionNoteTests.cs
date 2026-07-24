using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Ben.Service.RepositoryService.Tests;

/// <summary>
/// Entity-level tests for <see cref="UploadFileRegionNote"/> and the parent-file
/// tracking fields on <see cref="UploadFile"/> (ParentFileId, RegionStart, RegionEnd).
/// </summary>
public class UploadFileRegionNoteTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var opts = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PooledDbContextFactory<BenDataContext>(opts);
    }

    private static UploadFile MakeFile(Guid? parentId = null, double? start = null, double? end = null)
        => new()
        {
            Id = Guid.NewGuid(), UploadFileTypeId = Guid.NewGuid(), AppUserId = Guid.NewGuid(),
            FileName = "audio.wav", StoredFileName = "s.wav", ContentType = "audio/wav",
            FileSize = 100, FileData = new byte[4],
            ParentFileId = parentId, RegionStart = start, RegionEnd = end,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid(),
        };

    private static UploadFileRegionNote MakeNote(Guid fileId, double start = 0, double end = 5,
        double? timeOffset = null, string html = "<p>x</p>")
        => new()
        {
            Id = Guid.NewGuid(), UploadFileId = fileId,
            RegionStart = start, RegionEnd = end,
            TimeOffset = timeOffset, NoteHtml = html, IsPublic = false,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid(),
        };

    // ── UploadFileRegionNote CRUD ─────────────────────────────────────────────

    [Fact]
    public async Task RegionNote_CanBeCreatedAndRetrieved()
    {
        var factory = CreateFactory();
        var file    = MakeFile();
        var note    = MakeNote(file.Id, start: 10, end: 20, html: "<b>test</b>");

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFiles.Add(file);
            db.UploadFileRegionNotes.Add(note);
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var loaded = await db.UploadFileRegionNotes.FindAsync(note.Id);
            Assert.NotNull(loaded);
            Assert.Equal(file.Id, loaded.UploadFileId);
            Assert.Equal(10, loaded.RegionStart);
            Assert.Equal(20, loaded.RegionEnd);
            Assert.Equal("<b>test</b>", loaded.NoteHtml);
        }
    }

    [Fact]
    public async Task RegionNote_TimeOffset_Null_IsOverallNote()
    {
        var factory = CreateFactory();
        var file    = MakeFile();
        var note    = MakeNote(file.Id, timeOffset: null);

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFiles.Add(file);
            db.UploadFileRegionNotes.Add(note);
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var loaded = await db.UploadFileRegionNotes.FindAsync(note.Id);
            Assert.NotNull(loaded);
            Assert.Null(loaded.TimeOffset);
        }
    }

    [Fact]
    public async Task RegionNote_TimeOffset_Value_IsPointInTimeNote()
    {
        var factory = CreateFactory();
        var file    = MakeFile();
        var note    = MakeNote(file.Id, start: 5, end: 15, timeOffset: 9.25);

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFiles.Add(file);
            db.UploadFileRegionNotes.Add(note);
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var loaded = await db.UploadFileRegionNotes.FindAsync(note.Id);
            Assert.NotNull(loaded);
            Assert.Equal(9.25, loaded.TimeOffset);
        }
    }

    [Fact]
    public async Task RegionNote_CascadeDeletesWithParentFile()
    {
        var factory = CreateFactory();
        var file    = MakeFile();
        var note    = MakeNote(file.Id);
        var noteId  = note.Id;

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFiles.Add(file);
            db.UploadFileRegionNotes.Add(note);
            await db.SaveChangesAsync();
        }

        // Delete the parent file; EF InMemory cascades automatically
        await using (var db = await factory.CreateDbContextAsync())
        {
            var f = await db.UploadFiles
                .Include(x => x.RegionNotes)
                .FirstAsync(x => x.Id == file.Id);
            db.UploadFiles.Remove(f);
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            Assert.Null(await db.UploadFileRegionNotes.FindAsync(noteId));
        }
    }

    [Fact]
    public async Task MultipleNotes_CanExistForSameRegion()
    {
        var factory = CreateFactory();
        var file    = MakeFile();
        var n1      = MakeNote(file.Id, start: 5, end: 15, timeOffset: null, html: "<p>overall</p>");
        var n2      = MakeNote(file.Id, start: 5, end: 15, timeOffset: 7.0, html: "<p>point</p>");

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFiles.Add(file);
            db.UploadFileRegionNotes.AddRange(n1, n2);
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var notes = await db.UploadFileRegionNotes
                .Where(n => n.UploadFileId == file.Id)
                .ToListAsync();
            Assert.Equal(2, notes.Count);
            Assert.Contains(notes, n => n.TimeOffset is null);
            Assert.Contains(notes, n => n.TimeOffset == 7.0);
        }
    }

    // ── Parent-file tracking ─────────────────────────────────────────────────

    [Fact]
    public async Task UploadFile_ParentFileId_IsNull_ForOriginalFile()
    {
        var factory = CreateFactory();
        var file    = MakeFile(parentId: null, start: null, end: null);

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFiles.Add(file);
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var loaded = await db.UploadFiles.FindAsync(file.Id);
            Assert.NotNull(loaded);
            Assert.Null(loaded.ParentFileId);
            Assert.Null(loaded.RegionStart);
            Assert.Null(loaded.RegionEnd);
        }
    }

    [Fact]
    public async Task UploadFile_ParentFileId_Set_WhenCreatedFromClip()
    {
        var factory = CreateFactory();
        var parent  = MakeFile();
        var clip    = MakeFile(parentId: parent.Id, start: 5.0, end: 10.0);

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFiles.AddRange(parent, clip);
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var loaded = await db.UploadFiles.FindAsync(clip.Id);
            Assert.NotNull(loaded);
            Assert.Equal(parent.Id, loaded.ParentFileId);
            Assert.Equal(5.0, loaded.RegionStart);
            Assert.Equal(10.0, loaded.RegionEnd);
        }
    }

    [Fact]
    public async Task ChildClips_Navigation_ReturnsClipsForParent()
    {
        var factory = CreateFactory();
        var parent  = MakeFile();
        var clip1   = MakeFile(parentId: parent.Id, start: 0, end: 5);
        var clip2   = MakeFile(parentId: parent.Id, start: 10, end: 20);
        var other   = MakeFile();  // unrelated file

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFiles.AddRange(parent, clip1, clip2, other);
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var clips = await db.UploadFiles
                .Where(f => f.ParentFileId == parent.Id)
                .OrderBy(f => f.RegionStart)
                .ToListAsync();
            Assert.Equal(2, clips.Count);
            Assert.Equal(0, clips[0].RegionStart);
            Assert.Equal(10, clips[1].RegionStart);
        }
    }

    [Fact]
    public async Task ChildClips_DoNotIncludeUnrelatedFiles()
    {
        var factory = CreateFactory();
        var parent  = MakeFile();
        var other   = MakeFile(); // different parent

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UploadFiles.AddRange(parent, other);
            await db.SaveChangesAsync();
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var clips = await db.UploadFiles
                .Where(f => f.ParentFileId == parent.Id)
                .ToListAsync();
            Assert.Empty(clips);
        }
    }
}
