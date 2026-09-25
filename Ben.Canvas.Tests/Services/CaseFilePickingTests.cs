using Ben.Canvas.Core.Blocks;
using Ben.Canvas.Core.Model;
using Ben.Canvas.Editor.Services;

namespace Ben.Canvas.Tests.Services;

/// <summary>
/// Which block a file the case already holds becomes when somebody picks it onto a board.
/// </summary>
/// <remarks>
/// A dropped file is judged by its first bytes; a picked one is a row on the server, so all there is
/// to judge by is a name and a declared content type — and browsers disagree about both. The rule
/// reads each independently, and anything unrecognised stays a file chip rather than becoming a
/// player with nothing it can decode.
/// </remarks>
public sealed class CaseFileBlockKindTests
{
    [Theory]
    [InlineData("ghost.jpg", "image/jpeg")]
    [InlineData("ghost.JPEG", "application/octet-stream")]   // the name carries it
    [InlineData("scan", "image/png")]                        // the type carries it
    [InlineData("orb.webp", null)]
    [InlineData("IMG_4021.heic", "image/heic")]
    public void A_picture_becomes_a_picture(string name, string? mime) =>
        Assert.Equal(CanvasNodeType.Image, CaseFileBlockKind.For(name, mime));

    [Theory]
    [InlineData("evp.m4a", "audio/mp4")]
    [InlineData("session.m4a", "application/octet-stream")]
    [InlineData("knocks", "audio/wav")]
    [InlineData("hiss.flac", null)]
    public void A_recording_becomes_a_player(string name, string? mime) =>
        Assert.Equal(CanvasNodeType.Audio, CaseFileBlockKind.For(name, mime));

    [Theory]
    [InlineData("hallway.mov", "video/quicktime")]
    [InlineData("clip.mp4", null)]
    [InlineData("stairs", "video/webm")]
    public void A_film_becomes_a_screen(string name, string? mime) =>
        Assert.Equal(CanvasNodeType.Video, CaseFileBlockKind.For(name, mime));

    [Theory]
    [InlineData("report.pdf", "application/pdf")]
    [InlineData("readings.csv", "text/csv")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Everything_else_stays_a_file(string? name, string? mime) =>
        Assert.Equal(CanvasNodeType.File, CaseFileBlockKind.For(name, mime));
}

/// <summary>
/// A picked file points at the case's copy and carries no copy of its own.
/// </summary>
/// <remarks>
/// <b>The whole reason the picker exists.</b> Ben, 2026-09-16: "files can be picked from the case
/// files". A block that came away with an AssetId as well would be uploaded again by the next save
/// (CanvasServerSession.UploadFilesAsync uploads anything with an AssetId and no UploadFileId), so
/// the case would end up holding the same photograph twice and the account would be charged for
/// both — which is the problem picking was meant to solve.
/// </remarks>
public sealed class CaseFileBlocksTests
{
    private static readonly CanvasCaseFile Photo =
        new(Guid.NewGuid(), "back-stairs.jpg", "image/jpeg", 482_112, "Taken from the landing");

    private static readonly CanvasCaseFile Recording =
        new(Guid.NewGuid(), "evp-0214.m4a", "audio/mp4", 1_204_992, "  ");

    [Fact]
    public void A_picture_points_at_the_case_copy_and_keeps_nothing_on_the_device()
    {
        var data = (ImageData)CaseFileBlocks.Fill(new ImageData { AssetId = Guid.NewGuid() }, Photo)!;

        Assert.Equal(Photo.UploadFileId, data.UploadFileId);
        Assert.Null(data.AssetId);
        Assert.Equal("Taken from the landing", data.Caption);
    }

    [Fact]
    public void A_recording_carries_its_name_size_and_type()
    {
        var data = (AudioData)CaseFileBlocks.Fill(new AudioData { AssetId = Guid.NewGuid() }, Recording)!;

        Assert.Equal(Recording.UploadFileId, data.UploadFileId);
        Assert.Null(data.AssetId);
        Assert.Equal("evp-0214.m4a", data.FileName);
        Assert.Equal(1_204_992, data.Size);
        Assert.Equal("audio/mp4", data.ContentType);
        Assert.Null(data.Caption);   // a description of nothing but spaces is no caption
    }

    [Fact]
    public void A_film_carries_the_same()
    {
        var file = new CanvasCaseFile(Guid.NewGuid(), "hallway.mov", "video/quicktime", 9_000_001, null);
        var data = (VideoData)CaseFileBlocks.Fill(new VideoData(), file)!;

        Assert.Equal(file.UploadFileId, data.UploadFileId);
        Assert.Null(data.AssetId);
        Assert.Equal("hallway.mov", data.FileName);
        Assert.Equal("video/quicktime", data.ContentType);
    }

    [Fact]
    public void A_chip_carries_the_same()
    {
        var file = new CanvasCaseFile(Guid.NewGuid(), "report.pdf", "application/pdf", 12_345, null);
        var data = (FileData)CaseFileBlocks.Fill(new FileData { AssetId = Guid.NewGuid() }, file)!;

        Assert.Equal(file.UploadFileId, data.UploadFileId);
        Assert.Null(data.AssetId);
        Assert.Equal("report.pdf", data.FileName);
        Assert.Equal(12_345, data.Size);
    }

    [Fact]
    public void A_block_that_cannot_show_a_file_is_left_alone()
    {
        Assert.Null(CaseFileBlocks.Fill(new TextData(), Photo));
        Assert.Null(CaseFileBlocks.Fill(new CardData(), Photo));
    }
}
