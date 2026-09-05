using Ben.Video.Editor.Models;

namespace Ben.Video.Editor.Extensions;

/// <summary>
/// The one place that says what a full video editor is, so both hosts are the same editor.
/// </summary>
/// <remarks>
/// <para>Every editing capability in <see cref="VideoEditorOptions"/> defaults to <c>false</c>,
/// which is the right default for a library nobody has configured and the wrong one for a product.
/// The site host set eleven flags by hand; the standalone WebAssembly host — the one the editor is
/// actually deployed as, and the one whose whole point is that the work happens on the user's own
/// machine — set four, and only after checking that a WebApi address was configured. A person
/// opening <c>/editors/video</c> therefore got no second track, no audio track, no transitions, no
/// titles, no effects, no ripple, no error log, no background rendering, and no project restored on
/// reload. Importing an mp3 decoded it, reported "Done" and placed it nowhere, because there was no
/// audio track to place it on (2026-09-05 audit, F2).</para>
///
/// <para>Splitting the decision in two is what fixes the second half of that: the editing set has
/// nothing to do with whether a server is reachable, so <see cref="ApplyEditingDefaults"/> is
/// applied unconditionally and <see cref="ApplyServerIntegration"/> only when there is an API to
/// talk to. A checkout with an empty <c>WebApiBaseUrl</c> is then a complete local editor rather
/// than a crippled one.</para>
///
/// <para>Flags deliberately left alone: <see cref="VideoEditorOptions.ImageClips"/>,
/// <see cref="VideoEditorOptions.InlineTrimming"/>, <see cref="VideoEditorOptions.Markers"/> and
/// <see cref="VideoEditorOptions.Snapping"/> already default to true;
/// <see cref="VideoEditorOptions.AlphaCompositing"/> is a per-project rendering choice;
/// <see cref="VideoEditorOptions.ShowDiagnostics"/> is decided per user by the host, not per
/// deployment. <c>VideoEditorHostDefaultsTests</c> holds that list and fails when a new flag is
/// added without classifying it.</para>
/// </remarks>
public static class VideoEditorHostDefaults
{
    /// <summary>
    /// Turns on everything a person can do with media that is already on their machine.
    /// </summary>
    /// <remarks>
    /// Nothing here needs a network. <see cref="VideoEditorOptions.NativeSidecar"/> included: the
    /// sidecar is a companion app on the same computer, reached over loopback, and pairing with it
    /// is the user's own decision — a host that cannot reach a WebApi has, if anything, more reason
    /// to offer it.
    /// </remarks>
    public static void ApplyEditingDefaults(VideoEditorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.MultiTrack          = true;
        options.AudioTracks         = true;
        options.Transitions         = true;
        options.TextOverlays        = true;
        options.VideoEffects        = true;
        options.RippleEdit          = true;
        options.ProjectPersistence  = true;
        options.ErrorLog            = true;
        options.BackgroundRendering = true;
        options.NativeSidecar       = true;
    }

    /// <summary>
    /// Points the editor at a WebApi: the media library, the shared asset catalog and the place a
    /// project can be saved to.
    /// </summary>
    /// <param name="options">The options being configured.</param>
    /// <param name="apiBaseUrl">
    /// Base address of the WebApi, with or without a trailing slash. When null or empty nothing is
    /// set, so the caller does not need its own guard.
    /// </param>
    public static void ApplyServerIntegration(VideoEditorOptions options, string? apiBaseUrl)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(apiBaseUrl)) return;

        var baseUrl = apiBaseUrl.TrimEnd('/');

        options.MediaLibrary        = true;
        options.MediaLibraryBaseUrl = baseUrl;
        options.AssetCatalogUrl     = baseUrl;
        options.DocumentPostUrl     = $"{baseUrl}/api/video-projects";
    }
}
