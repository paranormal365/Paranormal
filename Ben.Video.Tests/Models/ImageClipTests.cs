using Ben.Video.Editor.Models;

namespace Ben.Video.Tests.Models;

public sealed class ImageClipTests
{
    // ── Defaults ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Default_IdIsNotEmpty()
    {
        var clip = new ImageClip();
        Assert.NotEqual(Guid.Empty, clip.Id);
    }

    [Fact]
    public void Default_DurationIsZero()
    {
        var clip = new ImageClip();
        Assert.Equal(0, clip.Duration);
    }

    [Fact]
    public void Default_WidthAndHeightAreZero()
    {
        var clip = new ImageClip();
        Assert.Equal(0, clip.Width);
        Assert.Equal(0, clip.Height);
    }

    [Fact]
    public void Default_MemFsNameIsNull()
    {
        var clip = new ImageClip();
        Assert.Null(clip.MemFsName);
    }

    [Fact]
    public void Default_ThumbnailUrlIsNull()
    {
        var clip = new ImageClip();
        Assert.Null(clip.ThumbnailUrl);
    }

    [Fact]
    public void Default_EffectsIsNeutral()
    {
        var clip = new ImageClip();
        Assert.True(clip.Effects.IsNeutral);
    }

    [Fact]
    public void Default_IsMediaMissingIsFalse()
    {
        var clip = new ImageClip();
        Assert.False(clip.IsMediaMissing);
    }

    // ── Property assignment ────────────────────────────────────────────────────────

    [Fact]
    public void Properties_CanBeSetViaInitializer()
    {
        var clip = new ImageClip
        {
            Name         = "photo.png",
            MemFsName    = "img_abc_photo.png",
            Duration     = 5.0,
            Width        = 1920,
            Height       = 1080,
            ThumbnailUrl = "blob:http://localhost/xyz",
            Order        = 3,
        };

        Assert.Equal("photo.png",              clip.Name);
        Assert.Equal("img_abc_photo.png",      clip.MemFsName);
        Assert.Equal(5.0,                      clip.Duration);
        Assert.Equal(1920,                     clip.Width);
        Assert.Equal(1080,                     clip.Height);
        Assert.Equal("blob:http://localhost/xyz", clip.ThumbnailUrl);
        Assert.Equal(3,                        clip.Order);
    }

    // ── Inheritance ────────────────────────────────────────────────────────────────

    [Fact]
    public void ImageClip_InheritsFromTrackItem()
    {
        var clip = new ImageClip();
        Assert.IsAssignableFrom<TrackItem>(clip);
    }

    [Fact]
    public void ImageClip_HasUniqueIdPerInstance()
    {
        var a = new ImageClip();
        var b = new ImageClip();
        Assert.NotEqual(a.Id, b.Id);
    }
}
