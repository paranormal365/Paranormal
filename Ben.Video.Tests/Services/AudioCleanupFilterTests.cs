using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Lifting a voice out of a field recording's hiss, and evening out its level.
/// </summary>
/// <remarks>
/// The editor had no audio effects at all, and a recording made in a house at two in the morning is
/// mostly room tone and the recorder's own noise floor (2026-09-05 audit, audio-25).
/// </remarks>
public sealed class AudioCleanupFilterTests
{
    [Fact]
    public void Nothing_asked_for_adds_nothing()
        => Assert.Null(AudioCleanupFilter.Build(0, false));

    [Fact]
    public void The_dial_at_zero_leaves_the_recording_alone()
        => Assert.Null(AudioCleanupFilter.NoiseReduction(0));

    [Fact]
    public void Turning_it_up_reduces_more()
    {
        var gentle = Reduction(AudioCleanupFilter.NoiseReduction(0.1)!);
        var heavy  = Reduction(AudioCleanupFilter.NoiseReduction(1.0)!);

        Assert.True(heavy > gentle);
    }

    /// <summary>
    /// The dial covers the part of the range that helps. Past about 30 dB the artefacts on speech
    /// are worse than the noise, so the heaviest setting stops where the result is worth having.
    /// </summary>
    [Fact]
    public void The_heaviest_setting_still_leaves_speech_alone()
    {
        var db = Reduction(AudioCleanupFilter.NoiseReduction(1.0)!);

        Assert.Equal(AudioCleanupFilter.MaximumReductionDb, db, precision: 1);
    }

    [Fact]
    public void The_gentlest_setting_still_does_something()
    {
        var db = Reduction(AudioCleanupFilter.NoiseReduction(0.0001)!);

        Assert.True(db >= AudioCleanupFilter.MinimumReductionDb);
    }

    [Fact]
    public void A_dial_past_the_end_is_treated_as_the_end()
    {
        Assert.Equal(
            AudioCleanupFilter.NoiseReduction(1.0),
            AudioCleanupFilter.NoiseReduction(5.0));
    }

    [Fact]
    public void Levelling_is_off_until_it_is_asked_for()
    {
        Assert.Null(AudioCleanupFilter.Levelling(false));
        Assert.Contains("loudnorm", AudioCleanupFilter.Levelling(true));
    }

    /// <summary>
    /// Noise comes out before the level is measured. Measuring first would level to the hiss.
    /// </summary>
    [Fact]
    public void The_hiss_comes_out_before_the_level_is_measured()
    {
        var chain = AudioCleanupFilter.Build(0.5, true)!;

        Assert.True(chain.IndexOf("afftdn", StringComparison.Ordinal)
                  < chain.IndexOf("loudnorm", StringComparison.Ordinal));
    }

    private static double Reduction(string clause) =>
        double.Parse(clause.Split("nr=")[1].Split(':')[0],
                     System.Globalization.CultureInfo.InvariantCulture);
}
