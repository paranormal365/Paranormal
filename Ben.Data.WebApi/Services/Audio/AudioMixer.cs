using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Ben.Data.WebApi.Services.Audio;

/// <summary>
/// Sums multiple offset, gained, and panned tracks down to a single stereo WAV.
/// Self-contained like <see cref="AudioEditor"/> — duplicates decode/resample helpers
/// rather than sharing a base.
/// </summary>
internal static class AudioMixer
{
    private const int OutputSampleRate = 44100;

    public sealed record TrackInput(Stream SourceStream, string SourceContentType, double OffsetSeconds, double GainDb, double Pan);

    public static (byte[] Bytes, string ContentType, string Extension) Mix(IReadOnlyList<TrackInput> tracks)
    {
        if (tracks.Count == 0)
            throw new ArgumentException("At least one audible track is required.", nameof(tracks));

        var placedTracks = new List<(float[] Left, float[] Right, int OffsetFrames, float Gain, float Pan)>();
        var totalFrames = 0;

        foreach (var track in tracks)
        {
            var (samples, format) = ReadSamples(track.SourceStream, track.SourceContentType);
            var (left, right)     = SplitChannels(samples, format.Channels);

            if (format.SampleRate != OutputSampleRate)
            {
                left  = Resample(left,  format.SampleRate, OutputSampleRate);
                right = Resample(right, format.SampleRate, OutputSampleRate);
            }

            var offsetFrames = Math.Max(0, (int)(track.OffsetSeconds * OutputSampleRate));
            var gain = (float)Math.Pow(10, track.GainDb / 20.0);

            placedTracks.Add((left, right, offsetFrames, gain, (float)Math.Clamp(track.Pan, -1, 1)));
            totalFrames = Math.Max(totalFrames, offsetFrames + Math.Min(left.Length, right.Length));
        }

        var mixLeft  = new float[totalFrames];
        var mixRight = new float[totalFrames];

        foreach (var (left, right, offsetFrames, gain, pan) in placedTracks)
        {
            var (keep, bleed) = PanCoefficients(pan);
            var frames = Math.Min(left.Length, right.Length);

            for (var i = 0; i < frames; i++)
            {
                var l = left[i]  * gain;
                var r = right[i] * gain;

                if (pan <= 0)
                {
                    // Panning left moves the right channel across rather than turning it down, so
                    // nothing is lost and a centred track passes through untouched.
                    mixLeft[offsetFrames + i]  += l + r * keep;
                    mixRight[offsetFrames + i] += r * bleed;
                }
                else
                {
                    mixLeft[offsetFrames + i]  += l * keep;
                    mixRight[offsetFrames + i] += r + l * bleed;
                }
            }
        }

        var stereo = new float[totalFrames * 2];
        for (var i = 0; i < totalFrames; i++)
        {
            stereo[i * 2]     = SoftClip(mixLeft[i]);
            stereo[i * 2 + 1] = SoftClip(mixRight[i]);
        }

        return WriteWav(stereo, OutputSampleRate, 2);
    }

