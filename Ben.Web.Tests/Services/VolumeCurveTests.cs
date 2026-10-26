using Ben.Data.WebApi.Services.Audio;
using Ben.Service.Models.Entities;
using NAudio.Wave;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// "Turn parts down": a volume line drawn over the waveform, saved as a new file (phase 7,
/// 10/26/2026). What is saved must be what was heard while drawing it.
/// </summary>
public class VolumeCurveTests
{
    private static readonly VolumePoint[] FullThenDip =
    [
        new(0, 1), new(1, 1), new(1.5, 0.1), new(2.5, 0.1), new(3, 1), new(4, 1),
    ];

    /// <summary>A steady tone, so the level at any moment is just the curve times the tone.</summary>
    private static byte[] ToneWav(double seconds = 4, int rate = 8000)
    {
        using var ms = new MemoryStream();
        using (var w = new WaveFileWriter(ms, new WaveFormat(rate, 16, 1)))
            for (var i = 0; i < seconds * rate; i++)
                w.WriteSample((float)(0.5 * Math.Sin(2 * Math.PI * 440 * i / rate)));
        return ms.ToArray();
    }

    private static double PeakBetween(byte[] wav, double from, double to)
    {
        using var reader = new WaveFileReader(new MemoryStream(wav));
        var provider = reader.ToSampleProvider();
        var all = new float[(int)(reader.SampleCount)];
        provider.Read(all, 0, all.Length);
        var rate = reader.WaveFormat.SampleRate;
        return all[(int)(from * rate)..(int)(to * rate)].Max(Math.Abs);
    }

    [Fact]
    public void The_line_turns_down_only_where_it_was_drawn_down()
    {
        var (bytes, contentType, _) = AudioEditor.Envelope(new MemoryStream(ToneWav()), "audio/wav", FullThenDip);

        Assert.Equal("audio/wav", contentType);
        Assert.InRange(PeakBetween(bytes, 0.1, 0.9), 0.48, 0.51);     // as recorded
        Assert.InRange(PeakBetween(bytes, 1.6, 2.4), 0.04, 0.06);     // a tenth
        Assert.InRange(PeakBetween(bytes, 3.1, 3.9), 0.48, 0.51);     // back up
    }

    /// <summary>
    /// Read the way the player reads it while drawing (envelope.ts, 8.0.3): straight lines between
    /// points, and silence before the first point and after the last.
    /// </summary>
    [Theory]
    [InlineData(0.5, 1.0)]
    [InlineData(1.25, 0.55)]     // halfway down the slope from 1 to 0.1
    [InlineData(2.0, 0.1)]
    [InlineData(2.75, 0.55)]
    public void Between_points_the_volume_moves_in_a_straight_line(double time, double expected)
    {
        var next = 0;
        Assert.Equal(expected, AudioEditor.VolumeAt(FullThenDip, time, 4, ref next), 6);
    }

    [Fact]
    public void Before_the_first_point_and_after_the_last_it_is_silent_as_while_playing()
    {
        VolumePoint[] middleOnly = [new(1, 1), new(3, 1)];
        var next = 0;
        Assert.Equal(0.5, AudioEditor.VolumeAt(middleOnly, 0.5, 4, ref next), 6);   // rising from (0, 0)
        next = 0;
        Assert.Equal(0.5, AudioEditor.VolumeAt(middleOnly, 3.5, 4, ref next), 6);   // falling to (4, 0)
    }

    // ── What the server refuses ───────────────────────────────────────────────

    private static string? Problem(IReadOnlyList<VolumePoint>? points) => AudioRequestLimits.EditProblem(
        new AudioEditRequest(AudioEditOperation.Envelope, null, null, null, null, null, null, false, Guid.NewGuid(), Volume: points));

    [Fact]
    public void A_drawn_line_is_accepted() => Assert.Null(Problem(FullThenDip));

    [Fact]
    public void A_line_needs_two_points() => Assert.Contains("at least two", Problem([new(0, 1)]));

    [Fact]
    public void No_line_at_all_is_refused() => Assert.NotNull(Problem(null));

    [Fact]
    public void A_volume_above_as_recorded_is_refused()
        => Assert.Contains("0 (silent) to 1", Problem([new(0, 1), new(1, 1.5)]));

    [Fact]
    public void Points_out_of_order_are_refused()
        => Assert.Contains("time order", Problem([new(0, 1), new(2, 1), new(1, 1)]));

    [Fact]
    public void A_point_that_is_not_a_number_is_refused()
        => Assert.NotNull(Problem([new(0, 1), new(double.NaN, 1)]));

    [Fact]
    public void Too_many_points_are_refused()
        => Assert.Contains("at most", Problem(Enumerable.Range(0, AudioRequestLimits.MaxVolumePoints + 1).Select(i => new VolumePoint(i, 1)).ToList()));
}
