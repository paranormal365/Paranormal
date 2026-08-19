using Ben.Web.Website.Library.Manage.Audio;
using Xunit;

namespace Ben.Web.Tests.Services;

public class AudioFormatUtilsTests
{
    // ── IsAudioContentType ────────────────────────────────────────────────────

    [Theory]
    [InlineData("audio/mpeg")]
    [InlineData("audio/wav")]
    [InlineData("audio/ogg")]
    [InlineData("audio/flac")]
    [InlineData("audio/aac")]
    [InlineData("audio/mp4")]
    [InlineData("audio/webm")]
    [InlineData("audio/opus")]
    [InlineData("AUDIO/MPEG")]          // case-insensitive
    public void IsAudioContentType_ReturnsTrue_ForAudioMimeTypes(string contentType)
    {
        Assert.True(AudioFormatUtils.IsAudioContentType(contentType));
    }

    [Theory]
    [InlineData("image/png")]
    [InlineData("image/jpeg")]
    [InlineData("video/mp4")]
    [InlineData("application/pdf")]
    [InlineData("text/plain")]
    [InlineData("")]
    public void IsAudioContentType_ReturnsFalse_ForNonAudioMimeTypes(string contentType)
    {
        Assert.False(AudioFormatUtils.IsAudioContentType(contentType));
    }

    [Fact]
    public void IsAudioContentType_ReturnsFalse_ForNullInput()
    {
        Assert.False(AudioFormatUtils.IsAudioContentType(null));
    }

    // ── FormatTime ────────────────────────────────────────────────────────────

    [Fact]
    public void FormatTime_Zero_Returns_0m00s0()
    {
        Assert.Equal("0:00.0", AudioFormatUtils.FormatTime(0));
    }

    [Fact]
    public void FormatTime_Under60Seconds_ShowsMinutesAndSeconds()
    {
        // 45.3 seconds → 0:45.3
        var result = AudioFormatUtils.FormatTime(45.3);
        Assert.StartsWith("0:", result);
        Assert.Contains("45", result);
    }

    [Fact]
    public void FormatTime_90Seconds_ShowsOneMinute()
    {
        // 90 seconds → 1:30.0
        var result = AudioFormatUtils.FormatTime(90.0);
        Assert.StartsWith("1:", result);
    }

    [Fact]
    public void FormatTime_3600Seconds_ShowsHoursFormat()
    {
        // Exactly 1 hour
        var result = AudioFormatUtils.FormatTime(3600.0);
        Assert.StartsWith("1:", result);   // h:mm:ss.f format
        Assert.Equal("1:00:00.0", result);
    }

    [Fact]
    public void FormatTime_3661Seconds_ShowsHoursMinutesSeconds()
    {
        // 1h 1m 1s
        var result = AudioFormatUtils.FormatTime(3661.0);
        Assert.Equal("1:01:01.0", result);
    }

    [Fact]
    public void FormatTime_IncludesTenthOfSecond()
    {
        // 5.7 seconds → 0:05.7
        var result = AudioFormatUtils.FormatTime(5.7);
        Assert.EndsWith(".7", result);
    }

    [Fact]
    public void FormatTime_JustUnder3600_UsesMinuteFormat()
    {
        // 3599.9 < 3600 → should still use m:ss.f (not hours)
        var result = AudioFormatUtils.FormatTime(3599.9);
        Assert.DoesNotContain("1:00", result);  // no hours component
    }

    // ── FormatSize ────────────────────────────────────────────────────────────

    [Fact]
    public void FormatSize_LessThan1KB_ShowsBytes()
    {
        Assert.Equal("512 bytes", AudioFormatUtils.FormatSize(512));
    }

    [Fact]
    public void FormatSize_ExactlyOneKB_ShowsKB()
    {
        var result = AudioFormatUtils.FormatSize(1024);
        Assert.Contains("KB", result);
        Assert.Contains("1,024 bytes", result);
    }

    [Fact]
    public void FormatSize_MegabyteRange_ShowsMB()
    {
        var result = AudioFormatUtils.FormatSize(3 * 1_048_576); // 3 MB
        Assert.Contains("MB", result);
        Assert.Contains("3.00 MB", result);
    }

    [Fact]
    public void FormatSize_GigabyteRange_ShowsGB()
    {
        var result = AudioFormatUtils.FormatSize(2 * 1_073_741_824L); // 2 GB
        Assert.Contains("GB", result);
        Assert.Contains("2.00 GB", result);
    }

    [Fact]
    public void FormatSize_AlwaysIncludesRawByteCount_ForLargerSizes()
    {
        var result = AudioFormatUtils.FormatSize(1_500_000);
        Assert.Contains("bytes", result);
        Assert.Contains("1,500,000", result);
    }

    [Fact]
    public void FormatSize_ZeroBytes_ShowsZeroBytes()
    {
        Assert.Equal("0 bytes", AudioFormatUtils.FormatSize(0));
    }

    // ── FormatSizeCompact ─────────────────────────────────────────────────────

    [Fact]
    public void FormatSizeCompact_SmallFile_ShowsKB()
    {
        var result = AudioFormatUtils.FormatSizeCompact(2048);
        Assert.Equal("2 KB", result);
    }

    [Fact]
    public void FormatSizeCompact_LargeFile_ShowsMB()
    {
        var result = AudioFormatUtils.FormatSizeCompact(5_242_880); // 5 MB
        Assert.Equal("5.0 MB", result);
    }

    [Fact]
    public void FormatSizeCompact_DoesNotIncludeRawByteCount()
    {
        var result = AudioFormatUtils.FormatSizeCompact(1_048_576);
        Assert.DoesNotContain("bytes", result);
        Assert.DoesNotContain("(", result);
    }
}
