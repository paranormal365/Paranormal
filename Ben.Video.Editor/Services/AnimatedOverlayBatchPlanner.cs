namespace Ben.Video.Editor.Services;

/// <summary>
/// Item #59-#65 flakiness investigation, phase 146 — how many PNG frames an animated overlay
/// export (raster ClipArt motion path, or an SVG clip with control points) renders/writes/encodes
/// at once. Before this phase, every frame for the WHOLE clip (<c>duration × fps</c> — e.g. 1800
/// frames for a 60s@30fps clip) was rendered in one JS batch call and written to MEMFS before a
/// single ffmpeg exec ever ran — the dominant source of MEMFS/heap pressure in the whole export
/// pipeline (confirmed via the item #38 design doc's own ranking, never actually measured until
/// <c>MemFsLedger</c>, phase 141, existed to measure it).
///
/// <see cref="BatchSize"/> picks a frame count whose *uncompressed* RGBA canvas footprint fits a
/// fixed byte budget — a deliberately conservative estimate, since actual PNG-encoded bytes are
/// never larger than the raw canvas and are usually much smaller. Clamped to a sane range so a
/// tiny canvas doesn't produce an absurdly large batch (bounds JS/.NET marshal size too, not just
/// MEMFS) and a huge canvas doesn't produce a batch of 0 or 1 (bounds the number of intermediate
/// ffmpeg execs for a long clip).
/// </summary>
public static class AnimatedOverlayBatchPlanner
{
    public const long DefaultByteBudget = 64L * 1024 * 1024; // 64 MB
    public const int MinBatchSize = 5;
    public const int MaxBatchSize = 240; // 8s at 30fps — a ceiling regardless of resolution

    public static int BatchSize(int width, int height, long byteBudget = DefaultByteBudget)
    {
        var bytesPerFrame = Math.Max(1L, (long)width * height * 4); // RGBA, uncompressed
        var raw = (int)Math.Min(int.MaxValue, byteBudget / bytesPerFrame);
        return Math.Clamp(raw, MinBatchSize, MaxBatchSize);
    }

    /// <summary>Splits <paramref name="frameCount"/> frames into consecutive, non-overlapping
    /// batches of at most <paramref name="batchSize"/> each (the last one a possibly-shorter
    /// remainder). Every frame appears in exactly one batch, in order.</summary>
    public static IEnumerable<(int BatchIndex, int Start, int Count)> Batches(int frameCount, int batchSize)
    {
        if (frameCount <= 0 || batchSize <= 0) yield break;

        var batchIndex = 0;
        for (var start = 0; start < frameCount; start += batchSize)
        {
            var count = Math.Min(batchSize, frameCount - start);
            yield return (batchIndex, start, count);
            batchIndex++;
        }
    }
}
