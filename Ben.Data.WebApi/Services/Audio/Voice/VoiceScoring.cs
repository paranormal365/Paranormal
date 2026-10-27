namespace Ben.Data.WebApi.Services.Audio.Voice;

/// <summary>
/// Measures how voice-like each candidate is (audio phase 9b, 10/27/2026): the highest VoiceNet reading
/// over one-second windows across the candidate's span, a quarter second apart.
/// </summary>
/// <remarks>
/// <para><b>The highest, not the average.</b> A word is shorter than most spans; an average would let the
/// silence around it pull the reading down.</para>
/// <para><b>Read in one more pass over the recording, keeping only the candidates' audio.</b> The scan
/// itself keeps no samples (a 90-minute recording is 345 MB of them), so the audio is read again a
/// piece at a time and only the stretches the windows need are kept: a few seconds per candidate.</para>
/// <para>A span shorter than a second is measured in the one second centred on it; a recording shorter
/// than a second is padded with silence, which the model's own normalising ignores.</para>
/// </remarks>
public static class VoiceScoring
{
    public const int Rate = 16_000;
    public const int WindowSamples = Rate;
    public const int HopSamples = Rate / 4;

    /// <summary>Where each window starts, in samples, for a span of a recording <paramref name="total"/> samples long.</summary>
    public static IReadOnlyList<long> WindowStarts(long spanStart, long spanEnd, long total)
    {
        if (total <= WindowSamples) return [0];
        spanStart = Math.Clamp(spanStart, 0, total);
        spanEnd = Math.Clamp(spanEnd, spanStart, total);

        if (spanEnd - spanStart <= WindowSamples)
        {
            var centred = (spanStart + spanEnd) / 2 - WindowSamples / 2;
            return [Math.Clamp(centred, 0, total - WindowSamples)];
        }

        var starts = new List<long>();
        for (var at = spanStart; at + WindowSamples <= spanEnd; at += HopSamples) starts.Add(at);
        if (starts[^1] + WindowSamples < spanEnd) starts.Add(spanEnd - WindowSamples);
        return starts;
    }

    /// <summary>One reading per span, 0–1, in the order given.</summary>
    public static async Task<float[]> MeasureAsync(
        IAsyncEnumerable<ReadOnlyMemory<float>> samples, IReadOnlyList<(double Start, double End)> spans,
        double recordingSeconds, IVoiceScorer scorer, CancellationToken ct)
    {
        if (spans.Count == 0) return [];
        var total = (long)Math.Round(recordingSeconds * Rate);

        // What each span needs kept: from its first window's start to its last window's end.
        var plans = spans.Select(s =>
        {
            var starts = WindowStarts((long)Math.Floor(s.Start * Rate), (long)Math.Ceiling(s.End * Rate), total);
            var from = starts[0];
            return (Starts: starts, From: from, Kept: new float[starts[^1] + WindowSamples - from]);
        }).ToList();
        var lastNeeded = plans.Max(p => p.From + p.Kept.Length);

        long position = 0;
        await foreach (var piece in samples.WithCancellation(ct))
        {
            var span = piece.Span;
            var pieceEnd = position + span.Length;
            foreach (var plan in plans)
            {
                var from = Math.Max(plan.From, position);
                var to = Math.Min(plan.From + plan.Kept.Length, pieceEnd);
                if (to > from)
                    span.Slice((int)(from - position), (int)(to - from)).CopyTo(plan.Kept.AsSpan((int)(from - plan.From)));
            }
            position = pieceEnd;
            if (position >= lastNeeded) break;
        }

        var windows = new List<float[]>();
        var owner = new List<int>();
        for (var i = 0; i < plans.Count; i++)
            foreach (var start in plans[i].Starts)
            {
                windows.Add(plans[i].Kept.AsSpan((int)(start - plans[i].From), WindowSamples).ToArray());
                owner.Add(i);
            }

        var readings = scorer.Score(windows);
        var best = new float[plans.Count];
        for (var w = 0; w < readings.Length; w++)
            best[owner[w]] = Math.Max(best[owner[w]], readings[w]);
        return best;
    }
}
