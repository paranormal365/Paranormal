namespace Ben.Video.Editor.Services;

/// <summary>
/// Item #62, phase 170 — decides which URLs of a *replaced* thumbnail strip are actually safe to
/// revoke.
///
/// <para>The bug this exists to prevent: a thumbnail strip is a <c>List&lt;string&gt;</c>, and both
/// clip-duplication paths (<c>VideoEditor.AddClipToTimeline</c> and <c>ClipStore.DuplicateClip</c>)
/// copy that <i>list</i> while sharing the <i>strings</i> — and a string here is a handle to a
/// browser blob. Two placements of the same source therefore point at one set of blobs, and the
/// import-status row holds <c>thumbs[0]</c> as its own <c>PreviewUrl</c> on top of that. Revoking
/// "the previous strip" wholesale, as the lazy thumbnail fill used to, kills blobs that other live
/// DOM nodes are still rendering — a 404 with no exception and no C#-side symptom.</para>
///
/// <para>Kept as a pure function rather than folded into the caller because the interesting part is
/// entirely a set-membership decision: everything else in that code path is interop.</para>
/// </summary>
public static class ThumbnailRevokePlan
{
    /// <summary>
    /// The subset of <paramref name="previous"/> that nothing references any more, in first-seen
    /// order and de-duplicated (a strip can legitimately repeat a URL — e.g. a clip shorter than
    /// the thumbnail interval — and revoking the same handle twice trips phase 144's
    /// double-revoke diagnostic).
    /// </summary>
    /// <param name="previous">The strip being replaced.</param>
    /// <param name="stillReferenced">
    /// Every URL still reachable from somewhere: the replacement strip, every OTHER clip's strip,
    /// and any non-clip holder (the import rows' <c>PreviewUrl</c>). Callers must gather this
    /// *after* assigning the replacement, so a URL carried over into the new strip is retained.
    /// </param>
    public static IReadOnlyList<string> Orphaned(
        IEnumerable<string> previous,
        IEnumerable<string> stillReferenced)
    {
        var keep = new HashSet<string>(stillReferenced, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var orphans = new List<string>();

        foreach (var url in previous)
        {
            if (string.IsNullOrEmpty(url)) continue;
            if (keep.Contains(url)) continue;
            if (!seen.Add(url)) continue;
            orphans.Add(url);
        }

        return orphans;
    }
}
