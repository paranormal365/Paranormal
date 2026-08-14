using NAudio.Wave;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>How far above the local noise floor a sound has to rise before it's proposed.</summary>
public enum EvpSensitivity
{
    /// <summary>9 dB over the floor. Only obvious events; use on a noisy recording.</summary>
    Low = 0,
    /// <summary>6 dB over the floor. The default.</summary>
    Medium = 1,
    /// <summary>4 dB over the floor. Surfaces faint events, at the cost of more to review.</summary>
    High = 2,
}

/// <summary>One stretch of audio the detector thinks is worth a listen.</summary>
/// <param name="StartSeconds">Start of the span, already padded.</param>
/// <param name="EndSeconds">End of the span, already padded.</param>
/// <param name="Score">
/// 0–100. <b>Not a probability that this is a voice.</b> It combines how far the sound rose above
/// its own noise floor, how much of its energy sits in the voice band, and whether its length is
/// speech-like. A loud door slam can score well; a real EVP buried in hiss can score poorly.
/// </param>
public readonly record struct EvpCandidate(double StartSeconds, double EndSeconds, float Score)
{
    public double DurationSeconds => EndSeconds - StartSeconds;
}

/// <summary>
/// Finds stretches of a recording where voice-band energy rises above the local noise floor.
/// </summary>
/// <remarks>
/// <para><b>What this does and does not claim.</b> It is an energy detector with a voice-band
/// bias, not a speech recogniser. It answers "is something happening here that doesn't sound like
/// the rest of this recording", which is the question that usefully narrows three hours of tape
/// down to a review queue. Deciding whether any of it is a voice is the investigator's job, which
/// is why every result lands as a Pending candidate rather than a marker.</para>
///
/// <para><b>Why the floor is adaptive and local.</b> A fixed threshold fails on real
/// investigation audio: a recorder near a fridge has a floor 20 dB above one in a quiet room, and
/// the same recorder's floor moves when the heating starts. The floor is therefore recomputed over
/// a sliding window around each frame, so an event is judged against the room it was recorded in a
/// few seconds either side, not against an absolute number.</para>
///
/// <para><b>Why the 20th percentile rather than the median.</b> During a stretch of continuous
/// speech — an investigator talking — the median of the window is dragged up by the speech itself,
/// which hides quieter events nearby. The 20th percentile tracks the quiet part of the window,
/// which is what "noise floor" means.</para>
///
/// <para>Deterministic: the same samples and sensitivity always produce the same candidates.</para>
/// </remarks>
internal static class EvpDetector
{
    // ── Framing ───────────────────────────────────────────────────────────────
    // 25 ms is long enough for a stable RMS at speech frequencies and short enough to place an
    // onset tightly; 10 ms of hop gives 3 frames per the shortest event we keep.
    private const double WindowSeconds = 0.025;
    private const double HopSeconds    = 0.010;

    // ── Voice band ────────────────────────────────────────────────────────────
    // Telephone band. Deliberately narrow: it discards the rumble and handling noise below it and
    // the hiss above it, both of which otherwise dominate the energy of a quiet recording.
    private const double HighPassHz = 300.0;
    private const double LowPassHz  = 3400.0;

    // ── Floor tracking ────────────────────────────────────────────────────────
    private const double FloorWindowSeconds = 10.0;
    private const double FloorPercentile    = 0.20;

    // ── Gating ────────────────────────────────────────────────────────────────
    // Release 2 dB below onset so a wavering sound stays one event instead of chattering into a
    // dozen fragments.
    private const double ReleaseHysteresisDb = 2.0;

    // ── Event shaping ─────────────────────────────────────────────────────────
    private const double MergeGapSeconds = 0.35;   // syllable gaps shouldn't split a phrase
    private const double MinEventSeconds = 0.15;   // shorter than this is a click, not an utterance

    /// <summary>
    /// Context added either side of the detected energy. A span trimmed exactly to where the gate
    /// opened and closed plays back as a fragment starting mid-sound, which is close to useless for
    /// deciding what you're hearing — a reviewer needs a moment of the room before and after to
    /// judge it against. Wide enough to give that, narrow enough that neighbouring events don't
    /// dissolve into each other.
    /// </summary>
    private const double ContextPadSeconds = 0.40;

    /// <summary>
    /// Merging stops here even if the gate is still open. Continuous talking would otherwise merge
    /// into one enormous candidate, and "listen to these ten seconds" is not a reviewable finding —
    /// several bounded candidates at least give a reviewer somewhere to start.
    /// </summary>
    private const double MaxEventSeconds = 5.0;

