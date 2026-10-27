namespace Ben.Data.WebApi.Services.Audio;

/// <summary>
/// Features 8 and 9 of a candidate (audio phase 11, item 253, 10/27/2026): measured for the detector to
/// learn from later, and never part of its score.
/// </summary>
/// <remarks>
/// <para>Frames match the detector's: 25 ms (400 samples) every 10 ms at 16 kHz, each Hann-windowed into a
/// 512-point FFT (<see cref="Fft"/>).</para>
/// <para><b>Spectral flatness</b>, 0–1: inside the listening range, the geometric mean of the power over
/// its arithmetic mean, averaged over the sound's frames. Hiss and room tone are flat (near 1); a voice or
/// a hum puts its power in a few places (near 0).</para>
/// <para><b>Voiced frame fraction</b>, 0–1: the share of frames whose normalized autocorrelation peaks at
/// 0.5 or more somewhere between lags 40 and 200 (400 Hz down to 80 Hz, the range of a speaking voice's
/// pitch). Speech is voiced for much of its length; a knock or hiss almost never.</para>
/// <para>Frames quieter than −60 dBFS are skipped: digital silence is neither flat nor voiced.</para>
/// </remarks>
public static class EvpSpectralFeatures
{
    public const int FrameSamples = 400;
    public const int HopSamples = 160;
    private const int FftSize = 512;
    private const int MinLag = 40, MaxLag = 200;
    private const double VoicedThreshold = 0.5;
    private const double SilentRms = 0.001;   // −60 dBFS

    private static readonly double[] Hann = Enumerable.Range(0, FrameSamples)
        .Select(n => 0.5 - 0.5 * Math.Cos(2 * Math.PI * n / FrameSamples)).ToArray();

    /// <summary>The two features of a stretch of 16 kHz audio, or nulls when it held no sound to measure.</summary>
    public static (float? Flatness, float? Voiced) Measure(ReadOnlySpan<float> audio, double bandLowHz, double bandHighHz, int rate = 16_000)
    {
        var lowBin = Math.Clamp((int)Math.Ceiling(bandLowHz * FftSize / rate), 1, FftSize / 2);
        var highBin = Math.Clamp((int)Math.Floor(bandHighHz * FftSize / rate), lowBin, FftSize / 2);

        var buffer = new double[2 * FftSize];
        double flatnessSum = 0;
        int measured = 0, voiced = 0;
        for (var start = 0; start + FrameSamples <= audio.Length; start += HopSamples)
        {
            var frame = audio.Slice(start, FrameSamples);
            double energy = 0;
            foreach (var x in frame) energy += x * x;
            if (Math.Sqrt(energy / FrameSamples) < SilentRms) continue;
            measured++;

            Array.Clear(buffer);
            for (var n = 0; n < FrameSamples; n++) buffer[2 * n] = frame[n] * Hann[n];
            Fft.Transform(buffer, FftSize, -1);
            double logSum = 0, sum = 0;
            for (var k = lowBin; k <= highBin; k++)
            {
                var power = buffer[2 * k] * buffer[2 * k] + buffer[2 * k + 1] * buffer[2 * k + 1] + 1e-20;
                logSum += Math.Log(power);
                sum += power;
            }
            var bins = highBin - lowBin + 1;
            flatnessSum += Math.Exp(logSum / bins) / (sum / bins);

            if (IsVoiced(frame, energy)) voiced++;
        }

        return measured == 0 ? (null, null) : ((float)(flatnessSum / measured), (float)voiced / measured);
    }

    private static bool IsVoiced(ReadOnlySpan<float> frame, double energy)
    {
        for (var lag = MinLag; lag <= MaxLag; lag++)
        {
            double r = 0;
            for (var n = 0; n + lag < frame.Length; n++) r += frame[n] * frame[n + lag];
            if (r / energy >= VoicedThreshold) return true;
        }
        return false;
    }
}
