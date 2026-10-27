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
        => Score(await CandidateAudio.CollectAsync(samples, spans, recordingSeconds, ct), scorer);

    /// <summary>One reading per clip: the highest over its windows.</summary>
    public static float[] Score(IReadOnlyList<CandidateAudio> clips, IVoiceScorer scorer)
    {
        if (clips.Count == 0) return [];
        var windows = new List<float[]>();
        var owner = new List<int>();
        for (var i = 0; i < clips.Count; i++)
            foreach (var start in clips[i].WindowStarts)
            {
                windows.Add(clips[i].Samples.AsSpan((int)(start - clips[i].From), WindowSamples).ToArray());
                owner.Add(i);
            }

        var readings = scorer.Score(windows);
        var best = new float[clips.Count];
        for (var w = 0; w < readings.Length; w++)
            best[owner[w]] = Math.Max(best[owner[w]], readings[w]);
        return best;
    }
}

/// <summary>
/// The audio of one candidate, read again after the scan (phases 9b and 11): the candidate's span, widened
/// to what VoiceNet's windows need, at 16 kHz mono. Past the recording's end it is silence.
/// </summary>
/// <param name="From">The sample the kept audio starts at.</param>
/// <param name="WindowStarts">Where VoiceNet's one-second windows start.</param>
public sealed record CandidateAudio(long From, float[] Samples, IReadOnlyList<long> WindowStarts)
{
    /// <summary>
    /// Reads the recording once more, a piece at a time, keeping only what each span needs: a few
    /// seconds per candidate, where the whole recording would be hundreds of megabytes.
    /// </summary>
    public static async Task<IReadOnlyList<CandidateAudio>> CollectAsync(
        IAsyncEnumerable<ReadOnlyMemory<float>> samples, IReadOnlyList<(double Start, double End)> spans,
        double recordingSeconds, CancellationToken ct)
    {
        if (spans.Count == 0) return [];
        var rate = VoiceScoring.Rate;
        var total = (long)Math.Round(recordingSeconds * rate);

        var clips = spans.Select(s =>
        {
            var starts = VoiceScoring.WindowStarts((long)Math.Floor(s.Start * rate), (long)Math.Ceiling(s.End * rate), total);
            var from = Math.Min(starts[0], Math.Max(0, (long)Math.Floor(s.Start * rate)));
            var to = Math.Max(starts[^1] + VoiceScoring.WindowSamples, Math.Min(total, (long)Math.Ceiling(s.End * rate)));
            return new CandidateAudio(from, new float[to - from], starts);
        }).ToList();
        var lastNeeded = clips.Max(c => c.From + c.Samples.Length);

        long position = 0;
        await foreach (var piece in samples.WithCancellation(ct))
        {
            var span = piece.Span;
            var pieceEnd = position + span.Length;
            foreach (var clip in clips)
            {
                var from = Math.Max(clip.From, position);
                var to = Math.Min(clip.From + clip.Samples.Length, pieceEnd);
                if (to > from)
                    span.Slice((int)(from - position), (int)(to - from)).CopyTo(clip.Samples.AsSpan((int)(from - clip.From)));
            }
            position = pieceEnd;
            if (position >= lastNeeded) break;
        }
        return clips;
    }

    /// <summary>The kept samples between two times in the recording, clamped to what was kept.</summary>
    public ReadOnlySpan<float> Between(double startSeconds, double endSeconds)
    {
        var a = Math.Clamp((long)Math.Floor(startSeconds * VoiceScoring.Rate) - From, 0, Samples.Length);
        var b = Math.Clamp((long)Math.Ceiling(endSeconds * VoiceScoring.Rate) - From, a, Samples.Length);
        return Samples.AsSpan((int)a, (int)(b - a));
    }
}
