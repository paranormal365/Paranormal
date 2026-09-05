using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Deciding whether a picked file is video, audio or a picture.
/// </summary>
/// <remarks>
/// Two extension lists used to decide, and anything they did not name took the video path — so a
/// .heic from a phone, a .tiff from a scanner or a .caf recording became a video clip with no
/// dimensions and an empty filmstrip, which is a confusing way to be told the format is not
/// supported (2026-09-05 audit, media-panel-8).
/// </remarks>
public sealed class MediaKindRouterTests
{
    [Theory]
    [InlineData("clip.mp4")]
    [InlineData("clip.mov")]
    [InlineData("clip.mkv")]
    [InlineData("no-extension")]
    public void Video_is_the_fallback(string name)
        => Assert.Equal(MediaKind.Video, MediaKindRouter.Decide(name));

    [Theory]
    [InlineData("song.mp3")]
    [InlineData("evp.m4a")]
    [InlineData("session.wav")]
    [InlineData("field.aiff")]
    [InlineData("iphone-memo.caf")]
    [InlineData("voice.amr")]
    [InlineData("stems.mka")]
    public void Audio_is_recognised_including_the_formats_the_old_list_missed(string name)
        => Assert.Equal(MediaKind.Audio, MediaKindRouter.Decide(name));

    [Theory]
    [InlineData("photo.jpg")]
    [InlineData("shot.png")]
    [InlineData("logo.svg")]
    [InlineData("phone.heic")]
    [InlineData("scan.tiff")]
    [InlineData("modern.avif")]
    [InlineData("web.jfif")]
    public void Pictures_are_recognised_including_the_formats_the_old_list_missed(string name)
        => Assert.Equal(MediaKind.Image, MediaKindRouter.Decide(name));

    /// <summary>
    /// The browser knows what the operating system thinks the file is; a name is only a hint.
    /// </summary>
    [Theory]
    [InlineData("recording", "audio/mp4", MediaKind.Audio)]
    [InlineData("picture",   "image/png", MediaKind.Image)]
    [InlineData("movie",     "video/mp4", MediaKind.Video)]
    public void The_browsers_own_type_wins(string name, string contentType, MediaKind expected)
        => Assert.Equal(expected, MediaKindRouter.Decide(name, contentType));

    /// <summary>
    /// A misnamed file is what its content says, not what its extension claims — the case the
    /// extension lists could never get right.
    /// </summary>
    [Fact]
    public void A_misnamed_file_follows_its_content_type()
        => Assert.Equal(MediaKind.Audio, MediaKindRouter.Decide("mislabelled.mp4", "audio/mpeg"));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("application/octet-stream")]
    public void An_unhelpful_content_type_falls_back_to_the_extension(string? contentType)
        => Assert.Equal(MediaKind.Image, MediaKindRouter.Decide("photo.png", contentType));

    [Theory]
    [InlineData("a.JPG",  "image/jpeg")]
    [InlineData("a.heic", "image/heic")]
    [InlineData("a.svg",  "image/svg+xml")]
    [InlineData("a.tiff", "image/tiff")]
    [InlineData("a.odd",  "image/png")]
    public void Image_mime_types_cover_what_the_editor_accepts(string name, string expected)
        => Assert.Equal(expected, MediaKindRouter.ImageMimeType(name));

    [Fact]
    public void Case_does_not_matter()
    {
        Assert.Equal(MediaKind.Audio, MediaKindRouter.Decide("SONG.MP3"));
        Assert.Equal(MediaKind.Image, MediaKindRouter.Decide("PHOTO.PNG"));
    }
}
