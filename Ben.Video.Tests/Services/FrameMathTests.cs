using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Turning a time into a frame number, at both ends.
/// </summary>
public sealed class FrameMathTests
{
    [Fact]
    public void The_first_frame_is_frame_one()
    {
        Assert.Equal(1, FrameMath.FrameAt(0, fps: 30, durationSeconds: 10));
    }

    /// <summary>
    /// The counter floored the time and then added one, which is right everywhere except the last
    /// moment: playing to the end of a three hundred frame clip read "F0301 / 0300", a frame that
    /// does not exist (2026-09-05 audit, preview-14).
    /// </summary>
    [Fact]
    public void Sitting_on_the_end_is_the_last_frame_not_one_past_it()
    {
        var total = FrameMath.TotalFrames(durationSeconds: 10, fps: 30);

        Assert.Equal(300, total);
        Assert.Equal(total, FrameMath.FrameAt(10, fps: 30, durationSeconds: 10));
    }

    [Fact]
    public void A_time_past_the_end_still_reads_as_the_last_frame()
    {
        Assert.Equal(300, FrameMath.FrameAt(99, fps: 30, durationSeconds: 10));
    }

    [Theory]
    [InlineData(0.0,    1)]
    [InlineData(0.033,  1)]   // still inside the first frame at 30fps
    [InlineData(0.034,  2)]
    [InlineData(1.0,   31)]
    public void A_frame_lasts_until_the_next_one_starts(double seconds, int expected)
    {
        Assert.Equal(expected, FrameMath.FrameAt(seconds, fps: 30, durationSeconds: 10));
    }

    [Fact]
    public void A_clip_shorter_than_one_frame_still_has_one()
    {
        Assert.Equal(1, FrameMath.TotalFrames(durationSeconds: 0.01, fps: 30));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Nothing_loaded_counts_no_frames(double duration)
    {
        Assert.Equal(0, FrameMath.TotalFrames(duration, fps: 30));
        Assert.Equal(0, FrameMath.FrameAt(0, fps: 30, durationSeconds: duration));
    }

    [Fact]
    public void An_unknown_frame_rate_counts_nothing_rather_than_dividing_by_zero()
    {
        Assert.Equal(0, FrameMath.TotalFrames(10, fps: 0));
        Assert.Equal(0, FrameMath.FrameAt(1, fps: 0, durationSeconds: 10));
        Assert.Equal(0, FrameMath.FrameDuration(0));
    }

    /// <summary>
    /// Frame numbers count from one and times count from zero, so the round trip has to allow for
    /// the offset — getting it wrong puts every step one frame out.
    /// </summary>
    [Theory]
    [InlineData(1,  0.0)]
    [InlineData(2,  1.0 / 30)]
    [InlineData(31, 1.0)]
    public void A_frame_number_maps_back_to_when_it_starts(int frame, double expected)
    {
        Assert.Equal(expected, FrameMath.TimeOfFrame(frame, fps: 30), 6);
    }

    [Fact]
    public void One_frame_at_thirty_is_a_thirtieth_of_a_second()
    {
        Assert.Equal(1.0 / 30, FrameMath.FrameDuration(30), 6);
    }
}
