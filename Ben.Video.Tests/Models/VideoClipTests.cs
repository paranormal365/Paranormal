using Ben.Video.Editor.Models;

namespace Ben.Video.Tests.Models;

public sealed class VideoClipTests
{
    [Fact]
    public void NewVideoClip_HasUniqueId()
    {
        var a = new VideoClip();
        var b = new VideoClip();

        Assert.NotEqual(a.Id, b.Id);
    }

    [Fact]
    public void TrimmedDuration_WhenTrimsSet_ReturnsCorrectValue()
    {
        var clip = new VideoClip { StartTrim = 2.0, EndTrim = 7.0, Duration = 10.0 };

        Assert.Equal(5.0, clip.TrimmedDuration);
    }

    [Fact]
    public void TrimmedDuration_WhenNoTrims_ReturnsDuration()
    {
        var clip = new VideoClip { Duration = 10.0 };

        Assert.Equal(10.0, clip.TrimmedDuration);
    }

    [Fact]
    public void TrimmedDuration_WhenEndTrimEqualsStartTrim_ReturnsDuration()
    {
        var clip = new VideoClip { StartTrim = 5.0, EndTrim = 5.0, Duration = 10.0 };

        Assert.Equal(10.0, clip.TrimmedDuration);
    }
}
