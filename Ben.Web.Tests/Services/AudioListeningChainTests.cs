using Ben.Web.Website.Library.Manage.Audio;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Remembering how somebody set the editor up to hear a recording.
/// </summary>
/// <remarks>
/// The equaliser, the filters, the compressor and the noise gate — fourteen numbers that had no
/// home, so every one of them was reset on every open. Somebody working a long recording finds a
/// filter setting that lets them hear a whisper, closes the editor, and has to find it again
/// (2026-09-06 audio walk, finding L; the half phase 5a could not do without a column).
/// </remarks>
public sealed class AudioListeningChainTests
{
    [Fact]
    public void A_recording_nobody_has_set_up_is_flat_with_everything_off()
    {
        var chain = AudioListeningChain.Default;

        Assert.Equal(AudioListeningChain.BandCount, chain.EqGains.Count);
        Assert.All(chain.EqGains, g => Assert.Equal(0, g));
        Assert.False(chain.HighPassOn);
        Assert.False(chain.LowPassOn);
        Assert.False(chain.CompressorOn);
        Assert.False(chain.NoiseGateOn);
        Assert.False(chain.IsAnythingOn);
    }

    [Fact]
    public void A_saved_chain_comes_back_as_it_was_left()
    {
        var chain = new AudioListeningChain
        {
            EqGains               = [3, -2, 0, 0, 6, 0, 0, -4, 0, 1],
            HighPassOn            = true,
            HighPassHz            = 220,
            LowPassOn             = true,
            LowPassHz             = 8_000,
            CompressorOn          = true,
            CompressorThresholdDb = -18,
            CompressorRatio       = 6,
            NoiseGateOn           = true,
            NoiseGateThresholdDb  = -55,
            NoiseGateAttack       = 0.02,
            NoiseGateRelease      = 0.3,
            SilenceThresholdDb    = -52,
        };

        var round = AudioListeningChain.FromJson(chain.ToJson());

        Assert.Equal(chain.EqGains, round.EqGains);
        Assert.Equal(chain.HighPassHz, round.HighPassHz);
        Assert.Equal(chain.LowPassHz, round.LowPassHz);
        Assert.Equal(chain.CompressorThresholdDb, round.CompressorThresholdDb);
        Assert.Equal(chain.CompressorRatio, round.CompressorRatio);
        Assert.Equal(chain.NoiseGateThresholdDb, round.NoiseGateThresholdDb);
        Assert.Equal(chain.NoiseGateAttack, round.NoiseGateAttack);
        Assert.Equal(chain.NoiseGateRelease, round.NoiseGateRelease);
        Assert.Equal(chain.SilenceThresholdDb, round.SilenceThresholdDb);
        Assert.True(round.HighPassOn && round.LowPassOn && round.CompressorOn && round.NoiseGateOn);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("{")]
    public void Nothing_saved_reads_as_the_defaults(string? json)
        => Assert.Equal(AudioListeningChain.Default.ToJson(), AudioListeningChain.FromJson(json).ToJson());

    /// <summary>
    /// A row written before a setting existed keeps everything it does have.
    /// </summary>
    /// <remarks>
    /// All-or-nothing would mean every addition to this chain silently reset everybody's filters.
    /// </remarks>
    [Fact]
    public void A_chain_from_an_older_shape_keeps_what_it_does_have()
    {
        var chain = AudioListeningChain.FromJson("""{"highPassOn":true,"highPassHz":300}""");

        Assert.True(chain.HighPassOn);
        Assert.Equal(300, chain.HighPassHz);
        Assert.False(chain.NoiseGateOn);                 // never chosen — the default
        Assert.Equal(-40, chain.NoiseGateThresholdDb);
        Assert.Equal(AudioListeningChain.BandCount, chain.EqGains.Count);
    }

    // ── The equaliser is indexed directly by ten sliders ──────────────────────

    /// <summary>
    /// A stored chain with too few bands is padded flat rather than read as it is.
    /// </summary>
    /// <remarks>
    /// The component indexes ten sliders directly, so a nine-band row read back would throw
    /// mid-render — a saved setting that breaks the page that saved it.
    /// </remarks>
    [Fact]
    public void Too_few_bands_are_padded_rather_than_left_short()
    {
        var chain = AudioListeningChain.FromJson("""{"eqGains":[4,4,4]}""");

        Assert.Equal(AudioListeningChain.BandCount, chain.EqGains.Count);
        Assert.Equal(4, chain.EqGains[0]);
        Assert.Equal(0, chain.EqGains[9]);
    }

    [Fact]
    public void Too_many_bands_are_trimmed()
    {
        var chain = AudioListeningChain.FromJson("""{"eqGains":[1,2,3,4,5,6,7,8,9,10,11,12]}""");

        Assert.Equal(AudioListeningChain.BandCount, chain.EqGains.Count);
        Assert.Equal(10, chain.EqGains[9]);
    }

    // ── Values that would silence the output ──────────────────────────────────

    /// <summary>
    /// A stored NaN would multiply the whole output into nothing, with no sign of why.
    /// </summary>
    /// <remarks>
    /// The recurring trap in this editor: NaN survives both halves of a range test, so a check
    /// written as two inequalities lets it through. Nothing here is trusted without
    /// <c>double.IsFinite</c>.
    /// </remarks>
    [Theory]
    [InlineData("NaN")]
    [InlineData("1e400")]
    [InlineData("-1e400")]
    public void A_value_that_is_not_a_real_number_falls_back(string literal)
    {
        // JSON has no NaN literal, so a value like this can only arrive from a hand-edited row —
        // which is exactly the case a stored setting has to survive.
        var chain = AudioListeningChain.FromJson($$"""{"compressorRatio":{{literal}}}""");

        Assert.Equal(AudioListeningChain.Default.CompressorRatio, chain.CompressorRatio);
    }

    [Fact]
    public void A_value_outside_its_range_is_brought_back_inside_it()
    {
        var chain = AudioListeningChain.FromJson(
            """{"highPassHz":99999,"compressorRatio":500,"noiseGateThresholdDb":-9999,"eqGains":[99,-99,0,0,0,0,0,0,0,0]}""");

        Assert.InRange(chain.HighPassHz, 10, 2_000);
        Assert.InRange(chain.CompressorRatio, 1, 20);
        Assert.InRange(chain.NoiseGateThresholdDb, -100, 0);
        Assert.InRange(chain.EqGains[0], -24, 24);
        Assert.InRange(chain.EqGains[1], -24, 24);
    }

    // ── Saying whether anything is on ─────────────────────────────────────────

    [Fact]
    public void A_chain_with_a_filter_on_says_something_is_on()
        => Assert.True((AudioListeningChain.Default with { HighPassOn = true }).IsAnythingOn);

    [Fact]
    public void A_chain_with_only_the_equaliser_moved_says_something_is_on()
        => Assert.True((AudioListeningChain.Default with { EqGains = [0, 0, 6, 0, 0, 0, 0, 0, 0, 0] }).IsAnythingOn);

    [Fact]
    public void A_chain_whose_silence_threshold_moved_is_not_something_you_hear()
        => Assert.False((AudioListeningChain.Default with { SilenceThresholdDb = -60 }).IsAnythingOn);
}
