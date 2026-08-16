using Ben.Video.Sidecar.Jobs;

namespace Ben.Video.Sidecar.Tests;

public sealed class ProgressParserTests
{
    [Fact]
    public void TryParsePercent_OutTimeMs_ComputesPercent()
    {
        // 5,000,000 microseconds = 5s elapsed of a 10s total = 50%.
        var pct = ProgressParser.TryParsePercent("out_time_ms=5000000", totalDurationSeconds: 10);
        Assert.Equal(50, pct);
    }

    [Fact]
    public void TryParsePercent_ClassicTimeFormat_ComputesPercent()
    {
        // What Ben.Video.Sidecar.FakeFfmpeg actually emits.
        var pct = ProgressParser.TryParsePercent(
            "frame=2 fps=30.0 q=28.0 size=100kB time=00:00:01.00 bitrate=800.0kbits/s speed=1.0x",
            totalDurationSeconds: 2);
        Assert.Equal(50, pct);
    }

    [Fact]
    public void TryParsePercent_NeverExceeds99_EvenAtOrPastTotalDuration()
    {
        var atTotal = ProgressParser.TryParsePercent("out_time_ms=10000000", totalDurationSeconds: 10);
        var pastTotal = ProgressParser.TryParsePercent("out_time_ms=99000000", totalDurationSeconds: 10);
        Assert.Equal(99, atTotal);
        Assert.Equal(99, pastTotal);
    }

    [Fact]
    public void TryParsePercent_NeverNegative()
    {
        var pct = ProgressParser.TryParsePercent("out_time_ms=0", totalDurationSeconds: 10);
        Assert.Equal(0, pct);
    }

    [Fact]
    public void TryParsePercent_UnrelatedLine_ReturnsNull()
    {
        var pct = ProgressParser.TryParsePercent("frame=1 fps=0.0 q=0.0 size=0kB", totalDurationSeconds: 10);
        Assert.Null(pct);
    }

    [Fact]
    public void TryParsePercent_ZeroOrNegativeDuration_ReturnsNull()
    {
        Assert.Null(ProgressParser.TryParsePercent("out_time_ms=1000000", totalDurationSeconds: 0));
        Assert.Null(ProgressParser.TryParsePercent("out_time_ms=1000000", totalDurationSeconds: -5));
    }

    [Fact]
    public void TryParsePercent_ClassicTimeWithHours_ComputesCorrectly()
    {
        // 1h = 3600s elapsed of a 7200s (2h) total = 50%.
        var pct = ProgressParser.TryParsePercent("time=01:00:00.00", totalDurationSeconds: 7200);
        Assert.Equal(50, pct);
    }
}
