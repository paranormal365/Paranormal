using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

/// <summary>
/// A chip draws the part of a recording its clip plays.
/// </summary>
/// <remarks>
/// It drew all of it however the clip was trimmed, so a thirty-second excerpt from a three-minute
/// recording showed the whole three minutes squeezed into its chip, and the two halves of a split
/// showed the same picture as each other (2026-09-05 audit, audio-13 and media-11).
/// </remarks>
public sealed class WaveformPeakSlicerTests
{
    private static float[] Peaks(int count) =>
        [.. Enumerable.Range(0, count).Select(i => (float)i)];

    [Fact]
    public void An_untrimmed_clip_draws_the_whole_recording()
    {
        var peaks = Peaks(100);

        Assert.Same(peaks, WaveformPeakSlicer.Slice(peaks, 60, 0, 0));
    }

    [Fact]
    public void A_head_trim_drops_the_beginning()
    {
        var slice = WaveformPeakSlicer.Slice(Peaks(100), 100, 25, 100)!;

        Assert.Equal(75, slice.Length);
        Assert.Equal(25f, slice[0]);
    }

    [Fact]
    public void A_tail_trim_drops_the_end()
    {
        var slice = WaveformPeakSlicer.Slice(Peaks(100), 100, 0, 40)!;

        Assert.Equal(40, slice.Length);
        Assert.Equal(0f, slice[0]);
    }

    /// <summary>
    /// The point of the whole thing: two halves of a split look different from each other.
    /// </summary>
    [Fact]
    public void The_two_halves_of_a_split_draw_different_shapes()
    {
        var peaks = Peaks(100);

        var first  = WaveformPeakSlicer.Slice(peaks, 100, 0, 50)!;
        var second = WaveformPeakSlicer.Slice(peaks, 100, 50, 100)!;

        Assert.NotEqual(first[0], second[0]);
    }

    [Fact]
    public void Peaks_that_have_not_been_decoded_yet_stay_that_way()
        => Assert.Null(WaveformPeakSlicer.Slice(null, 100, 10, 20));

    /// <summary>
    /// A source of unknown length has no fractions to map onto, so the whole array is the honest
    /// answer rather than an arbitrary slice of it.
    /// </summary>
    [Fact]
    public void An_unknown_duration_leaves_the_peaks_alone()
    {
        var peaks = Peaks(100);

        Assert.Same(peaks, WaveformPeakSlicer.Slice(peaks, 0, 10, 20));
    }

    /// <summary>
    /// An end at or before the start means "not trimmed", which is what the clip's own
    /// TrimmedDuration makes of it. The picture and the length have to agree, or a slice would be
    /// stretched across a full-length chip.
    /// </summary>
    [Theory]
    [InlineData(60, 20)]
    [InlineData(25, 0)]
    public void No_real_end_trim_draws_the_whole_recording(double start, double end)
    {
        var peaks = Peaks(100);

        Assert.Same(peaks, WaveformPeakSlicer.Slice(peaks, 100, start, end));
    }

    [Fact]
    public void A_slice_is_never_empty()
    {
        var slice = WaveformPeakSlicer.Slice(Peaks(100), 100, 50.0, 50.001)!;

        Assert.True(slice.Length >= 1);
    }
}
