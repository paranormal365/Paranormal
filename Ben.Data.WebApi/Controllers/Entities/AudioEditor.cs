using NAudio.Wave;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// Destructive audio edit operations (cut, silence, normalize, gain, fade, reverse).
/// Mirrors <see cref="AudioClipper"/>'s conventions: supports WAV and MP3 sources,
/// always outputs 16-bit PCM WAV.
/// </summary>
internal static class AudioEditor
{
    public static (byte[] Bytes, string ContentType, string Extension) CutRegion(
        Stream sourceStream, string sourceContentType, double startSeconds, double endSeconds)
    {
        var (samples, format) = ReadSamples(sourceStream, sourceContentType);
        var channels = format.Channels;
        var totalFrames = samples.Length / channels;
        var startFrame = Math.Clamp((int)(startSeconds * format.SampleRate), 0, totalFrames);
        var endFrame   = Math.Clamp((int)(endSeconds   * format.SampleRate), startFrame, totalFrames);

        var result = new float[samples.Length - (endFrame - startFrame) * channels];
        Array.Copy(samples, 0, result, 0, startFrame * channels);
        Array.Copy(samples, endFrame * channels, result, startFrame * channels, (totalFrames - endFrame) * channels);
        return WriteWav(result, format);
    }

    public static (byte[] Bytes, string ContentType, string Extension) SilenceRegion(
        Stream sourceStream, string sourceContentType, double startSeconds, double endSeconds)
    {
        var (samples, format) = ReadSamples(sourceStream, sourceContentType);
        var channels = format.Channels;
        var totalFrames = samples.Length / channels;
        var startFrame = Math.Clamp((int)(startSeconds * format.SampleRate), 0, totalFrames);
        var endFrame   = Math.Clamp((int)(endSeconds   * format.SampleRate), startFrame, totalFrames);

        Array.Clear(samples, startFrame * channels, (endFrame - startFrame) * channels);
        return WriteWav(samples, format);
    }

    public static (byte[] Bytes, string ContentType, string Extension) Normalize(
        Stream sourceStream, string sourceContentType)
    {
        var (samples, format) = ReadSamples(sourceStream, sourceContentType);
        var peak = 0f;
        foreach (var s in samples) peak = Math.Max(peak, Math.Abs(s));

        if (peak > 0.0001f)
        {
            var scale = 0.98f / peak;
            for (var i = 0; i < samples.Length; i++)
                samples[i] = Math.Clamp(samples[i] * scale, -1f, 1f);
        }
        return WriteWav(samples, format);
    }

    public static (byte[] Bytes, string ContentType, string Extension) Gain(
        Stream sourceStream, string sourceContentType, double gainDb)
    {
        var (samples, format) = ReadSamples(sourceStream, sourceContentType);
        var factor = (float)Math.Pow(10, gainDb / 20.0);
        for (var i = 0; i < samples.Length; i++)
            samples[i] = Math.Clamp(samples[i] * factor, -1f, 1f);
        return WriteWav(samples, format);
    }

    public static (byte[] Bytes, string ContentType, string Extension) Fade(
        Stream sourceStream, string sourceContentType, double fadeInSeconds, double fadeOutSeconds)
    {
        var (samples, format) = ReadSamples(sourceStream, sourceContentType);
        var channels = format.Channels;
        var totalFrames = samples.Length / channels;
        var fadeInFrames  = Math.Clamp((int)(fadeInSeconds  * format.SampleRate), 0, totalFrames);
        var fadeOutFrames = Math.Clamp((int)(fadeOutSeconds * format.SampleRate), 0, totalFrames);

        for (var f = 0; f < fadeInFrames; f++)
        {
            var mult = (float)f / fadeInFrames;
            for (var c = 0; c < channels; c++) samples[f * channels + c] *= mult;
        }
        for (var f = 0; f < fadeOutFrames; f++)
        {
            var mult = (float)f / fadeOutFrames;
            var frame = totalFrames - 1 - f;
            for (var c = 0; c < channels; c++) samples[frame * channels + c] *= mult;
        }
        return WriteWav(samples, format);
    }

    public static (byte[] Bytes, string ContentType, string Extension) Reverse(
        Stream sourceStream, string sourceContentType)
    {
        var (samples, format) = ReadSamples(sourceStream, sourceContentType);
        var channels = format.Channels;
        var totalFrames = samples.Length / channels;

        var result = new float[samples.Length];
        for (var f = 0; f < totalFrames; f++)
            Array.Copy(samples, f * channels, result, (totalFrames - 1 - f) * channels, channels);
        return WriteWav(result, format);
    }

