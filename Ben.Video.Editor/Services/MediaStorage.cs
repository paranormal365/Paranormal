namespace Ben.Video.Editor.Services;

/// <summary>
/// Where a clip's stored media actually is.
/// </summary>
/// <remarks>
/// <para>Not always under the clip's own id. A clip placed from the media bin shares the copy the
/// bin entry already holds rather than making a second one, so its bytes are under the bin entry's
/// id — that is what stopped every import accumulating a duplicate per placement.</para>
///
/// <para>Anything that opens a clip's media has to know both, and anything that knows only the
/// first works perfectly for a clip imported straight onto the timeline and finds nothing for a
/// clip placed from the bin. The live player was written that way and played black for a clip
/// that was plainly there, which is how this became a rule with a name (phase 12).</para>
/// </remarks>
public static class MediaStorage
{
    /// <summary>
    /// The ids a clip's media may be stored under, in the order worth trying.
    /// </summary>
    /// <param name="clipId">The clip's own id.</param>
    /// <param name="sourceBinId">The bin entry it was placed from, when it was.</param>
    /// <remarks>
    /// The clip's own copy first: a clip whose media was replaced has its own, and it is the newer
    /// answer.
    /// </remarks>
    public static IReadOnlyList<Guid> CandidateIds(Guid clipId, Guid? sourceBinId) =>
        sourceBinId is { } binId && binId != clipId && binId != Guid.Empty
            ? [clipId, binId]
            : [clipId];
}
