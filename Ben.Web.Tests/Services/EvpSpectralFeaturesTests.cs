using System.Diagnostics;
using Ben.Data.WebApi.Services.Audio;
using Ben.Data.WebApi.Services.Audio.Scanning;
using Xunit;
using Xunit.Abstractions;

namespace Ben.Web.Tests.Services;

/// <summary>Features 8 and 9 (audio phase 11): spectral flatness and the voiced share of a sound.</summary>
public sealed class EvpSpectralFeaturesTests(ITestOutputHelper output)
{
    private const int Rate = 16_000;

    private static float[] Make(double seconds, Func<double, double> at)
        => Enumerable.Range(0, (int)(seconds * Rate)).Select(i => (float)at(i / (double)Rate)).ToArray();

    /// <summary>A buzz at a speaking voice's pitch: all its power in harmonics, and pitched throughout.</summary>
    [Fact]
    public void A_voice_pitched_buzz_is_tonal_and_voiced()
    {
        var buzz = Make(1, t => Enumerable.Range(1, 20).Sum(h => Math.Sin(2 * Math.PI * 140 * h * t) / h) * 0.2);
        var (flatness, voiced) = EvpSpectralFeatures.Measure(buzz, 300, 3400);
        Assert.InRange(flatness!.Value, 0f, 0.2f);
        Assert.InRange(voiced!.Value, 0.9f, 1f);
    }

    [Fact]
    public void Hiss_is_flat_and_unvoiced()
    {
        var random = new Random(11);
        var hiss = Make(1, _ => (random.NextDouble() * 2 - 1) * 0.2);
        var (flatness, voiced) = EvpSpectralFeatures.Measure(hiss, 300, 3400);
        Assert.InRange(flatness!.Value, 0.4f, 1f);
        Assert.InRange(voiced!.Value, 0f, 0.1f);
    }

    /// <summary>The scannable fixture's "utterance": three voice-range tones rising and falling like syllables.</summary>
    [Fact]
    public void The_fixtures_utterance_is_mostly_voiced()
    {
        var utterance = Make(0.8, t => (0.5 + 0.5 * Math.Sin(2 * Math.PI * 4 * t))
            * (Math.Sin(2 * Math.PI * 500 * t) + Math.Sin(2 * Math.PI * 1500 * t) + Math.Sin(2 * Math.PI * 2500 * t)) / 3 * 0.03);
        var (_, voiced) = EvpSpectralFeatures.Measure(utterance, 300, 3400);
        Assert.True(voiced > 0.5f, $"voiced share {voiced}");
    }

    [Fact]
    public void Silence_has_nothing_to_measure()
    {
        Assert.Equal((null, null), EvpSpectralFeatures.Measure(new float[Rate], 300, 3400));
        Assert.Equal((null, null), EvpSpectralFeatures.Measure(new float[100], 300, 3400));
    }

    /// <summary>The sound inside its padding, wherever the padding was clamped.</summary>
    [Fact]
    public void The_event_is_found_inside_its_padding_at_either_end()
    {
        EvpCandidate C(double s, double e, double eventSeconds) => new(s, e, 50, new EvpFeatures(0, 0, eventSeconds, 0, 0, 0, 0));
        Assert.Equal((10.4, 11.4), Round(EvpScanService.EventSpan(C(10, 11.8, 1), 0.4, 60)));
        Assert.Equal((0.2, 1.2), Round(EvpScanService.EventSpan(C(0, 1.6, 1), 0.4, 60)));      // clamped at the start
        Assert.Equal((58.4, 59.4), Round(EvpScanService.EventSpan(C(58, 59.4, 1), 0.4, 59.4))); // clamped at the end
        Assert.Equal((0.0, 1.0), Round(EvpScanService.EventSpan(C(0, 1.0, 1), 0.4, 1.0)));     // both

        static (double, double) Round((double A, double B) x) => (Math.Round(x.A, 3), Math.Round(x.B, 3));
    }

    /// <summary>What it costs, for the README: measured here rather than guessed.</summary>
    [Fact]
    public void Cost_per_minute_of_candidate_audio_is_reported()
    {
        var random = new Random(3);
        var minute = Make(60, t => Math.Sin(2 * Math.PI * 180 * t) * 0.1 + (random.NextDouble() - 0.5) * 0.05);
        EvpSpectralFeatures.Measure(minute.AsSpan(0, Rate), 300, 3400);   // warm up
        var clock = Stopwatch.StartNew();
        EvpSpectralFeatures.Measure(minute, 300, 3400);
        output.WriteLine($"features 8-9: {clock.ElapsedMilliseconds} ms per minute of candidate audio");
    }
}
