using System.Reflection;
using Ben.Video.Editor.Extensions;
using Ben.Video.Editor.Models;
using Xunit;

namespace Ben.Video.Tests.Extensions;

/// <summary>
/// The two hosts must offer the same editor, and a new capability must not be able to ship dark.
/// </summary>
/// <remarks>
/// <para>Every editing flag on <see cref="VideoEditorOptions"/> defaults to false. The site host
/// set eleven by hand and the standalone WebAssembly host set four, so the deployed
/// <c>/editors/video</c> had no second track, no audio track, no transitions, no titles, no
/// effects, no ripple, no error log, no background rendering and no project restored on reload —
/// and nothing failed, because nothing compared the two lists (2026-09-05 audit, F2).</para>
///
/// <para>The reflection test below is the part that keeps working after this session: a bool added
/// to the options class fails it until somebody decides whether it belongs to the editor
/// (<see cref="VideoEditorHostDefaults.ApplyEditingDefaults"/>) or to one of the categories in
/// <see cref="NotEditingCapabilities"/>. Listing the exceptions rather than the members is
/// deliberate — a forgotten new flag then fails closed.</para>
/// </remarks>
public sealed class VideoEditorHostDefaultsTests
{
    /// <summary>
    /// Flags that are deliberately not part of "turn the editor on", with the reason.
    /// </summary>
    private static readonly Dictionary<string, string> NotEditingCapabilities = new()
    {
        [nameof(VideoEditorOptions.ImageClips)] =
            "already true by default",
        [nameof(VideoEditorOptions.InlineTrimming)] =
            "already true by default",
        [nameof(VideoEditorOptions.Markers)] =
            "already true by default",
        [nameof(VideoEditorOptions.Snapping)] =
            "already true by default",
        [nameof(VideoEditorOptions.PauseBackgroundRenderDuringExport)] =
            "tuning for background rendering, already true by default",
        [nameof(VideoEditorOptions.EnableRoughPass)] =
            "tuning for background rendering, already true by default",
        [nameof(VideoEditorOptions.AlphaCompositing)] =
            "a rendering choice for projects with alpha footage, not a capability to switch on everywhere",
        [nameof(VideoEditorOptions.MediaLibrary)] =
            "needs a server; set by ApplyServerIntegration",
        [nameof(VideoEditorOptions.AutoPlaceFirstImport)] =
            "a behaviour preference, already true by default",
        [nameof(VideoEditorOptions.ShowDiagnostics)] =
            "decided per signed-in user by the host, not per deployment",
    };

    private static IEnumerable<PropertyInfo> BoolFlags() =>
        typeof(VideoEditorOptions)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(bool) && p.CanWrite);

    [Fact]
    public void Every_bool_flag_is_either_an_editing_capability_or_explicitly_excluded()
    {
        var options = new VideoEditorOptions();
        VideoEditorHostDefaults.ApplyEditingDefaults(options);

        var unclassified = BoolFlags()
            .Where(p => !(bool)p.GetValue(options)!)
            .Select(p => p.Name)
            .Where(name => !NotEditingCapabilities.ContainsKey(name))
            .ToList();

        Assert.True(unclassified.Count == 0,
            "New option flag(s) are off after ApplyEditingDefaults and are not listed as " +
            "deliberate exclusions, so they would ship dark on every host: " +
            string.Join(", ", unclassified));
    }

    [Fact]
    public void The_exclusion_list_only_names_flags_that_exist()
    {
        var actual = BoolFlags().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

        var stale = NotEditingCapabilities.Keys.Where(name => !actual.Contains(name)).ToList();

        Assert.True(stale.Count == 0,
            "The exclusion list names options that no longer exist: " + string.Join(", ", stale));
    }

    /// <summary>
    /// The specific set the audit found missing, named so a regression reads as itself.
    /// </summary>
    [Fact]
    public void The_editing_set_turns_on_what_the_standalone_host_was_missing()
    {
        var options = new VideoEditorOptions();

        VideoEditorHostDefaults.ApplyEditingDefaults(options);

        Assert.True(options.MultiTrack);
        Assert.True(options.AudioTracks);
        Assert.True(options.Transitions);
        Assert.True(options.TextOverlays);
        Assert.True(options.VideoEffects);
        Assert.True(options.RippleEdit);
        Assert.True(options.ProjectPersistence);
        Assert.True(options.ErrorLog);
        Assert.True(options.BackgroundRendering);
        Assert.True(options.NativeSidecar);
    }

    /// <summary>
    /// Editing does not depend on a server. This is the half the WebAssembly host got wrong: its
    /// whole configure delegate returned early when no API was configured, taking the sidecar and
    /// every editing flag with it.
    /// </summary>
    [Fact]
    public void Editing_defaults_do_not_touch_the_server_settings()
    {
        var options = new VideoEditorOptions();

        VideoEditorHostDefaults.ApplyEditingDefaults(options);

        Assert.False(options.MediaLibrary);
        Assert.Null(options.MediaLibraryBaseUrl);
        Assert.Null(options.AssetCatalogUrl);
        Assert.Null(options.DocumentPostUrl);
    }

    [Theory]
    [InlineData("https://ishaunted.com/webapi")]
    [InlineData("https://ishaunted.com/webapi/")]
    public void Server_integration_points_every_client_at_one_base_address(string configured)
    {
        var options = new VideoEditorOptions();

        VideoEditorHostDefaults.ApplyServerIntegration(options, configured);

        Assert.True(options.MediaLibrary);
        Assert.Equal("https://ishaunted.com/webapi", options.MediaLibraryBaseUrl);
        Assert.Equal("https://ishaunted.com/webapi", options.AssetCatalogUrl);
        Assert.Equal("https://ishaunted.com/webapi/api/video-projects", options.DocumentPostUrl);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void No_configured_api_leaves_the_editor_purely_local(string? configured)
    {
        var options = new VideoEditorOptions();
        VideoEditorHostDefaults.ApplyEditingDefaults(options);

        VideoEditorHostDefaults.ApplyServerIntegration(options, configured);

        Assert.False(options.MediaLibrary);
        Assert.Null(options.DocumentPostUrl);

        // The point of the fix: a local-only deployment is still the whole editor.
        Assert.True(options.MultiTrack);
        Assert.True(options.NativeSidecar);
    }
}
