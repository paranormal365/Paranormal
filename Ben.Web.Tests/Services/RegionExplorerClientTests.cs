using Ben.Service.Models.Entities;
using Ben.Web.Library.Services;
using Ben.Web.WebApp.Services.WebApi;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Tests for the region-note and audio-clip methods of <see cref="BenAdminClientAdapter"/>.
/// Verifies correct delegation to <see cref="IWebApiClient"/> and correct parameter passing.
/// </summary>
public class RegionExplorerClientTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Mock<IWebApiClient>     ApiMock()  => new();
    private static Mock<IWebApiAuthService> AuthMock() => new();

    private static BenAdminClientAdapter Build(
        Mock<IWebApiClient> api, Mock<IWebApiAuthService>? auth = null)
        => new(api.Object, (auth ?? AuthMock()).Object);

    // ── GetRegionNotesAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task GetRegionNotesAsync_DelegatesToApiAndReturnsNotes()
    {
        var fileId   = Guid.NewGuid();
        var expected = new List<UploadFileRegionNoteRecord>
        {
            new() { Id = Guid.NewGuid(), UploadFileId = fileId, NoteHtml = "<p>hi</p>",
                    RegionStart = 0, RegionEnd = 5, CreatedByAppUserId = Guid.NewGuid() }
        };
        var apiMock = ApiMock();
        apiMock.Setup(x => x.GetRegionNotesAsync(fileId, default))
               .ReturnsAsync(expected);

        var result = await Build(apiMock).GetRegionNotesAsync(fileId);

        Assert.Single(result);
        Assert.Equal("<p>hi</p>", result[0].NoteHtml);
        apiMock.Verify(x => x.GetRegionNotesAsync(fileId, default), Times.Once);
    }

    [Fact]
    public async Task GetRegionNotesAsync_ReturnsEmptyList_WhenApiReturnsEmpty()
    {
        var fileId  = Guid.NewGuid();
        var apiMock = ApiMock();
        apiMock.Setup(x => x.GetRegionNotesAsync(fileId, default))
               .ReturnsAsync([]);

        var result = await Build(apiMock).GetRegionNotesAsync(fileId);

        Assert.Empty(result);
    }

    // ── CreateRegionNoteAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task CreateRegionNoteAsync_DelegatesToApi_AndReturnsRecord()
    {
        var fileId  = Guid.NewGuid();
        var userId  = Guid.NewGuid();
        var request = new CreateRegionNoteRequest(5.0, 15.0, "Verse", 7.0, "<em>note</em>", true);
        var record  = new UploadFileRegionNoteRecord
        {
            Id = Guid.NewGuid(), UploadFileId = fileId, NoteHtml = "<em>note</em>",
            RegionStart = 5.0, RegionEnd = 15.0, TimeOffset = 7.0, IsPublic = true,
            CreatedByAppUserId = userId
        };
        var apiMock = ApiMock();
        apiMock.Setup(x => x.CreateRegionNoteAsync(fileId, request, default))
               .ReturnsAsync(record);

        var result = await Build(apiMock).CreateRegionNoteAsync(fileId, request);

        Assert.NotNull(result);
        Assert.Equal(5.0, result!.RegionStart);
        Assert.Equal(7.0, result.TimeOffset);
        Assert.True(result.IsPublic);
    }

    [Fact]
    public async Task CreateRegionNoteAsync_ReturnsNull_WhenApiFails()
    {
        var fileId  = Guid.NewGuid();
        var request = new CreateRegionNoteRequest(0, 5, null, null, "<p>x</p>", false);
        var apiMock = ApiMock();
        apiMock.Setup(x => x.CreateRegionNoteAsync(fileId, request, default))
               .ReturnsAsync((UploadFileRegionNoteRecord?)null);

        var result = await Build(apiMock).CreateRegionNoteAsync(fileId, request);

        Assert.Null(result);
    }

    // ── UpdateRegionNoteAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task UpdateRegionNoteAsync_DelegatesToApiWithCorrectParams()
    {
        var fileId  = Guid.NewGuid();
        var noteId  = Guid.NewGuid();
        var request = new UpdateRegionNoteRequest(9.5, "<p>updated</p>", false);
        var record  = new UploadFileRegionNoteRecord
        {
            Id = noteId, UploadFileId = fileId, NoteHtml = "<p>updated</p>",
            TimeOffset = 9.5, RegionStart = 0, RegionEnd = 10,
            CreatedByAppUserId = Guid.NewGuid()
        };
        var apiMock = ApiMock();
        apiMock.Setup(x => x.UpdateRegionNoteAsync(fileId, noteId, request, default))
               .ReturnsAsync(record);

        var result = await Build(apiMock).UpdateRegionNoteAsync(fileId, noteId, request);

        Assert.NotNull(result);
        Assert.Equal("<p>updated</p>", result!.NoteHtml);
        Assert.Equal(9.5, result.TimeOffset);
        apiMock.Verify(x => x.UpdateRegionNoteAsync(fileId, noteId, request, default), Times.Once);
    }

    // ── DeleteRegionNoteAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task DeleteRegionNoteAsync_ReturnsTrue_WhenApiSucceeds()
    {
        var fileId  = Guid.NewGuid();
        var noteId  = Guid.NewGuid();
        var apiMock = ApiMock();
        apiMock.Setup(x => x.DeleteRegionNoteAsync(fileId, noteId, default))
               .ReturnsAsync(true);

        var result = await Build(apiMock).DeleteRegionNoteAsync(fileId, noteId);

        Assert.True(result);
        apiMock.Verify(x => x.DeleteRegionNoteAsync(fileId, noteId, default), Times.Once);
    }

    [Fact]
    public async Task DeleteRegionNoteAsync_ReturnsFalse_WhenApiFails()
    {
        var fileId  = Guid.NewGuid();
        var noteId  = Guid.NewGuid();
        var apiMock = ApiMock();
        apiMock.Setup(x => x.DeleteRegionNoteAsync(fileId, noteId, default))
               .ReturnsAsync(false);

        var result = await Build(apiMock).DeleteRegionNoteAsync(fileId, noteId);

        Assert.False(result);
    }

    // ── ClipAudioAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task ClipAudioAsync_DelegatesToApi_AndReturnsRecord()
    {
        var fileId   = Guid.NewGuid();
        var typeId   = Guid.NewGuid();
        var request  = new ClipAudioRequest(5.0, 15.0, "Chorus", true, typeId);
        var record   = new UploadFileRecord
        {
            FileName = "chorus.wav", StoredFileName = "c.wav", ContentType = "audio/wav",
            ParentFileId = fileId, RegionStart = 5.0, RegionEnd = 15.0
        };
        var apiMock = ApiMock();
        apiMock.Setup(x => x.ClipAudioAsync(fileId, request, default))
               .ReturnsAsync(record);

        var result = await Build(apiMock).ClipAudioAsync(fileId, request);

        Assert.NotNull(result);
        Assert.Equal(fileId, result!.ParentFileId);
        Assert.Equal(5.0,  result.RegionStart);
        Assert.Equal(15.0, result.RegionEnd);
        apiMock.Verify(x => x.ClipAudioAsync(fileId, request, default), Times.Once);
    }

    [Fact]
    public async Task ClipAudioAsync_ReturnsNull_WhenApiFails()
    {
        var fileId  = Guid.NewGuid();
        var request = new ClipAudioRequest(0, 5, null, false, Guid.NewGuid());
        var apiMock = ApiMock();
        apiMock.Setup(x => x.ClipAudioAsync(fileId, request, default))
               .ReturnsAsync((UploadFileRecord?)null);

        var result = await Build(apiMock).ClipAudioAsync(fileId, request);

        Assert.Null(result);
    }

    // ── GetChildClipsAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task GetChildClipsAsync_DelegatesToApiAndReturnsClips()
    {
        var parentId = Guid.NewGuid();
        var clips    = new List<UploadFileRecord>
        {
            new() { FileName = "clip1.wav", StoredFileName = "c1.wav", ContentType = "audio/wav",
                    ParentFileId = parentId, RegionStart = 0, RegionEnd = 5 },
            new() { FileName = "clip2.wav", StoredFileName = "c2.wav", ContentType = "audio/wav",
                    ParentFileId = parentId, RegionStart = 10, RegionEnd = 20 },
        };
        var apiMock = ApiMock();
        apiMock.Setup(x => x.GetChildClipsAsync(parentId, default))
               .ReturnsAsync(clips);

        var result = await Build(apiMock).GetChildClipsAsync(parentId);

        Assert.Equal(2, result.Count);
        Assert.All(result, r => Assert.Equal(parentId, r.ParentFileId));
        apiMock.Verify(x => x.GetChildClipsAsync(parentId, default), Times.Once);
    }

    [Fact]
    public async Task GetChildClipsAsync_ReturnsEmpty_WhenNoClipsExist()
    {
        var parentId = Guid.NewGuid();
        var apiMock  = ApiMock();
        apiMock.Setup(x => x.GetChildClipsAsync(parentId, default))
               .ReturnsAsync([]);

        var result = await Build(apiMock).GetChildClipsAsync(parentId);

        Assert.Empty(result);
    }

    // ── GetClipPreviewAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task GetClipPreviewAsync_DelegatesToApiAndReturnsBytesAndContentType()
    {
        var fileId      = Guid.NewGuid();
        var bytes       = new byte[] { 0x52, 0x49, 0x46, 0x46 }; // RIFF
        var apiMock     = ApiMock();
        apiMock.Setup(x => x.GetClipPreviewAsync(fileId, 5.0, 10.0, default))
               .ReturnsAsync((bytes, "audio/wav"));

        var result = await Build(apiMock).GetClipPreviewAsync(fileId, 5.0, 10.0);

        Assert.NotNull(result);
        Assert.Equal(bytes,       result!.Value.Data);
        Assert.Equal("audio/wav", result!.Value.ContentType);
        apiMock.Verify(x => x.GetClipPreviewAsync(fileId, 5.0, 10.0, default), Times.Once);
    }

    [Fact]
    public async Task GetClipPreviewAsync_ReturnsNull_WhenApiReturnsNull()
    {
        var fileId  = Guid.NewGuid();
        var apiMock = ApiMock();
        apiMock.Setup(x => x.GetClipPreviewAsync(fileId, 0, 5, default))
               .ReturnsAsync((ValueTuple<byte[], string>?)null);

        var result = await Build(apiMock).GetClipPreviewAsync(fileId, 0, 5);

        Assert.Null(result);
    }
}
