using Ben.Data.Common.Enums;
using Ben.Service.Models.Entities;
using NAudio.Wave;

namespace Ben.Data.WebApi.Services.Audio;

/// <summary>One stretch of audio the detector thinks is worth a listen.</summary>
/// <param name="StartSeconds">Start of the span, already padded.</param>
/// <param name="EndSeconds">End of the span, already padded.</param>
/// <param name="Score">
/// 0–100. <b>Not a probability that this is a voice.</b> It combines how far the sound rose above
/// its own noise floor, how much of its energy sits in the voice band, and whether its length is
/// speech-like. A loud door slam can score well; a real EVP buried in hiss can score poorly.
/// </param>
/// <param name="Features">
/// The measurements the score was made from, kept so a reviewer's Keep or Dismiss can later
/// re-weight them (item 253). Default for a candidate built by hand.
/// </param>
/// <param name="Exploratory">
/// Kept as one of the lower-ranked candidates a full scan sets aside places for
/// (<see cref="EvpDetectionOptions.ExplorationShare"/>), not for its score.
/// </param>
/// <param name="VoiceLikelihood">
/// How voice-shaped the sound is, 0–1, from VoiceNet after the scan (phase 9b); null when not measured.
/// Never part of <see cref="Score"/>.
/// </param>
/// <param name="SpectralFlatness">Feature 8 (phase 11): how hiss-like the sound is; see <see cref="EvpSpectralFeatures"/>.</param>
/// <param name="VoicedFrameFraction">Feature 9 (phase 11): how much of it has a speaking voice's pitch.</param>
public readonly record struct EvpCandidate(
    double StartSeconds, double EndSeconds, float Score, EvpFeatures Features = default,
    bool Exploratory = false, float? VoiceLikelihood = null,
    float? SpectralFlatness = null, float? VoicedFrameFraction = null)
{
    public double DurationSeconds => EndSeconds - StartSeconds;
}

/// <summary>
/// What the detector measured about one event, before any weighting (item 253).
/// </summary>
/// <remarks>
/// <para>The first three are what <see cref="EvpDetector"/>'s score is made of today; the score is
/// computed from these values and nothing else, so storing them is enough to re-fit its weights.
/// The rest are cheap extras measured now so that a re-fit has something to learn from beyond
/// the hand-picked three.</para>
/// <para>All are of the detected sound itself — the context padding is not included.</para>
/// </remarks>
/// <param name="PeakProminenceDb">Highest voice-band level above the local noise floor, in dB.</param>
/// <param name="MeanBandGapDb">Mean voice-band level minus full-band level, in dB; 0 means all of the energy is in the voice band.</param>
/// <param name="EventSeconds">Length of the detected sound, unpadded.</param>
/// <param name="MeanFloorDb">Mean local noise floor under the event, in dBFS — how quiet the room was.</param>
/// <param name="PeakBandDb">Loudest voice-band frame, in dBFS.</param>
/// <param name="BandLevelSpreadDb">Standard deviation of the voice-band level across the event: speech rises and falls with syllables, hum holds steady.</param>
/// <param name="ZeroCrossingRate">Fraction of samples where the full-band signal changes sign: hiss is high, voiced sound is low.</param>
public readonly record struct EvpFeatures(
    double PeakProminenceDb,
    double MeanBandGapDb,
    double EventSeconds,
    double MeanFloorDb,
    double PeakBandDb,
    double BandLevelSpreadDb,
    double ZeroCrossingRate);

/// <summary>One scan's result: the candidates, how long the recording was, and what the detector saw.</summary>
/// <param name="Trace">The detector's view of the whole recording (phase 8); null for an empty one.</param>
/// <param name="VoiceModel">The model that measured the candidates' voice-likeness, or null when none did.</param>
/// <param name="ScoringVersion">Which weights scored it (phase 13): 1 for the built-in ones.</param>
public sealed record EvpAnalysis(IReadOnlyList<EvpCandidate> Candidates, double RecordingSeconds, EvpTrace? Trace = null,
    string? VoiceModel = null, int ScoringVersion = 1);