    public static (byte[] Bytes, string ContentType, string Extension) PitchShift(
        Stream sourceStream, string sourceContentType, double semitones)
    {
        var (samples, format) = ReadSamples(sourceStream, sourceContentType);
        var pitchFactor = Math.Pow(2.0, semitones / 12.0);
        var shifted = ApplyPitchShift(samples, format.Channels, format.SampleRate, pitchFactor);
        return WriteWav(shifted, format);
    }

    /// <summary>
    /// Changes playback speed by <paramref name="speedRatio"/> (2.0 = twice as fast/half duration,
    /// 0.5 = half speed/double duration) while preserving pitch: resample to change duration and
    /// pitch together, then pitch-shift back by the inverse ratio to restore the original pitch.
    /// </summary>
    public static (byte[] Bytes, string ContentType, string Extension) ChangeSpeed(
        Stream sourceStream, string sourceContentType, double speedRatio)
    {
        var (samples, format) = ReadSamples(sourceStream, sourceContentType);
        var resampled = ResampleLinear(samples, format.Channels, speedRatio);
        var corrected = ApplyPitchShift(resampled, format.Channels, format.SampleRate, 1.0 / speedRatio);
        return WriteWav(corrected, format);
    }

    // ── Shared helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Runs each channel through its own <see cref="SmbPitchShifter"/>. Pads the input with
    /// <see cref="SmbPitchShifter.InFifoLatency"/> trailing zero samples and discards the same
    /// number of leading output samples so the phase vocoder's group delay doesn't clip the tail
    /// or leave silence at the start.
    /// </summary>
    private static float[] ApplyPitchShift(float[] samples, int channels, int sampleRate, double pitchFactor)
    {
        const int fftFrameSize = 2048;
        const int oversample = 8;

        var totalFrames = samples.Length / channels;
        var result = new float[samples.Length];

        for (var c = 0; c < channels; c++)
        {
            var shifter = new SmbPitchShifter(fftFrameSize, oversample, sampleRate);
            var paddedFrames = totalFrames + shifter.InFifoLatency;

            var mono = new float[paddedFrames];
            for (var f = 0; f < totalFrames; f++) mono[f] = samples[f * channels + c];

            var outMono = new float[paddedFrames];
            shifter.PitchShift(pitchFactor, mono, outMono, paddedFrames);

            for (var f = 0; f < totalFrames; f++) result[f * channels + c] = outMono[f + shifter.InFifoLatency];
        }
        return result;
    }

    /// <summary>Linear-interpolation resample that changes both frame count and (if played at the original rate) pitch by <paramref name="ratio"/>.</summary>
    private static float[] ResampleLinear(float[] samples, int channels, double ratio)
    {
        var inFrames = samples.Length / channels;
        var outFrames = Math.Max(1, (int)(inFrames / ratio));
        var result = new float[outFrames * channels];

        for (var i = 0; i < outFrames; i++)
        {
            var srcPos = i * ratio;
            var srcIndex = Math.Min((int)srcPos, inFrames - 1);
            var srcIndexNext = Math.Min(srcIndex + 1, inFrames - 1);
            var frac = srcPos - (int)srcPos;
            for (var c = 0; c < channels; c++)
            {
                var a = samples[srcIndex * channels + c];
                var b = samples[srcIndexNext * channels + c];
                result[i * channels + c] = (float)(a + (b - a) * frac);
            }
        }
        return result;
    }

    /// <summary>Decodes the full source stream into normalized [-1, 1] float samples (interleaved by channel).</summary>
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

    // Goes through AudioSourceReader so MP3 uses the managed decoder — the default NAudio path is
    // Windows-only and threw DllNotFoundException on macOS/Linux for every MP3 edit.
    private static WaveStream OpenWaveStream(Stream sourceStream, string sourceContentType)
        => AudioSourceReader.Open(sourceStream, sourceContentType);

    private static (byte[] Bytes, string ContentType, string Extension) WriteWav(float[] samples, WaveFormat sourceFormat)
    {
        using var ms = new MemoryStream();
        using (var writer = new WaveFileWriter(ms, new WaveFormat(sourceFormat.SampleRate, 16, sourceFormat.Channels)))
        {
            var shortBuffer = new short[4096];
            var i = 0;
            while (i < samples.Length)
            {
                var chunk = Math.Min(shortBuffer.Length, samples.Length - i);
                for (var j = 0; j < chunk; j++)
                    shortBuffer[j] = (short)Math.Clamp(samples[i + j] * 32767f, short.MinValue, short.MaxValue);
                writer.WriteSamples(shortBuffer, 0, chunk);
                i += chunk;
            }
        }
        return (ms.ToArray(), "audio/wav", ".wav");
    }
}
