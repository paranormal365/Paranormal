namespace Ben.Service.Models.Entities;

/// <summary>One placed clip in a Phase E mixer export — offset plus its track's gain/pan/mute/solo.</summary>
public record MixTrackExportInput(
    Guid CaseFileId,
    double OffsetSeconds,
    double GainDb,
    double Pan,
    bool Muted,
    bool Solo);

/// <summary>Request body for exporting a multi-track mix down to a single audio file.</summary>
public record ExportAudioMixRequest(IReadOnlyList<MixTrackExportInput> Tracks);

/// <summary>
/// Which tracks of a mix are heard.
/// </summary>
/// <remarks>
/// Mute and solo interact, and the rule has to be identical in the browser and on the server or the
/// preview is a different mix from the export — which is the one thing a preview must never be. The
/// server has always had this rule; stating it here lets the mixer page use the same one rather
/// than a copy that can drift.
/// </remarks>
public static class MixAudibility
{
    /// <summary>
    /// The tracks that will be heard: not muted, and — as soon as anything is soloed — soloed.
    /// </summary>
    /// <remarks>
    /// Solo is exclusive by convention: the moment one track is soloed, every track that is not
    /// becomes silent. With nothing soloed, mute alone decides.
    /// </remarks>
    public static IReadOnlyList<T> Audible<T>(
        IReadOnlyList<T> tracks, Func<T, bool> isMuted, Func<T, bool> isSoloed)
    {
        var anySolo = tracks.Any(isSoloed);
        return [.. tracks.Where(t => !isMuted(t) && (!anySolo || isSoloed(t)))];
    }

    /// <summary>The audible tracks of an export request.</summary>
    public static IReadOnlyList<MixTrackExportInput> Audible(IReadOnlyList<MixTrackExportInput> tracks)
        => Audible(tracks, t => t.Muted, t => t.Solo);
}