/// <summary>
/// What the detector saw across a recording, a tenth of a second at a time (phase 8): enough to draw
/// why each candidate was found, without decoding the recording again.
/// </summary>
/// <remarks>
/// <para>The voice-band and full levels are the loudest frame in each bucket, so a short sound is
/// never averaged away; the background is the mean. The threshold line is the background plus the
/// scan's threshold, worked out where it is drawn, so moving the threshold redraws without a rescan.</para>
/// <para><see cref="GateRuns"/> are where the gate opened, before joining, splitting and the
/// shortest-sound rule: the page can show "the gate opened here, but it was too short to keep".</para>
/// </remarks>
public sealed record EvpTrace(
    double BucketSeconds, float[] BandDb, float[] FullDb, float[] FloorDb, IReadOnlyList<(double Start, double End)> GateRuns);

/// <summary>A candidate's score, taken apart (phase 8): each term's points and what it measured, in a sentence.</summary>
public sealed record EvpScoreExplanation(
    double ProminencePoints, string Prominence,
    double BandPoints, string Band,
    double DurationPoints, string Duration)
{
    public double Total => ProminencePoints + BandPoints + DurationPoints;
}

/// <summary>
/// How much each part of the score counts (audio phase 13, 10/27/2026): how far a sound stood out, how
/// much of it was in the listening range, and its length. Each is at least 0 and they add up to 1, so a
/// score is always 0–100 and always made of the same three parts.
/// </summary>
/// <param name="Version">1 for the built-in weights; 2 and up for weights fitted from Keep and Dismiss.</param>
public sealed record EvpScoreWeights(int Version, double Prominence, double Band, double Duration)
{
    /// <summary>The hand-set weights the detector has always used.</summary>
    public static readonly EvpScoreWeights BuiltIn = new(1, 0.5, 0.3, 0.2);

    /// <summary>Null when usable, otherwise why not.</summary>
    public string? Problem() =>
        Prominence < 0 || Band < 0 || Duration < 0 ? "A weight is below zero."
        : Math.Abs(Prominence + Band + Duration - 1) > 1e-6 ? "The weights do not add up to 1."
        : null;
}