    /// <summary>Quietest level considered; below this a frame is treated as digital silence.</summary>
    private const double SilenceFloorDb = -120.0;

    private static double OnsetDeltaDb(EvpSensitivity s) => s switch
    {
        EvpSensitivity.High => 4.0,
        EvpSensitivity.Low  => 9.0,
        _                   => 6.0,
    };

    /// <summary>
    /// Reads an audio stream (WAV or MP3, matching <see cref="AudioEditor"/>) and detects
    /// candidates in it.
    /// </summary>
    public static IReadOnlyList<EvpCandidate> Detect(
        Stream sourceStream, string sourceContentType, EvpSensitivity sensitivity, int maxResults)
    {
        var (mono, sampleRate) = ReadMono(sourceStream, sourceContentType);
        return Detect(mono, sampleRate, sensitivity, maxResults);
    }

    /// <summary>
    /// Detects candidates in mono samples. Separate from the decoding overload so the algorithm can
    /// be exercised against synthetic audio with known event positions.
    /// </summary>
    /// <param name="mono">Mono samples in [-1, 1].</param>
    /// <param name="sampleRate">Samples per second.</param>
    /// <param name="sensitivity">How far above the floor counts as an event.</param>
    /// <param name="maxResults">Keep at most this many, highest-scoring first.</param>
    public static IReadOnlyList<EvpCandidate> Detect(
        float[] mono, int sampleRate, EvpSensitivity sensitivity, int maxResults)
    {
        if (mono.Length == 0 || sampleRate <= 0 || maxResults <= 0) return [];

        var band = BandPass(mono, sampleRate);

        var (bandDb, fullDb) = FrameEnergies(band, mono, sampleRate);
        if (bandDb.Length == 0) return [];

        var floorDb = SlidingFloor(bandDb, sampleRate);
        var runs    = GateRuns(bandDb, floorDb, OnsetDeltaDb(sensitivity));
        var events  = ShapeEvents(runs, bandDb.Length, sampleRate);

        var totalSeconds = mono.Length / (double)sampleRate;

        // Length rules are applied to the detected energy, before padding — otherwise the context
        // itself would push a 60 ms click past the minimum and back into the queue.
        var scored = events
            .Where(e => FrameToSeconds(e.End, sampleRate) - FrameToSeconds(e.Start, sampleRate) >= MinEventSeconds)
            .Select(e => new EvpCandidate(
                Math.Max(0,            FrameToSeconds(e.Start, sampleRate) - ContextPadSeconds),
                Math.Min(totalSeconds, FrameToSeconds(e.End,   sampleRate) + ContextPadSeconds),
                Score(e, bandDb, fullDb, floorDb, sampleRate)))
            .ToList();

        // Cap by score, then hand back in playback order — a review queue reads top to bottom
        // through the recording, not by rank.
        return scored
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.StartSeconds)
            .Take(maxResults)
            .OrderBy(c => c.StartSeconds)
            .ToList();
    }

    // ── Band-pass ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Cascaded RBJ high-pass and low-pass biquads. Two passes of a second-order section is a
    /// gentle slope, which is what's wanted: a steep filter rings, and ringing on a transient looks
    /// exactly like a short voice-band event.
    /// </summary>
    private static float[] BandPass(float[] input, int sampleRate)
    {
        var hp = Biquad.HighPass(HighPassHz, sampleRate);
        var lp = Biquad.LowPass(Math.Min(LowPassHz, sampleRate / 2.0 - 1), sampleRate);

        var output = new float[input.Length];
        for (var i = 0; i < input.Length; i++)
            output[i] = (float)lp.Process(hp.Process(input[i]));
        return output;
    }

    // ── Framing ───────────────────────────────────────────────────────────────

    private static (double[] BandDb, double[] FullDb) FrameEnergies(
        float[] band, float[] full, int sampleRate)
    {
        var window = Math.Max(1, (int)(WindowSeconds * sampleRate));
        var hop    = Math.Max(1, (int)(HopSeconds    * sampleRate));
        if (band.Length < window) return ([], []);

        var count  = 1 + (band.Length - window) / hop;
        var bandDb = new double[count];
        var fullDb = new double[count];

        for (var f = 0; f < count; f++)
        {
            var offset = f * hop;
            bandDb[f] = RmsDb(band, offset, window);
            fullDb[f] = RmsDb(full, offset, window);
        }
        return (bandDb, fullDb);
    }

    private static double RmsDb(float[] samples, int offset, int length)
    {
        double sum = 0;
        for (var i = offset; i < offset + length; i++) sum += samples[i] * (double)samples[i];
        var rms = Math.Sqrt(sum / length);
        return rms <= 1e-12 ? SilenceFloorDb : Math.Max(SilenceFloorDb, 20.0 * Math.Log10(rms));
    }

    private static double FrameToSeconds(int frame, int sampleRate)
    {
        var hop = Math.Max(1, (int)(HopSeconds * sampleRate));
        return frame * (double)hop / sampleRate;
    }

    // ── Adaptive floor ────────────────────────────────────────────────────────

    /// <summary>
    /// The <see cref="FloorPercentile"/> of a window centred on each frame, via a 1 dB histogram
    /// so the cost is per-frame constant rather than a sort per frame.
    /// </summary>
    private static double[] SlidingFloor(double[] bandDb, int sampleRate)
    {
        var half = Math.Max(1, (int)(FloorWindowSeconds / HopSeconds / 2));
        var bins = new int[(int)-SilenceFloorDb + 2];   // one bin per dB, from SilenceFloorDb to 0

        int BinOf(double db) => Math.Clamp((int)Math.Round(db - SilenceFloorDb), 0, bins.Length - 1);

        var floor = new double[bandDb.Length];
        var lo = 0;
        var hi = -1;
        var inWindow = 0;

        for (var f = 0; f < bandDb.Length; f++)
        {
            var wantLo = Math.Max(0, f - half);
            var wantHi = Math.Min(bandDb.Length - 1, f + half);

            while (hi < wantHi) { bins[BinOf(bandDb[++hi])]++; inWindow++; }
            while (lo < wantLo) { bins[BinOf(bandDb[lo++])]--; inWindow--; }

            var target = Math.Max(1, (int)Math.Ceiling(inWindow * FloorPercentile));
            var seen = 0;
            for (var b = 0; b < bins.Length; b++)
            {
                seen += bins[b];
                if (seen >= target) { floor[f] = b + SilenceFloorDb; break; }
            }
        }
        return floor;
    }

    // ── Gating ────────────────────────────────────────────────────────────────

    private readonly record struct Run(int Start, int End);

    private static List<Run> GateRuns(double[] bandDb, double[] floorDb, double onsetDeltaDb)
    {
        var runs = new List<Run>();
        var active = false;
        var start = 0;

        for (var f = 0; f < bandDb.Length; f++)
        {
            var onset   = floorDb[f] + onsetDeltaDb;
            var release = onset - ReleaseHysteresisDb;

            if (!active && bandDb[f] >= onset)
            {
                active = true;
                start = f;
            }
            else if (active && bandDb[f] < release)
            {
                active = false;
                runs.Add(new Run(start, f - 1));
            }
        }
        if (active) runs.Add(new Run(start, bandDb.Length - 1));
        return runs;
    }

    private static List<Run> ShapeEvents(List<Run> runs, int frameCount, int sampleRate)
    {
        if (runs.Count == 0) return runs;

        var mergeFrames = (int)Math.Round(MergeGapSeconds / HopSeconds);
        var maxFrames   = (int)Math.Round(MaxEventSeconds / HopSeconds);
        var merged = new List<Run>();
        var current = runs[0];

        foreach (var run in runs.Skip(1))
        {
            var wouldSpan = run.End - current.Start + 1;
            if (run.Start - current.End <= mergeFrames && wouldSpan <= maxFrames)
                current = current with { End = run.End };
            else { merged.Add(current); current = run; }
        }
        merged.Add(current);

        // A single gate-open stretch can exceed the cap without any merging at all (sustained
        // sound), so split those too rather than letting them through the check above.
        var bounded = new List<Run>();
        foreach (var run in merged)
        {
            var length = run.End - run.Start + 1;
            if (length <= maxFrames) { bounded.Add(run); continue; }

            for (var start = run.Start; start <= run.End; start += maxFrames)
                bounded.Add(new Run(start, Math.Min(run.End, start + maxFrames - 1)));
        }

        var minFrames = (int)Math.Round(MinEventSeconds / HopSeconds);
        return bounded.Where(r => r.End - r.Start + 1 >= minFrames).ToList();
    }

    // ── Scoring ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Blends three independent signals. None is decisive alone: prominence alone promotes any
    /// loud bang, band ratio alone promotes steady tonal hum, and duration alone means nothing.
    /// </summary>
    private static float Score(
        Run run, double[] bandDb, double[] fullDb, double[] floorDb, int sampleRate)
    {
        double peakProminence = 0;
        double bandSum = 0, fullSum = 0;

        for (var f = run.Start; f <= run.End; f++)
        {
            peakProminence = Math.Max(peakProminence, bandDb[f] - floorDb[f]);
            bandSum += bandDb[f];
            fullSum += fullDb[f];
        }
        var frames = run.End - run.Start + 1;

        // How far it stood out. 20 dB over the floor is emphatic; beyond that adds nothing.
        var prominence = Math.Clamp(peakProminence / 20.0, 0, 1);

        // How much of the energy is in the voice band. Band-passing can only remove energy, so this
        // difference is <= 0 dB; near 0 means the sound lives where speech lives. A wideband clap
        // or a sub-bass thump loses most of its energy here and scores low.
        var bandGapDb = (bandSum - fullSum) / frames;
        var bandRatio = Math.Clamp(1.0 + bandGapDb / 20.0, 0, 1);

        // Speech-shaped length. Anything can happen in 60 ms; anything over ~10 s is usually a
        // person in the room talking, kept but demoted.
        var seconds  = frames * HopSeconds;
        var duration = seconds switch
        {
            < 0.20 => 0.3,
            <= 3.0 => 1.0,
            <= 10.0 => 0.6,
            _ => 0.2,
        };

        var score = 100.0 * (0.5 * prominence + 0.3 * bandRatio + 0.2 * duration);
        return (float)Math.Clamp(score, 0, 100);
    }

    // ── Decoding ──────────────────────────────────────────────────────────────

    private static (float[] Mono, int SampleRate) ReadMono(Stream sourceStream, string contentType)
    {
        using var reader = CreateReader(sourceStream, contentType);
        var provider = reader.ToSampleProvider();
        var format = reader.WaveFormat;

        var buffer = new List<float>();
        var chunk = new float[format.SampleRate * format.Channels];
        int read;
        while ((read = provider.Read(chunk, 0, chunk.Length)) > 0)
            buffer.AddRange(chunk.AsSpan(0, read).ToArray());

        var channels = Math.Max(1, format.Channels);
        if (channels == 1) return ([.. buffer], format.SampleRate);

        var frames = buffer.Count / channels;
        var mono = new float[frames];
        for (var i = 0; i < frames; i++)
        {
            double sum = 0;
            for (var c = 0; c < channels; c++) sum += buffer[i * channels + c];
            mono[i] = (float)(sum / channels);
        }
        return (mono, format.SampleRate);
    }

    private static WaveStream CreateReader(Stream stream, string contentType)
    {
        stream.Position = 0;
        var type = (contentType ?? string.Empty).ToLowerInvariant();
        if (type.Contains("mpeg") || type.Contains("mp3")) return new Mp3FileReader(stream);
        return new WaveFileReader(stream);
    }

    // ── Biquad ────────────────────────────────────────────────────────────────

    /// <summary>Direct-form-I RBJ biquad. Holds its own state, so one instance filters one stream.</summary>
    private sealed class Biquad
    {
        private readonly double _b0, _b1, _b2, _a1, _a2;
        private double _x1, _x2, _y1, _y2;

        private Biquad(double b0, double b1, double b2, double a0, double a1, double a2)
        {
            _b0 = b0 / a0; _b1 = b1 / a0; _b2 = b2 / a0;
            _a1 = a1 / a0; _a2 = a2 / a0;
        }

        public static Biquad HighPass(double freq, int sampleRate)
        {
            var (w0, alpha, cos) = Coefficients(freq, sampleRate);
            return new Biquad(
                (1 + cos) / 2, -(1 + cos), (1 + cos) / 2,
                1 + alpha, -2 * cos, 1 - alpha);
        }

        public static Biquad LowPass(double freq, int sampleRate)
        {
            var (w0, alpha, cos) = Coefficients(freq, sampleRate);
            return new Biquad(
                (1 - cos) / 2, 1 - cos, (1 - cos) / 2,
                1 + alpha, -2 * cos, 1 - alpha);
        }

        private static (double W0, double Alpha, double Cos) Coefficients(double freq, int sampleRate)
        {
            const double q = 0.7071067811865476;   // Butterworth: maximally flat, no passband ripple
            var w0 = 2 * Math.PI * Math.Clamp(freq, 1, sampleRate / 2.0 - 1) / sampleRate;
            return (w0, Math.Sin(w0) / (2 * q), Math.Cos(w0));
        }

        public double Process(double x)
        {
            var y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
            _x2 = _x1; _x1 = x;
            _y2 = _y1; _y1 = y;
            return y;
        }
    }
}
