namespace Ben.Video.Editor.Services;

/// <summary>
/// Where a slideshow's playhead is, MEASURED rather than counted.
/// </summary>
/// <remarks>
/// <para>
/// An image-only timeline has no <c>&lt;video&gt;</c> element to keep time, so the page keeps it.
/// That loop used to add a fixed frame's worth per pass — <c>CurrentTime + 1/fps</c> — which
/// counts PASSES rather than measuring time. A pass is a delay plus an image swap plus an event
/// callback plus a full render pushed down the circuit, so at 30fps it asks for 33ms and takes
/// considerably more. The slideshow therefore played in slow motion, the clock read low the whole
/// way, and a title or callout timed to a moment arrived late with it.
/// </para>
/// <para>
/// Same fault, same fix as the Field Kit replay on both the web and the phone (2026-09-11).
/// Pure and out of the component so it can be checked without a scheduler, the scheduler being
/// exactly what cannot be relied on.
/// </para>
/// </remarks>
public static class SlideshowClock
{
    /// <summary>
    /// The playhead <paramref name="elapsed"/> after <paramref name="from"/>, never past
    /// <paramref name="totalDuration"/>.
    /// </summary>
    public static double At(double from, TimeSpan elapsed, double totalDuration)
    {
        var next = from + elapsed.TotalSeconds;
        return next > totalDuration ? totalDuration : next;
    }
}
