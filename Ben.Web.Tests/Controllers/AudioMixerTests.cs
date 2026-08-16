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
}
