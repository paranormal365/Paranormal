using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

/// <summary>
/// An image-only timeline has no video element to keep time, so the preview keeps it — and it
/// must MEASURE the time rather than count its own passes.
/// </summary>
/// <remarks>
/// The loop asks for a frame's worth of delay and then renders, swaps the image and raises an
/// event, so each pass takes considerably longer than the frame it stands for. Adding a fixed
/// <c>1/fps</c> per pass therefore played the slideshow in slow motion and read the clock low the
/// whole way, which anything timed off the current time — a title, a callout — inherited. Found
/// on 2026-09-11 alongside the same fault in the Field Kit replay on both the web and the phone.
/// The source guard that keeps all three honest lives in <c>ReplayClockTests</c> over in
/// <c>Ben.Web.Tests</c>, which already scans this project.
/// </remarks>
public sealed class SlideshowClockTests
{
    [Fact]
    public void The_playhead_is_measured_not_counted()
    {
        // Half a second of real time is half a second of the slideshow, however many passes ran.
        Assert.Equal(2.5, SlideshowClock.At(2, TimeSpan.FromMilliseconds(500), totalDuration: 60), 3);

        // One slow pass covers exactly what several quick ones would have.
        Assert.Equal(3.0, SlideshowClock.At(2, TimeSpan.FromSeconds(1), totalDuration: 60), 3);
    }

    [Fact]
    public void The_playhead_never_runs_past_the_end_of_the_timeline()
    {
        // A starved loop can wake long after the last picture; the timeline has no such moment.
        Assert.Equal(10, SlideshowClock.At(2, TimeSpan.FromMinutes(1), totalDuration: 10), 3);
    }
}
