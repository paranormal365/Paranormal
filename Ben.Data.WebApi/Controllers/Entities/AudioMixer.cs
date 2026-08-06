using NAudio.Wave;

namespace Ben.Data.WebApi.Controllers.Entities;

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

        var placedTracks = new List<(float[] Mono, int OffsetFrames, float Gain, float PanLeft, float PanRight)>();
        var totalFrames = 0;

        foreach (var track in tracks)
        {
            var (samples, format) = ReadSamples(track.SourceStream, track.SourceContentType);
            var mono = Downmix(samples, format.Channels);
            var resampled = format.SampleRate == OutputSampleRate
                ? mono
                : ResampleToRate(mono, format.SampleRate, OutputSampleRate);

            var offsetFrames = Math.Max(0, (int)(track.OffsetSeconds * OutputSampleRate));
            var gain = (float)Math.Pow(10, track.GainDb / 20.0);

            var panAngle = (Math.Clamp(track.Pan, -1, 1) + 1) * (Math.PI / 4);
            var panLeft  = (float)Math.Cos(panAngle);
            var panRight = (float)Math.Sin(panAngle);

            placedTracks.Add((resampled, offsetFrames, gain, panLeft, panRight));
            totalFrames = Math.Max(totalFrames, offsetFrames + resampled.Length);
        }

        var left  = new float[totalFrames];
        var right = new float[totalFrames];

        foreach (var (mono, offsetFrames, gain, panLeft, panRight) in placedTracks)
        {
            for (var i = 0; i < mono.Length; i++)
            {
                var sample = mono[i] * gain;
                left[offsetFrames + i]  += sample * panLeft;
                right[offsetFrames + i] += sample * panRight;
            }
        }

        // Soft-clip so multiple summed tracks don't produce harsh digital clipping.
        var stereo = new float[totalFrames * 2];
        for (var i = 0; i < totalFrames; i++)
        {
            stereo[i * 2]     = (float)Math.Tanh(left[i]);
            stereo[i * 2 + 1] = (float)Math.Tanh(right[i]);
        }

        return WriteWav(stereo, OutputSampleRate, 2);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static float[] Downmix(float[] samples, int channels)
    {
        if (channels == 1) return samples;

        var frames = samples.Length / channels;
        var mono = new float[frames];
        for (var f = 0; f < frames; f++)
        {
            var sum = 0f;
            for (var c = 0; c < channels; c++) sum += samples[f * channels + c];
            mono[f] = sum / channels;
        }
        return mono;
    }

    private static float[] ResampleToRate(float[] mono, int fromRate, int toRate)
    {
        var outFrames = Math.Max(1, (int)((long)mono.Length * toRate / fromRate));
        var result = new float[outFrames];
        var ratio = (double)fromRate / toRate;

        for (var i = 0; i < outFrames; i++)
        {
            var srcPos = i * ratio;
            var srcIndex = Math.Min((int)srcPos, mono.Length - 1);
            var srcIndexNext = Math.Min(srcIndex + 1, mono.Length - 1);
            var frac = srcPos - (int)srcPos;
            result[i] = (float)(mono[srcIndex] + (mono[srcIndexNext] - mono[srcIndex]) * frac);
        }
        return result;
    }

    private static (float[] Samples, WaveFormat Format) ReadSamples(Stream sourceStream, string sourceContentType)
    {
        using var waveStream = OpenWaveStream(sourceStream, sourceContentType);
        var provider = waveStream.ToSampleProvider();
        var format = provider.WaveFormat;

        var all = new List<float>();
        var buffer = new float[4096];
        int read;
        while ((read = provider.Read(buffer, 0, buffer.Length)) > 0)
            all.AddRange(new ArraySegment<float>(buffer, 0, read));

        return (all.ToArray(), format);
    }

    private static WaveStream OpenWaveStream(Stream sourceStream, string sourceContentType)
    {
        if (sourceContentType.Contains("wav", StringComparison.OrdinalIgnoreCase))
            return new WaveFileReader(sourceStream);
        if (sourceContentType.Contains("mp3", StringComparison.OrdinalIgnoreCase) ||
            sourceContentType.Contains("mpeg", StringComparison.OrdinalIgnoreCase))
            return new Mp3FileReader(sourceStream);

        throw new NotSupportedException(
            $"Audio mixing is supported for WAV and MP3. Received content-type: '{sourceContentType}'.");
    }

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
