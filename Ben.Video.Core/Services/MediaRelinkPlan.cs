namespace Ben.Video.Editor.Services;

/// <summary>One clip whose media is missing but could be fetched again.</summary>
/// <param name="ClipId">The clip on the timeline, or the media-bin entry.</param>
/// <param name="SourceFileId">The server file its media came from.</param>
/// <param name="Ext">The stored extension, so the fetch lands where the restore looks.</param>
/// <param name="SizeBytes">How large it was when the project was saved, if that was recorded.</param>
public sealed record MediaRelinkCandidate(Guid ClipId, Guid SourceFileId, string Ext, long? SizeBytes);

/// <summary>
/// Whether to fetch a project's missing media straight away, or ask first.
/// </summary>
/// <remarks>
/// <para>Opening a project on a second machine means re-downloading its footage. For a handful of
/// short clips that should just happen — being asked about a 4 MB download is noise. For an
/// evening's session recordings it should not: somebody on a phone tether opening a project to
/// check one title does not want half a gigabyte to start moving without being asked
/// (2026-09-05 audit, F14).</para>
///
/// <para>Pure, because "start downloading" is a decision worth being able to check without a
/// network.</para>
/// </remarks>
public static class MediaRelinkPlan
{
    /// <summary>The most this will fetch without asking, in bytes.</summary>
    /// <remarks>
    /// 50 MB — roughly one mid-size clip. Small enough that nobody minds it happening, large enough
    /// that the ordinary case of a few short clips is never interrupted by a question.
    /// </remarks>
    public const long AutomaticLimitBytes = 50L * 1024 * 1024;

    /// <summary>The total of every size that was recorded.</summary>
    public static long KnownTotalBytes(IEnumerable<MediaRelinkCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        return candidates.Sum(c => c.SizeBytes ?? 0);
    }

    /// <summary>Whether to ask before fetching.</summary>
    /// <remarks>
    /// A clip whose size was never recorded counts as a reason to ask. Not knowing how much is
    /// about to be downloaded is precisely the case where somebody should be the one to decide, and
    /// treating an unknown as zero would let an unbounded download start in silence.
    /// </remarks>
    public static bool ShouldAskFirst(IReadOnlyCollection<MediaRelinkCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count == 0) return false;

        return candidates.Any(c => c.SizeBytes is null)
            || KnownTotalBytes(candidates) > AutomaticLimitBytes;
    }

    /// <summary>
    /// How to describe the fetch to the person being asked about it.
    /// </summary>
    /// <remarks>
    /// The size is the part that decides the answer, so it is stated when it is known and its
    /// absence is stated when it is not — a prompt that quietly omits an unknown size reads as a
    /// small download.
    /// </remarks>
    public static string Describe(IReadOnlyCollection<MediaRelinkCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count == 0) return "Nothing is missing.";

        var clips = candidates.Count == 1 ? "1 clip" : $"{candidates.Count} clips";

        if (candidates.Any(c => c.SizeBytes is null))
            return $"{clips} can be downloaded from the server. Their size is not recorded.";

        return $"{clips} ({FormatSize(KnownTotalBytes(candidates))}) can be downloaded from the server.";
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / 1024d / 1024 / 1024:0.#} GB",
        >= 1024L * 1024        => $"{bytes / 1024d / 1024:0.#} MB",
        >= 1024                => $"{bytes / 1024d:0.#} KB",
        _                      => $"{bytes} bytes",
    };
}
