using Ben.Data.WebApi.Controllers.Entities;
using Xunit;
using Ben.Data.WebApi.Services.Audio;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Correctness tests for <see cref="AudioMixer"/> — verifies offset placement, pan, and gain
/// by mixing synthetic sine tones and inspecting the resulting stereo WAV.
/// </summary>
public class AudioMixerTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static byte[] CreateSineWav(double frequencyHz, double seconds, int sampleRate = 22050)
    {
        var numSamples = (int)(sampleRate * seconds);
        var dataSize = numSamples * 2;
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        w.Write(new[] { 'R', 'I', 'F', 'F' });
        w.Write(36 + dataSize);
        w.Write(new[] { 'W', 'A', 'V', 'E' });
        w.Write(new[] { 'f', 'm', 't', ' ' });
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(sampleRate);
        w.Write(sampleRate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write(new[] { 'd', 'a', 't', 'a' });
        w.Write(dataSize);
        for (var i = 0; i < numSamples; i++)
        {
            var value = (short)(0.8 * short.MaxValue * Math.Sin(2 * Math.PI * frequencyHz * i / sampleRate));
            w.Write(value);
        }
        return ms.ToArray();
    }

    /// <summary>Parses a stereo 16-bit PCM WAV back into separate left/right channel arrays.</summary>
    private static (short[] Left, short[] Right, int SampleRate) ReadWavPcm16Stereo(byte[] wavBytes)
    {
        using var ms = new MemoryStream(wavBytes);
        using var r = new BinaryReader(ms);
        r.ReadBytes(4); // RIFF
        r.ReadInt32();  // file size
        r.ReadBytes(4); // WAVE

        var sampleRate = 0;
        short[]? interleaved = null;
        while (ms.Position < ms.Length)
        {
            var chunkId = new string(r.ReadChars(4));
            var chunkSize = r.ReadInt32();
            if (chunkId == "fmt ")
            {
                r.ReadInt16(); // audio format
                r.ReadInt16(); // channels
                sampleRate = r.ReadInt32();
                r.ReadBytes(chunkSize - 8);
            }
            else if (chunkId == "data")
            {
                var raw = r.ReadBytes(chunkSize);
                interleaved = new short[raw.Length / 2];
                Buffer.BlockCopy(raw, 0, interleaved, 0, raw.Length);
            }
            else
            {
                r.ReadBytes(chunkSize);
            }
        }

        interleaved ??= [];
        var left = new short[interleaved.Length / 2];
        var right = new short[interleaved.Length / 2];
        for (var i = 0; i < left.Length; i++)
        {
            left[i] = interleaved[i * 2];
            right[i] = interleaved[i * 2 + 1];
        }
        return (left, right, sampleRate);
    }

    private static double EstimateFrequencyHz(short[] samples, int sampleRate)
    {
        var crossings = 0;
        for (var i = 1; i < samples.Length; i++)
            if (samples[i - 1] < 0 && samples[i] >= 0) crossings++;
        var durationSeconds = samples.Length / (double)sampleRate;
        return crossings / durationSeconds;
    }

    private static double Peak(short[] samples) => samples.Length == 0 ? 0 : samples.Max(Math.Abs);

    /// <summary>A stereo WAV with a different tone on each side, so a downmix is audible in a test.</summary>
    private static byte[] CreateStereoWav(
        double leftHz, double rightHz, double seconds, int sampleRate = 22050)
    {
        var frames   = (int)(sampleRate * seconds);
        var dataSize = frames * 4;                 // 16-bit, two channels
        using var ms = new MemoryStream();
        using var w  = new BinaryWriter(ms);

        w.Write(new[] { 'R', 'I', 'F', 'F' });
        w.Write(36 + dataSize);
        w.Write(new[] { 'W', 'A', 'V', 'E' });
        w.Write(new[] { 'f', 'm', 't', ' ' });
        w.Write(16);
        w.Write((short)1);
        w.Write((short)2);                          // channels
        w.Write(sampleRate);
        w.Write(sampleRate * 4);
        w.Write((short)4);
        w.Write((short)16);
        w.Write(new[] { 'd', 'a', 't', 'a' });
        w.Write(dataSize);
        for (var i = 0; i < frames; i++)
        {
            w.Write((short)(0.8 * short.MaxValue * Math.Sin(2 * Math.PI * leftHz  * i / sampleRate)));
            w.Write((short)(0.8 * short.MaxValue * Math.Sin(2 * Math.PI * rightHz * i / sampleRate)));
        }
        return ms.ToArray();
    }

    private static AudioMixer.TrackInput Track(
        byte[] wav, double offset = 0, double gainDb = 0, double pan = 0)
        => new(new MemoryStream(wav), "audio/wav", offset, gainDb, pan);

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Mix_NoTracks_Throws()
    {
        Assert.Throws<ArgumentException>(() => AudioMixer.Mix([]));
    }

    [Fact]
    public void Mix_SingleTrack_PreservesFrequencyAndDuration()
    {
        var wav = CreateSineWav(440.0, seconds: 1.0);
        using var stream = new MemoryStream(wav);

        var (bytes, contentType, _) = AudioMixer.Mix([
            new AudioMixer.TrackInput(stream, "audio/wav", OffsetSeconds: 0, GainDb: 0, Pan: 0),
        ]);

        Assert.Equal("audio/wav", contentType);
        var (left, right, sampleRate) = ReadWavPcm16Stereo(bytes);
        Assert.InRange(left.Length / (double)sampleRate, 0.9, 1.1);
        Assert.InRange(EstimateFrequencyHz(left, sampleRate), 440.0 * 0.9, 440.0 * 1.1);
        Assert.InRange(EstimateFrequencyHz(right, sampleRate), 440.0 * 0.9, 440.0 * 1.1);
    }

    [Fact]
    public void Mix_TwoTracks_NonOverlappingOffsets_PlaceEachAtItsOwnOffset()
    {
        var wavA = CreateSineWav(440.0, seconds: 1.0);
        var wavB = CreateSineWav(880.0, seconds: 1.0);
        using var streamA = new MemoryStream(wavA);
        using var streamB = new MemoryStream(wavB);

        var (bytes, _, _) = AudioMixer.Mix([
            new AudioMixer.TrackInput(streamA, "audio/wav", OffsetSeconds: 0, GainDb: 0, Pan: 0),
            new AudioMixer.TrackInput(streamB, "audio/wav", OffsetSeconds: 1.0, GainDb: 0, Pan: 0),
        ]);

        var (left, _, sampleRate) = ReadWavPcm16Stereo(bytes);
        Assert.InRange(left.Length / (double)sampleRate, 1.9, 2.1);

        var firstSecond = left.Take(sampleRate).ToArray();
        var secondSecond = left.Skip(sampleRate).ToArray();
        Assert.InRange(EstimateFrequencyHz(firstSecond, sampleRate), 440.0 * 0.9, 440.0 * 1.1);
        Assert.InRange(EstimateFrequencyHz(secondSecond, sampleRate), 880.0 * 0.9, 880.0 * 1.1);
    }

    [Fact]
    public void Mix_PanFullLeft_SilencesRightChannel()
    {
        var wav = CreateSineWav(440.0, seconds: 0.5);
        using var stream = new MemoryStream(wav);

        var (bytes, _, _) = AudioMixer.Mix([
            new AudioMixer.TrackInput(stream, "audio/wav", OffsetSeconds: 0, GainDb: 0, Pan: -1.0),
        ]);

        var (left, right, _) = ReadWavPcm16Stereo(bytes);
        Assert.True(Peak(left) > 1000);
        Assert.True(Peak(right) < 50); // silent modulo rounding
    }

    [Fact]
    public void Mix_Gain_ReducesAmplitude()
    {
        var wavLoud = CreateSineWav(440.0, seconds: 0.5);
        var wavAlsoLoud = CreateSineWav(440.0, seconds: 0.5);

        using var loudStream = new MemoryStream(wavLoud);
        var (loudBytes, _, _) = AudioMixer.Mix([
            new AudioMixer.TrackInput(loudStream, "audio/wav", OffsetSeconds: 0, GainDb: 0, Pan: 0),
        ]);

        using var quietStream = new MemoryStream(wavAlsoLoud);
        var (quietBytes, _, _) = AudioMixer.Mix([
            new AudioMixer.TrackInput(quietStream, "audio/wav", OffsetSeconds: 0, GainDb: -12, Pan: 0),
        ]);

        var (loudLeft, _, _) = ReadWavPcm16Stereo(loudBytes);
        var (quietLeft, _, _) = ReadWavPcm16Stereo(quietBytes);

        var ratio = Peak(quietLeft) / Peak(loudLeft);
        Assert.InRange(ratio, 0.20, 0.30); // -12dB ≈ 0.25x amplitude
    }

    // ── What the mixer was doing to the audio (2026-09-06 audio walk, finding 12) ──

    /// <summary>
    /// A stereo recording keeps its two channels.
    /// </summary>
    /// <remarks>
    /// Every source was averaged down to mono before being placed, so a two-microphone setup — the
    /// ordinary case for an investigation — came out of the mixer as one microphone, and whatever
    /// separated the two was gone. Two different tones, one per side, and each must survive on its
    /// own side.
    /// </remarks>
    [Fact]
    public void A_stereo_source_keeps_its_two_channels()
    {
        var (bytes, _, _) = AudioMixer.Mix([Track(CreateStereoWav(300, 900, 1.0))]);

        var (left, right, rate) = ReadWavPcm16Stereo(bytes);

        Assert.InRange(EstimateFrequencyHz(left,  rate), 270, 330);
        Assert.InRange(EstimateFrequencyHz(right, rate), 810, 990);
    }

    /// <summary>
    /// A mix of one track, at unity, is that track.
    /// </summary>
    /// <remarks>
    /// <c>tanh</c> was applied to every sample whether or not anything had summed, and the pan law
    /// attenuated the centre, so a single quiet track came out of the mixer quieter and slightly
    /// distorted than it went in. A mix of one thing that is not that thing is the clearest
    /// possible sign the mixer is doing something to audio it was not asked to do.
    /// </remarks>
    [Fact]
    public void A_mix_of_one_centred_track_is_that_track()
    {
        var source = CreateSineWav(440, 1.0);

        var (bytes, _, _) = AudioMixer.Mix([Track(source)]);
        var (left, right, _) = ReadWavPcm16Stereo(bytes);

        // The source peaks at 0.8 of full scale; a centred unity mix must too, on both sides.
        Assert.InRange(Peak(left)  / (double)short.MaxValue, 0.76, 0.84);
        Assert.InRange(Peak(right) / (double)short.MaxValue, 0.76, 0.84);
    }

    /// <summary>
    /// Two tracks that sum past full scale are held inside it rather than wrapping.
    /// </summary>
    /// <remarks>
    /// The soft knee is still there; it is only that it no longer touches audio that fits.
    /// </remarks>
    [Fact]
    public void Tracks_that_sum_past_full_scale_are_held_inside_it()
    {
        var loud = CreateSineWav(440, 1.0);

        var (bytes, _, _) = AudioMixer.Mix([Track(loud), Track(loud), Track(loud)]);
        var (left, _, _) = ReadWavPcm16Stereo(bytes);

        Assert.True(Peak(left) <= short.MaxValue, "the sum wrapped instead of being held");
        Assert.True(Peak(left) > 0.9 * short.MaxValue, "three summed tracks should reach full scale");
    }

    /// <summary>
    /// Panning left moves the right channel across rather than throwing it away.
    /// </summary>
    /// <remarks>
    /// The same law <c>StereoPannerNode</c> applies to a stereo input, which is what the mixer
    /// page's live preview runs on. Matching it is what makes the preview a preview.
    /// </remarks>
    [Fact]
    public void Panning_hard_left_carries_the_right_channel_over()
    {
        // Silence on the left, a tone on the right. Panning hard left must move the tone across;
        // an equal-power law that only turns the right side DOWN leaves silence everywhere.
        var (bytes, _, _) = AudioMixer.Mix([Track(CreateStereoWav(0, 900, 1.0), pan: -1)]);

        var (left, right, rate) = ReadWavPcm16Stereo(bytes);

        Assert.True(Peak(right) < 0.02 * short.MaxValue, "hard left should leave the right side silent");
        Assert.InRange(Peak(left) / (double)short.MaxValue, 0.76, 0.84);
        Assert.InRange(EstimateFrequencyHz(left, rate), 810, 990);
    }

    /// <summary>
    /// A recording at a different sample rate arrives without the tones it was resampled into.
    /// </summary>
    /// <remarks>
    /// The linear interpolation this replaced aliases on downsampling: everything above the new
    /// Nyquist frequency folds back into the audible band as tones that were never in the room. On
    /// a site about hearing things that were not there, that is the worst artefact available.
    /// </remarks>
    [Fact]
    public void A_source_at_another_rate_keeps_the_tone_it_had()
    {
        // 8 kHz in, 44.1 kHz out. A 1 kHz tone must still be a 1 kHz tone.
        var (bytes, _, _) = AudioMixer.Mix([Track(CreateSineWav(1000, 1.0, sampleRate: 8000))]);

        var (left, _, rate) = ReadWavPcm16Stereo(bytes);

        Assert.Equal(44100, rate);
        Assert.InRange(EstimateFrequencyHz(left, rate), 950, 1050);
    }

    /// <summary>
    /// Coming DOWN in rate, content above the new limit is removed rather than folded back.
    /// </summary>
    /// <remarks>
    /// This is what linear interpolation cannot do. A 23 kHz tone in a 48 kHz recording is above
    /// 44.1 kHz's Nyquist limit, so on the way down it either gets filtered out — which is correct
    /// — or reflects back into the band as a 21 kHz tone that was never in the room. On a site
    /// about hearing things that were not there, inventing tones is the artefact to be most careful
    /// about (2026-09-06 audio walk, finding 12).
    /// </remarks>
    [Fact]
    public void Coming_down_in_rate_does_not_fold_high_content_back_into_the_band()
    {
        var (bytes, _, _) = AudioMixer.Mix([Track(CreateSineWav(23_000, 0.5, sampleRate: 48_000))]);

        var (left, _, _) = ReadWavPcm16Stereo(bytes);

        Assert.True(Peak(left) < 0.2 * short.MaxValue,
            $"a 23 kHz tone survived the trip down to 44.1 kHz at {Peak(left) / (double)short.MaxValue:0.00} "
            + "of full scale — it has been reflected back into the audible band rather than filtered out");
    }

    [Fact]
    public void A_mono_source_arrives_on_both_sides()
    {
        var (bytes, _, _) = AudioMixer.Mix([Track(CreateSineWav(440, 1.0))]);

        var (left, right, _) = ReadWavPcm16Stereo(bytes);

        Assert.Equal(Peak(left), Peak(right), 1);
    }
}