/// <summary>How far a streamed scan has got (phase 6).</summary>
/// <param name="ProcessedSeconds">Audio read and measured so far.</param>
/// <param name="TotalSeconds">The recording's length when it was known up front, else null.</param>
/// <param name="SettledSeconds">
/// Audio whose result can no longer change: everything up to half a background window behind what
/// has been read, because the background level at a moment depends on what comes after it too. The
/// editor draws this part solid.
/// </param>
public readonly record struct EvpScanProgress(double ProcessedSeconds, double? TotalSeconds, double SettledSeconds);

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
    /// <summary>
    /// Which scoring the candidates came from, stored with every scan (item 253). Raise it whenever
    /// <see cref="Score(EvpFeatures)"/> or the way events are found changes, so labels gathered
    /// under one version are never mistaken for another's.
    /// </summary>
    public const int Version = 1;

    // ── Framing ───────────────────────────────────────────────────────────────
    // 25 ms is long enough for a stable RMS at speech frequencies and short enough to place an
    // onset tightly; 10 ms of hop gives 3 frames per the shortest event we keep.
    private const double WindowSeconds = 0.025;
    private const double HopSeconds    = 0.010;

    // Everything else is caller-tunable — see EvpDetectionOptions, which documents each value, the
    // range it's held to and why its default is what it is. The defaults are the constants that
    // used to live here: the voice band is the telephone band (300–3400 Hz), deliberately narrow so
    // it discards the rumble below and the hiss above, both of which otherwise dominate a quiet
    // recording; the floor is the 20th percentile of 10 s; and a sound is released 2 dB below its
    // onset so a wavering one stays a single event. Recordings differ too much for one fixed set to
    // serve them all (phase 5, 10/26/2026).

    /// <summary>Quietest level considered; below this a frame is treated as digital silence.</summary>
    private const double SilenceFloorDb = -120.0;

    /// <summary>
    /// Reads an audio stream (WAV or MP3, matching <see cref="AudioEditor"/>) and detects
    /// candidates in it.
    /// </summary>
    public static IReadOnlyList<EvpCandidate> Detect(
        Stream sourceStream, string sourceContentType, EvpDetectionOptions options, int maxResults)
        => Analyze(sourceStream, sourceContentType, options, maxResults).Candidates;

    /// <summary>
    /// <see cref="Detect(Stream, string, EvpDetectionOptions, int)"/>, plus the recording's length
    /// for the scan's record.
    /// </summary>
    public static EvpAnalysis Analyze(
        Stream sourceStream, string sourceContentType, EvpDetectionOptions options, int maxResults)
    {
        var (mono, sampleRate) = ReadMono(sourceStream, sourceContentType);
        return Analyze(mono, sampleRate, options, maxResults);
    }

    /// <summary>
    /// <see cref="Analyze(Stream, string, EvpDetectionOptions, int)"/> over samples a decoder has
    /// already read — how a scan reaches audio NAudio cannot open.
    /// </summary>
    public static EvpAnalysis Analyze(
        float[] mono, int sampleRate, EvpDetectionOptions options, int maxResults)
        => new(
            Detect(mono, sampleRate, options, maxResults),
            sampleRate > 0 ? mono.Length / (double)sampleRate : 0);

    /// <summary>
    /// Detects candidates in mono samples. Separate from the decoding overload so the algorithm can
    /// be exercised against synthetic audio with known event positions.
    /// </summary>
    /// <param name="mono">Mono samples in [-1, 1].</param>
    /// <param name="sampleRate">Samples per second.</param>
    /// <param name="options">Tunable thresholds — see <see cref="EvpDetectionOptions"/>.</param>
    /// <param name="maxResults">Keep at most this many, highest-scoring first.</param>
    public static IReadOnlyList<EvpCandidate> Detect(
        float[] mono, int sampleRate, EvpDetectionOptions options, int maxResults)
    {
        if (mono.Length == 0 || sampleRate <= 0 || maxResults <= 0) return [];

        // One chunk through the same path a streamed scan takes, so the two cannot drift apart.
        var frames = new FrameAccumulator(sampleRate, options);
        frames.Push(mono);
        return Candidates(frames.Finish(), sampleRate, options, maxResults).Found;
    }

    /// <summary>
    /// Detects candidates in audio that arrives a piece at a time (phase 6), reporting progress as
    /// it goes. Gives exactly what <see cref="Detect(float[], int, EvpDetectionOptions, int)"/>
    /// gives for the same samples, however they are split.
    /// </summary>
    /// <remarks>
    /// <para>Only the per-frame measurements are kept, not the samples: about 24 bytes per 10 ms,
    /// so 90 minutes is 13 MB where the samples at 16 kHz would be 345 MB. The background level,
    /// the gating and the scoring need the whole recording, so they run once the last piece is in.</para>
    /// <para>Cancellation is checked between pieces.</para>
    /// </remarks>
    public static async Task<EvpAnalysis> AnalyzeAsync(
        IAsyncEnumerable<ReadOnlyMemory<float>> chunks, int sampleRate, EvpDetectionOptions options,
        int maxResults, double? totalSeconds, IProgress<EvpScanProgress>? progress, CancellationToken ct,
        EvpScoreWeights? weights = null)
    {
        weights ??= EvpScoreWeights.BuiltIn;
        var frames = new FrameAccumulator(sampleRate, options);
        await foreach (var chunk in chunks.WithCancellation(ct))
        {
            ct.ThrowIfCancellationRequested();
            frames.Push(chunk.Span);
            var read = frames.SampleCount / (double)sampleRate;
            progress?.Report(new EvpScanProgress(read, totalSeconds, Math.Max(0, read - options.FloorWindowSeconds / 2)));
        }

        var data = frames.Finish();
        var seconds = sampleRate > 0 ? data.SampleCount / (double)sampleRate : 0;
        var (found, trace) = data.SampleCount == 0 || sampleRate <= 0 || maxResults <= 0
            ? ([], null)
            : Candidates(data, sampleRate, options, maxResults, weights);
        return new EvpAnalysis(found, seconds, trace, ScoringVersion: weights.Version);
    }

    /// <summary>From the per-frame measurements to the candidates: the part that needs all of them.</summary>
    private static (IReadOnlyList<EvpCandidate> Found, EvpTrace? Trace) Candidates(
        Frames frames, int sampleRate, EvpDetectionOptions options, int maxResults, EvpScoreWeights? weights = null)
    {
        weights ??= EvpScoreWeights.BuiltIn;
        var (bandDb, fullDb) = (frames.BandDb, frames.FullDb);
        if (bandDb.Length == 0) return ([], null);

        var floorDb = SlidingFloor(bandDb, options);
        var runs    = GateRuns(bandDb, floorDb, options.ThresholdDb, options.ReleaseHysteresisDb);
        var trace   = Trace(bandDb, fullDb, floorDb, runs, sampleRate);
        var events  = ShapeEvents(runs, sampleRate, options);

        var totalSeconds = frames.SampleCount / (double)sampleRate;

        // Length rules are applied to the detected energy, before padding — otherwise the context
        // itself would push a 60 ms click past the minimum and back into the queue.
        var scored = events
            .Where(e => FrameToSeconds(e.End, sampleRate) - FrameToSeconds(e.Start, sampleRate) >= options.MinDurationSeconds)
            .Select(e =>
            {
                var features = Measure(e, frames, floorDb, sampleRate);
                return new EvpCandidate(
                    Math.Max(0,            FrameToSeconds(e.Start, sampleRate) - options.ContextPadSeconds),
                    Math.Min(totalSeconds, FrameToSeconds(e.End,   sampleRate) + options.ContextPadSeconds),
                    Score(features, weights),
                    features);
            })
            .Where(c => c.Score >= options.MinScore)
            .ToList();

        // Hand back in playback order — a review queue reads top to bottom through the recording,
        // not by rank.
        return (Cap(scored, Math.Min(maxResults, options.MaxCandidates), options.ExplorationShare)
            .OrderBy(c => c.StartSeconds)
            .ToList(), trace);
    }

    /// <summary>A tenth of a second per bucket.</summary>
    internal const double TraceBucketSeconds = 0.1;

    /// <summary>The per-frame measurements gathered into tenths of a second (phase 8). See <see cref="EvpTrace"/>.</summary>
    private static EvpTrace Trace(double[] bandDb, double[] fullDb, double[] floorDb, List<Run> runs, int sampleRate)
    {
        var per     = Math.Max(1, (int)Math.Round(TraceBucketSeconds / HopSeconds));
        var buckets = (bandDb.Length + per - 1) / per;
        var band  = new float[buckets];
        var full  = new float[buckets];
        var floor = new float[buckets];
        for (var b = 0; b < buckets; b++)
        {
            double bandMax = double.MinValue, fullMax = double.MinValue, floorSum = 0;
            var from = b * per;
            var to   = Math.Min(bandDb.Length, from + per);
            for (var f = from; f < to; f++)
            {
                bandMax  = Math.Max(bandMax, bandDb[f]);
                fullMax  = Math.Max(fullMax, fullDb[f]);
                floorSum += floorDb[f];
            }
            band[b]  = (float)bandMax;
            full[b]  = (float)fullMax;
            floor[b] = (float)(floorSum / (to - from));
        }
        var gate = runs.Select(r => (FrameToSeconds(r.Start, sampleRate), FrameToSeconds(r.End + 1, sampleRate))).ToList();
        return new EvpTrace(per * HopSeconds, band, full, floor, gate);
    }

    /// <summary>
    /// At most <paramref name="cap"/>: the highest scores, less a share of the places set aside for
    /// lower-ranked candidates spread evenly through the rest of the recording (item 253).
    /// </summary>
    /// <remarks>
    /// <para>A detector that only ever shows its favorites only ever learns about its favorites. The
    /// set-aside places go to every k-th of the remaining candidates in playback order, so the same
    /// scan always picks the same ones; nothing here is random.</para>
    /// <para>The share is rounded down, so a small cap reserves nothing: two places are both the top
    /// two.</para>
    /// </remarks>
    internal static List<EvpCandidate> Cap(List<EvpCandidate> scored, int cap, double explorationShare)
    {
        if (cap <= 0) return [];

        var ranked = scored.OrderByDescending(c => c.Score).ThenBy(c => c.StartSeconds).ToList();
        if (ranked.Count <= cap) return ranked;

        var reserve = (int)Math.Floor(Math.Clamp(explorationShare, 0, 1) * cap);
        var kept    = ranked.Take(cap - reserve).ToList();
        if (reserve == 0) return kept;

        var rest = ranked.Skip(cap - reserve).OrderBy(c => c.StartSeconds).ToList();
        var step = rest.Count / (double)reserve;
        for (var i = 0; i < reserve; i++)
            kept.Add(rest[(int)(i * step + step / 2)] with { Exploratory = true });
        return kept;
    }

    // ── Framing, as the samples arrive ────────────────────────────────────────

    /// <summary>What the detector keeps of a recording: one entry per frame, never the samples.</summary>
    /// <param name="CrossingsBefore">
    /// Sign changes in the full-band signal before each frame's second sample: with
    /// <paramref name="CrossingsThrough"/>, the count over any run of frames without the samples.
    /// </param>
    /// <param name="CrossingsThrough">Sign changes up to the last sample of each frame's window.</param>
    private sealed record Frames(
        double[] BandDb, double[] FullDb, int[] CrossingsBefore, int[] CrossingsThrough, long SampleCount);

    /// <summary>
    /// Band-passes, frames and measures samples as they come, holding only the last window of them.
    /// </summary>
    /// <remarks>
    /// <para>Cascaded RBJ high-pass and low-pass biquads. Two passes of a second-order section is a
    /// gentle slope, which is what's wanted: a steep filter rings, and ringing on a transient looks
    /// exactly like a short voice-band event. The filters keep their state between pieces, so a
    /// recording split anywhere is filtered exactly as it would be whole.</para>
    /// <para>Each frame's level is summed oldest sample first, the order the array version summed
    /// in, so the levels match to the last bit and so do the pinned scores.</para>
    /// </remarks>
    private sealed class FrameAccumulator
    {
        private readonly Biquad _hp, _lp;
        private readonly int _window, _hop;
        private readonly float[] _band, _full;
        private readonly List<double> _bandDb = [], _fullDb = [];
        private readonly List<int> _crossingsBefore = [], _crossingsThrough = [];
        private long _count;
        private bool _previousNonNegative;
        private int _crossings;

        public FrameAccumulator(int sampleRate, EvpDetectionOptions options)
        {
            _hp     = Biquad.HighPass(options.BandLowHz, sampleRate);
            _lp     = Biquad.LowPass(Math.Min(options.BandHighHz, sampleRate / 2.0 - 1), sampleRate);
            _window = Math.Max(1, (int)(WindowSeconds * sampleRate));
            _hop    = Math.Max(1, (int)(HopSeconds    * sampleRate));
            _band   = new float[_window];
            _full   = new float[_window];
        }

        public long SampleCount => _count;

        public void Push(ReadOnlySpan<float> samples)
        {
            foreach (var x in samples)
            {
                var slot = (int)(_count % _window);
                _band[slot] = (float)_lp.Process(_hp.Process(x));
                _full[slot] = x;

                // A sign change between this sample and the one before it.
                if (_count > 0 && (x >= 0) != _previousNonNegative) _crossings++;
                _previousNonNegative = x >= 0;
                _count++;

                if ((_count - 1) % _hop == 0) _crossingsBefore.Add(_crossings);
                if (_count >= _window && (_count - _window) % _hop == 0)
                {
                    _bandDb.Add(RmsDb(_band));
                    _fullDb.Add(RmsDb(_full));
                    _crossingsThrough.Add(_crossings);
                }
            }
        }

        public Frames Finish() => new(
            [.. _bandDb], [.. _fullDb], [.. _crossingsBefore], [.. _crossingsThrough], _count);

        /// <summary>The window's level, its oldest sample first.</summary>
        private double RmsDb(float[] ring)
        {
            double sum = 0;
            var oldest = (int)(_count % _window);
            for (var k = 0; k < _window; k++)
            {
                var v = ring[(oldest + k) % _window];
                sum += v * (double)v;
            }
            var rms = Math.Sqrt(sum / _window);
            return rms <= 1e-12 ? SilenceFloorDb : Math.Max(SilenceFloorDb, 20.0 * Math.Log10(rms));
        }
    }

    private static double FrameToSeconds(int frame, int sampleRate)
    {
        var hop = Math.Max(1, (int)(HopSeconds * sampleRate));
        return frame * (double)hop / sampleRate;
    }

    // ── Adaptive floor ────────────────────────────────────────────────────────

    /// <summary>
    /// The <see cref="EvpDetectionOptions.FloorPercentile"/> of a window centred on each frame, via a
    /// 1 dB histogram so the cost is per-frame constant rather than a sort per frame.
    /// </summary>
    private static double[] SlidingFloor(double[] bandDb, EvpDetectionOptions options)
    {
        var half = Math.Max(1, (int)(options.FloorWindowSeconds / HopSeconds / 2));
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

            var target = Math.Max(1, (int)Math.Ceiling(inWindow * options.FloorPercentile));
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

    private static List<Run> GateRuns(double[] bandDb, double[] floorDb, double onsetDeltaDb, double releaseHysteresisDb)
    {
        var runs = new List<Run>();
        var active = false;
        var start = 0;

        for (var f = 0; f < bandDb.Length; f++)
        {
            var onset   = floorDb[f] + onsetDeltaDb;
            var release = onset - releaseHysteresisDb;

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

    private static List<Run> ShapeEvents(List<Run> runs, int sampleRate, EvpDetectionOptions options)
    {
        if (runs.Count == 0) return runs;

        var mergeFrames = (int)Math.Round(options.MergeGapSeconds / HopSeconds);
        var maxFrames   = Math.Max(1, (int)Math.Round(options.MaxEventSeconds / HopSeconds));
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

        var minFrames = Math.Max(1, (int)Math.Round(options.MinDurationSeconds / HopSeconds));
        return bounded.Where(r => r.End - r.Start + 1 >= minFrames).ToList();
    }

    // ── Measuring and scoring ─────────────────────────────────────────────────

    /// <summary>
    /// Everything the score needs, and a few things it doesn't use yet (item 253).
    /// </summary>
    private static EvpFeatures Measure(Run run, Frames measured, double[] floorDb, int sampleRate)
    {
        var (bandDb, fullDb) = (measured.BandDb, measured.FullDb);
        double peakProminence = 0, peakBand = double.MinValue;
        double bandSum = 0, fullSum = 0, floorSum = 0, bandSquares = 0;

        for (var f = run.Start; f <= run.End; f++)
        {
            peakProminence = Math.Max(peakProminence, bandDb[f] - floorDb[f]);
            peakBand = Math.Max(peakBand, bandDb[f]);
            bandSum += bandDb[f];
            fullSum += fullDb[f];
            floorSum += floorDb[f];
            bandSquares += bandDb[f] * bandDb[f];
        }
        var frames = run.End - run.Start + 1;
        var bandMean = bandSum / frames;

        // The samples the event's frames cover, for the sign changes: counted as they arrived, so
        // the samples themselves are not needed (phase 6).
        var hop       = Math.Max(1, (int)(HopSeconds    * sampleRate));
        var window    = Math.Max(1, (int)(WindowSeconds * sampleRate));
        var first     = run.Start * hop;
        var last      = run.End * hop + window - 1;
        var crossings = measured.CrossingsThrough[run.End] - measured.CrossingsBefore[run.Start];

        return new EvpFeatures(
            PeakProminenceDb:  peakProminence,
            MeanBandGapDb:     (bandSum - fullSum) / frames,
            EventSeconds:      frames * HopSeconds,
            MeanFloorDb:       floorSum / frames,
            PeakBandDb:        peakBand,
            BandLevelSpreadDb: Math.Sqrt(Math.Max(0, bandSquares / frames - bandMean * bandMean)),
            ZeroCrossingRate:  last > first ? crossings / (double)(last - first) : 0);
    }

    /// <summary>
    /// Blends three independent signals. None is decisive alone: prominence alone promotes any
    /// loud bang, band ratio alone promotes steady tonal hum, and duration alone means nothing.
    /// </summary>
    /// <remarks>
    /// The weights are hand-set. Item 253 is the plan for letting reviewers' rulings re-fit them;
    /// until then this is <see cref="Version"/> 1, and it reads nothing but its argument so the
    /// stored features always reproduce the stored score.
    /// </remarks>
    /// <summary>
    /// <see cref="Score"/>'s three terms, in points, with what each measured in a plain sentence
    /// (phase 8). The weights and the shape are the same; <c>Explain_terms_sum_to_the_score</c>
    /// holds them together.
    /// </summary>
    public static EvpScoreExplanation Explain(EvpFeatures features) => Explain(features, EvpScoreWeights.BuiltIn);

    /// <summary><see cref="Explain(EvpFeatures)"/> for the weights the scan used (phase 13).</summary>
    public static EvpScoreExplanation Explain(EvpFeatures features, EvpScoreWeights weights)
    {
        var prominence = Math.Clamp(features.PeakProminenceDb / 20.0, 0, 1);
        var bandRatio  = Math.Clamp(1.0 + features.MeanBandGapDb / 20.0, 0, 1);
        var duration   = DurationShape(features.EventSeconds);

        var share = Math.Clamp(Math.Pow(10, features.MeanBandGapDb / 10.0), 0, 1);
        var length = features.EventSeconds switch
        {
            < 0.20  => "very short for a word",
            <= 3.0  => "about the length of a word or a phrase",
            <= 10.0 => "long, more like somebody talking",
            _       => "very long, more like a conversation",
        };

        return new EvpScoreExplanation(
            100 * weights.Prominence * prominence,
            $"It rose {features.PeakProminenceDb:0} dB above the room around it"
                + (prominence >= 1 ? " (20 dB or more counts in full)." : "."),
            100 * weights.Band * bandRatio,
            $"About {share * 100:0}% of its sound was in the range the scan listens in.",
            100 * weights.Duration * duration,
            $"It lasted {features.EventSeconds:0.0} s: {length}.");
    }

    private static double DurationShape(double eventSeconds) => eventSeconds switch
    {
        < 0.20 => 0.3,
        <= 3.0 => 1.0,
        <= 10.0 => 0.6,
        _ => 0.2,
    };

    internal static float Score(EvpFeatures features) => Score(features, EvpScoreWeights.BuiltIn);

    /// <summary>The score's three parts, each 0–1, before weighting (phase 13: what a refit weighs).</summary>
    public static (double Prominence, double Band, double Duration) Parts(EvpFeatures features) =>
        (Math.Clamp(features.PeakProminenceDb / 20.0, 0, 1),
         Math.Clamp(1.0 + features.MeanBandGapDb / 20.0, 0, 1),
         DurationShape(features.EventSeconds));

    /// <summary>The score with these weights; with the built-in ones it is exactly the version-1 score.</summary>
    public static float Score(EvpFeatures features, EvpScoreWeights weights)
    {
        // Fitted weights take the general path; the built-in ones keep the original arithmetic below
        // untouched, so version 1's pinned scores stay exact to the last bit.
        if (weights != EvpScoreWeights.BuiltIn)
        {
            var (p, b, d) = Parts(features);
            return (float)Math.Clamp(100.0 * (weights.Prominence * p + weights.Band * b + weights.Duration * d), 0, 100);
        }

        // How far it stood out. 20 dB over the floor is emphatic; beyond that adds nothing.
        var prominence = Math.Clamp(features.PeakProminenceDb / 20.0, 0, 1);

        // How much of the energy is in the voice band. Band-passing can only remove energy, so this
        // difference is <= 0 dB; near 0 means the sound lives where speech lives. A wideband clap
        // or a sub-bass thump loses most of its energy here and scores low.
        var bandRatio = Math.Clamp(1.0 + features.MeanBandGapDb / 20.0, 0, 1);

        // Speech-shaped length. Anything can happen in 60 ms; anything over ~10 s is usually a
        // person in the room talking, kept but demoted.
        var duration = DurationShape(features.EventSeconds);

        var score = 100.0 * (0.5 * prominence + 0.3 * bandRatio + 0.2 * duration);
        return (float)Math.Clamp(score, 0, 100);
    }

    // ── Decoding ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Decodes the recording as mono at the detection rate.
    /// </summary>
    /// <remarks>
    /// <para>The mixdown and the rate change happen while the stream is read, so the recording is
    /// never held at its own rate. It used to be: a <c>List&lt;float&gt;</c> was grown a second at
    /// a time — allocating a fresh array for every one of those seconds — then copied again to
    /// average the channels. One scan of a 90-minute recording took the API from 1.5 GB to 5.1 GB
    /// and left it there (2026-09-06 audio walk, finding 1).</para>
    ///
    /// <para>Reading at 16 kHz loses the detector nothing: it band-passes 300–3400 Hz and looks
    /// above that band only to compare against it.</para>
    /// </remarks>
    private static (float[] Mono, int SampleRate) ReadMono(Stream sourceStream, string contentType)
        => AudioSourceReader.ReadMonoAt(sourceStream, contentType);



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
