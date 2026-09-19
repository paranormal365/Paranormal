namespace Ben.Web.Website.Library.Manage.Audio;

/// <summary>
/// Which stretch of which recording the region explorer is currently showing.
/// </summary>
/// <remarks>
/// <para>The explorer downloads one region's audio and then decides whether to do it again by
/// asking whether it had ever loaded anything: <c>if (!Visible || _source is not null) return;</c>.
/// So the first region a person explored was the audio they heard for every region after it, while
/// the notes panel, the title and — worse — the Save button all used the new region's coordinates.
/// Listen to the second region, save it, and the file that arrives is not the sound that was
/// playing (2026-09-06 audio walk, finding H).</para>
///
/// <para>The question is not "have I loaded anything" but "have I loaded <i>this</i>", which is a
/// comparison of three numbers. Stated here so it can be tested without a browser, and so the Save
/// path can use the key that was actually loaded rather than whatever the parameter says now.</para>
/// </remarks>
public readonly record struct RegionExplorerKey(Guid FileId, double Start, double End)
{
    /// <summary>
    /// How far two boundaries may differ and still be the same load.
    /// </summary>
    /// <remarks>
    /// Region bounds are floating-point seconds that make a round trip through the browser, so
    /// exact equality would re-download the same audio on every render. Ten milliseconds is well
    /// below anything a person can hear or place by dragging.
    /// </remarks>
    public const double ToleranceSeconds = 0.01;

    /// <summary>The key for a region, or null when there is nothing to show.</summary>
    public static RegionExplorerKey? For(Guid fileId, double? start, double? end)
        => fileId == Guid.Empty || start is not { } s || end is not { } e || e <= s
            ? null
            : new RegionExplorerKey(fileId, s, e);

    /// <summary>
    /// Whether the explorer must fetch audio again to be showing <paramref name="wanted"/>.
    /// </summary>
    /// <param name="loaded">What is on screen now, or null when nothing has been loaded.</param>
    /// <param name="wanted">What the parameters now say should be on screen.</param>
    public static bool ShouldReload(RegionExplorerKey? loaded, RegionExplorerKey? wanted)
    {
        if (wanted is not { } target) return false;      // nothing to show
        if (loaded  is not { } current) return true;     // nothing shown yet

        return current.FileId != target.FileId
            || Math.Abs(current.Start - target.Start) >= ToleranceSeconds
            || Math.Abs(current.End   - target.End)   >= ToleranceSeconds;
    }

    /// <summary>How long the stretch is.</summary>
    public double DurationSeconds => End - Start;
}