    /// <summary>
    /// The two coefficients the stereo pan law needs, for a pan in [-1, 1].
    /// </summary>
    /// <remarks>
    /// <para>This is the law <c>StereoPannerNode</c> applies to a stereo input, which is what the
    /// mixer page's live preview runs on. Matching it exactly is the point: a preview that sounds
    /// different from the export is worse than no preview, because it is believed.</para>
    ///
    /// <para>At centre it is the identity — the track passes through at its own level, in its own
    /// image. Panning moves one channel across into the other rather than turning it down, so
    /// nothing is quietly lost on the way.</para>
    /// </remarks>
    private static (float Keep, float Bleed) PanCoefficients(float pan)
    {
        var x = pan <= 0 ? pan + 1 : pan;
        return ((float)Math.Cos(x * Math.PI / 2), (float)Math.Sin(x * Math.PI / 2));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Splits interleaved samples into a left and a right channel.
    /// </summary>
    /// <remarks>
    /// <para>The mixer used to average every source down to mono before placing it, so a stereo
    /// recording lost its image the moment it entered the mixer and a two-microphone setup — the
    /// ordinary case for an investigation — came out as one microphone (2026-09-06 audio walk,
    /// finding 12).</para>
    ///
    /// <para>A mono source becomes the same signal on both sides, which is what mono means. More
    /// than two channels are folded into two rather than refused: a field recorder can hand back
    /// four, and losing the extra pair is better than losing the file.</para>
    /// </remarks>
    private static (float[] Left, float[] Right) SplitChannels(float[] samples, int channels)
    {
        channels = Math.Max(1, channels);
        var frames = samples.Length / channels;

        if (channels == 1) return (samples, samples);

        var left  = new float[frames];
        var right = new float[frames];

        for (var f = 0; f < frames; f++)
        {
            var baseIndex = f * channels;
            if (channels == 2)
            {
                left[f]  = samples[baseIndex];
                right[f] = samples[baseIndex + 1];
                continue;
            }

            // Odd channels to the left, even to the right — the conventional fold-down.
            float l = 0, r = 0;
            for (var c = 0; c < channels; c++)
                if (c % 2 == 0) l += samples[baseIndex + c]; else r += samples[baseIndex + c];

            var perSide = channels / 2f;
            left[f]  = l / perSide;
            right[f] = r / perSide;
        }

        return (left, right);
    }

    /// <summary>
    /// Changes one channel's sample rate.
    /// </summary>
    /// <remarks>
    /// Through NAudio's <see cref="WdlResamplingSampleProvider"/>, which band-limits properly. The
    /// linear interpolation this replaced aliases audibly on downsampling — it folds everything
    /// above the new Nyquist frequency back into the audible band as tones that were never in the
    /// room, which on a site about hearing things that were not there is the worst possible
    /// artefact (finding 12).
    /// </remarks>
    private static float[] Resample(float[] channel, int fromRate, int toRate)
    {
        if (fromRate == toRate || channel.Length == 0) return channel;

        var provider  = new WdlResamplingSampleProvider(new ChannelProvider(channel, fromRate), toRate);
        var estimated = (int)((long)channel.Length * toRate / fromRate) + toRate;
        var output    = new float[estimated];
        var count     = 0;

        while (count < output.Length)
        {
            var read = provider.Read(output, count, Math.Min(1 << 16, output.Length - count));
            if (read == 0) break;
            count += read;
        }

        Array.Resize(ref output, count);
        return output;
    }

    /// <summary>One already-decoded mono channel, as something NAudio can resample.</summary>
    private sealed class ChannelProvider(float[] samples, int sampleRate) : ISampleProvider
    {
        private int _position;

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);

        public int Read(float[] buffer, int offset, int count)
        {
            var take = Math.Min(count, samples.Length - _position);
            if (take <= 0) return 0;
            Array.Copy(samples, _position, buffer, offset, take);
            _position += take;
            return take;
        }
    }

    /// <summary>
    /// Keeps a summed peak inside full scale without touching anything that already fits.
    /// </summary>
    /// <remarks>
    /// <c>tanh</c> was applied to every sample, so a single quiet track passed through the mixer
    /// came out quieter and slightly distorted even though nothing had summed and nothing was near
    /// clipping — the mix of one track was not the track (finding 12). Below full scale this is now
    /// the identity; above it, the same soft knee as before.
    /// </remarks>
    private static float SoftClip(float sample)
        => Math.Abs(sample) <= 1f ? sample : (float)Math.Tanh(sample);

    /// <summary>
    /// Decodes one track.
    /// </summary>
    /// <remarks>
    /// Through <see cref="AudioSourceReader"/>, which allocates one buffer from the header and
    /// enforces the same length ceiling every other edit obeys. This mattered most here: a mix
    /// holds every track at once, so the old grow-and-copy read was three copies per track and up
    /// to eight tracks (2026-09-06 audio walk, finding 1).
    /// </remarks>
    private static (float[] Samples, WaveFormat Format) ReadSamples(Stream sourceStream, string sourceContentType)
        => AudioSourceReader.ReadAll(sourceStream, sourceContentType);

    private static (byte[] Bytes, string ContentType, string Extension) WriteWav(float[] interleavedStereo, int sampleRate, int channels)
    {
        using var ms = new MemoryStream();
        using (var writer = new WaveFileWriter(ms, new WaveFormat(sampleRate, 16, channels)))
        {
            var shortBuffer = new short[4096];
            var i = 0;
            while (i < interleavedStereo.Length)
            {
                var chunk = Math.Min(shortBuffer.Length, interleavedStereo.Length - i);
                for (var j = 0; j < chunk; j++)
                    shortBuffer[j] = (short)Math.Clamp(interleavedStereo[i + j] * 32767f, short.MinValue, short.MaxValue);
                writer.WriteSamples(shortBuffer, 0, chunk);
                i += chunk;
            }
        }
        return (ms.ToArray(), "audio/wav", ".wav");
    }
}
