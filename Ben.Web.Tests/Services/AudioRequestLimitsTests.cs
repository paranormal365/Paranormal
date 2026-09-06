using Ben.Data.WebApi.Services.Audio;
using Ben.Service.Models.Entities;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The numbers the audio endpoints accept, and the one that used to walk straight through.
/// </summary>
/// <remarks>
/// The controller tests prove each endpoint asks these questions; this proves the answers are
/// right, and states in one place why <c>NaN</c> needed a check of its own.
/// </remarks>
public sealed class AudioRequestLimitsTests
{
    private static AudioEditRequest Edit(
        AudioEditOperation op, double? start = null, double? end = null, double? gain = null,
        double? fadeIn = null, double? fadeOut = null, double? speed = null, double? pitch = null,
        string? label = null)
        => new(op, start, end, gain, fadeIn, fadeOut, label, false, Guid.NewGuid(), speed, pitch);

    /// <summary>
    /// Why the range checks could not simply be written as comparisons.
    /// </summary>
    /// <remarks>
    /// Every comparison against <c>NaN</c> is false, including both halves of a range test, so
    /// "reject anything outside [-60, 24]" rejects nothing. It reaches the sample loop, multiplies
    /// every sample into <c>NaN</c>, and writes as zero — a file of silence, answered 201.
    /// </remarks>
    [Fact]
    public void NaN_passes_a_range_check_written_the_obvious_way()
    {
        Assert.False(double.NaN < -60);
        Assert.False(double.NaN > 24);

        Assert.False(AudioRequestLimits.IsFinite(double.NaN));
        Assert.False(AudioRequestLimits.IsFinite(double.PositiveInfinity));
        Assert.True(AudioRequestLimits.IsFinite(null));   // "not given" is not "not a number"
        Assert.True(AudioRequestLimits.IsFinite(0));
    }

    [Theory]
    [InlineData(0.001)]
    [InlineData(0.24)]
    [InlineData(4.1)]
    [InlineData(double.NaN)]
    public void A_speed_outside_the_range_is_refused(double ratio)
        => Assert.NotNull(AudioRequestLimits.EditProblem(Edit(AudioEditOperation.Speed, speed: ratio)));

    [Theory]
    [InlineData(0.25)]
    [InlineData(1.0)]
    [InlineData(4.0)]
    public void A_speed_inside_the_range_is_allowed(double ratio)
        => Assert.Null(AudioRequestLimits.EditProblem(Edit(AudioEditOperation.Speed, speed: ratio)));

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(-61)]
    [InlineData(25)]
    public void A_gain_outside_the_range_is_refused(double gain)
        => Assert.NotNull(AudioRequestLimits.EditProblem(Edit(AudioEditOperation.Gain, gain: gain)));

    [Fact]
    public void A_gain_of_zero_is_allowed_and_is_not_read_as_missing()
        => Assert.Null(AudioRequestLimits.EditProblem(Edit(AudioEditOperation.Gain, gain: 0)));

    [Fact]
    public void A_region_that_ends_before_it_starts_is_refused()
        => Assert.NotNull(AudioRequestLimits.EditProblem(Edit(AudioEditOperation.Cut, start: 9, end: 2)));

    [Fact]
    public void A_region_before_the_recording_starts_is_refused()
        => Assert.NotNull(AudioRequestLimits.EditProblem(Edit(AudioEditOperation.Cut, start: -1, end: 2)));

    [Fact]
    public void A_label_longer_than_the_column_is_refused_and_says_how_long_it_may_be()
    {
        var problem = AudioRequestLimits.LabelProblem(new string('x', 201));

        Assert.NotNull(problem);
        Assert.Contains("200", problem);
    }

    [Fact]
    public void A_label_at_the_limit_is_allowed()
        => Assert.Null(AudioRequestLimits.LabelProblem(new string('x', 200)));

    // ── Mixes ─────────────────────────────────────────────────────────────────

    private static MixTrackExportInput Track(double offset = 0, double gain = 0, double pan = 0)
        => new(Guid.NewGuid(), offset, gain, pan, false, false);

    [Fact]
    public void A_mix_with_no_tracks_is_refused()
        => Assert.NotNull(AudioRequestLimits.MixProblem([]));

    [Fact]
    public void A_mix_of_eight_tracks_is_allowed_and_a_ninth_is_not()
    {
        var eight = Enumerable.Range(0, 8).Select(_ => Track()).ToList();

        Assert.Null(AudioRequestLimits.MixProblem(eight));
        Assert.NotNull(AudioRequestLimits.MixProblem([.. eight, Track()]));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3601)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_track_placed_outside_the_mix_is_refused(double offset)
        => Assert.NotNull(AudioRequestLimits.MixProblem([Track(offset: offset)]));

    [Fact]
    public void A_pan_outside_left_to_right_is_refused()
    {
        Assert.NotNull(AudioRequestLimits.MixProblem([Track(pan: -1.5)]));
        Assert.NotNull(AudioRequestLimits.MixProblem([Track(pan: 2)]));
        Assert.Null(AudioRequestLimits.MixProblem([Track(pan: -1)]));
        Assert.Null(AudioRequestLimits.MixProblem([Track(pan: 1)]));
    }

    // ── Marker spans ──────────────────────────────────────────────────────────

    [Fact]
    public void A_point_marker_needs_no_end()
        => Assert.Null(AudioRequestLimits.MarkerSpanProblem(12.0, null, "Whisper?"));

    [Fact]
    public void A_span_that_ends_before_it_starts_is_refused()
        => Assert.NotNull(AudioRequestLimits.MarkerSpanProblem(12.0, 4.0, "Whisper?"));

    [Fact]
    public void A_marker_before_the_recording_starts_is_refused()
        => Assert.NotNull(AudioRequestLimits.MarkerSpanProblem(-0.5, null, "Whisper?"));
}
