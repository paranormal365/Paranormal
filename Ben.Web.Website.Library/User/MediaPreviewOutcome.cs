namespace Ben.Web.Website.Library.User;

/// <summary>
/// What happened when a file tried to show itself.
/// </summary>
/// <remarks>
/// This exists because the three states looked identical on screen. A preview component that
/// catches its own failure and falls back to a badge tells a viewer nothing: a photograph whose
/// bytes have gone missing, a .json that was never going to render, and a one-pixel test image
/// all came out as the same quiet gap. Whoever is looking then has to guess whether the page is
/// broken, the file is broken, or nothing is wrong at all.
/// </remarks>
public enum MediaPreviewOutcome
{
    /// <summary>Still fetching.</summary>
    Loading = 0,

    /// <summary>Rendered — an image, a video, or a waveform.</summary>
    Shown = 1,

    /// <summary>Nothing is wrong; this kind of file simply has no in-page preview.</summary>
    NotPreviewable = 2,

    /// <summary>
    /// The file should have shown and did not — the bytes did not come back.
    /// </summary>
    /// <remarks>
    /// Usually a record pointing at storage that no longer holds the file. Worth saying out loud
    /// rather than swallowing: it is the one outcome here that means something is actually wrong.
    /// </remarks>
    Unavailable = 3,
}
