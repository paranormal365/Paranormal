using NAudio.Wave;

namespace EvpLab;

/// <summary>Everything the bench does with samples: 16 kHz mono float, read, write, measure, filter.</summary>
internal static class Audio
{
    public const int Rate = 16_000;

    public static float[] ReadMono16k(string path)
    {
        using var reader = new AudioFileReader(path);
        var provider = reader.WaveFormat.SampleRate == Rate
            ? (ISampleProvider)reader
            : new NAudio.Wave.SampleProviders.WdlResamplingSampleProvider(reader, Rate);
        var channels = provider.WaveFormat.Channels;
        var mono = new List<float>();
        var buffer = new float[Rate * channels];
        int read;
        while ((read = provider.Read(buffer, 0, buffer.Length)) > 0)
            for (var i = 0; i + channels <= read; i += channels)
            {
                float sum = 0;
                for (var c = 0; c < channels; c++) sum += buffer[i + c];
                mono.Add(sum / channels);
            }
        return [.. mono];
    }

    public static void WriteMono16k(string path, float[] samples)
    {
        using var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(Rate, 1));
        writer.WriteSamples(samples, 0, samples.Length);
    }

    public static double Rms(ReadOnlySpan<float> s)
    {
        if (s.Length == 0) return 0;
        double sum = 0;
        foreach (var v in s) sum += (double)v * v;
        return Math.Sqrt(sum / s.Length);
    }

    public static double Db(double rms) => 20 * Math.Log10(Math.Max(rms, 1e-9));

    /// <summary>
    /// RMS of the sound while it is actually sounding: 20 ms frames within 30 dB of the loudest frame. A phrase has
    /// gaps between words, and averaging them in would make every voice look quieter than it is.
    /// </summary>
    public static double ActiveRms(float[] s)
    {
        const int frame = Rate / 50;
        var frames = new List<double>();
        for (var i = 0; i + frame <= s.Length; i += frame) frames.Add(Rms(s.AsSpan(i, frame)));
        if (frames.Count == 0) return Rms(s);
        var peak = frames.Max();
        var active = frames.Where(f => Db(f) > Db(peak) - 30).ToList();
        return Math.Sqrt(active.Average(f => f * f));
    }

    /// <summary>A copy band-limited to 300–3400 Hz, the band a voice's intelligibility lives in.</summary>
    public static float[] VoiceBand(float[] s)
    {
        var copy = (float[])s.Clone();
        Biquad.HighPass(300).Apply(copy);
        Biquad.LowPass(3400).Apply(copy);
        return copy;
    }

    public static float[] Slice(float[] s, double startSeconds, double endSeconds)
    {
        var a = Math.Clamp((int)(startSeconds * Rate), 0, s.Length);
        var b = Math.Clamp((int)(endSeconds * Rate), a, s.Length);
        return s[a..b];
    }

    /// <summary>RBJ biquad, applied in place. Enough for band-limiting a voice or shaping noise.</summary>
    public sealed class Biquad
    {
        private readonly double _b0, _b1, _b2, _a1, _a2;
        private double _x1, _x2, _y1, _y2;

        private Biquad(double b0, double b1, double b2, double a0, double a1, double a2)
            => (_b0, _b1, _b2, _a1, _a2) = (b0 / a0, b1 / a0, b2 / a0, a1 / a0, a2 / a0);

        public static Biquad LowPass(double hz, double q = 0.707)
        {
            var w = 2 * Math.PI * hz / Rate; var alpha = Math.Sin(w) / (2 * q); var cos = Math.Cos(w);
            return new((1 - cos) / 2, 1 - cos, (1 - cos) / 2, 1 + alpha, -2 * cos, 1 - alpha);
        }

        public static Biquad HighPass(double hz, double q = 0.707)
        {
            var w = 2 * Math.PI * hz / Rate; var alpha = Math.Sin(w) / (2 * q); var cos = Math.Cos(w);
            return new((1 + cos) / 2, -(1 + cos), (1 + cos) / 2, 1 + alpha, -2 * cos, 1 - alpha);
        }

        public float Next(float x)
        {
            var y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
            (_x2, _x1, _y2, _y1) = (_x1, x, _y1, y);
            return (float)y;
        }

        public void Apply(float[] s) { for (var i = 0; i < s.Length; i++) s[i] = Next(s[i]); }
    }
}
