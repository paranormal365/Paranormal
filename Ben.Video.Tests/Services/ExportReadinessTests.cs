using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Refusing an export that cannot succeed, and saying why.
/// </summary>
/// <remarks>
/// A clip whose file the browser no longer holds used to be met partway through the render: it
/// stopped at that clip's percentage and stayed there, with no message and nothing to act on,
/// while the person watched a progress bar that had stopped meaning anything
/// (2026-09-06 large-media walk).
/// </remarks>
public sealed class ExportReadinessTests
{
    private static VideoClip Video(string name = "clip", string? memFs = "clip.mp4", bool missing = false) =>
        new() { Name = name, Duration = 5, MemFsName = memFs, IsMediaMissing = missing };

    private static ImageClip Image(string name = "still", string? memFs = "still.png", bool missing = false) =>
        new() { Name = name, Duration = 5, MemFsName = memFs, IsMediaMissing = missing };

    private static AudioClip Audio(string name = "sound", string? memFs = "sound.m4a", bool missing = false) =>
        new() { Name = name, Duration = 5, MemFsName = memFs, IsMediaMissing = missing };

    private static TimelineTrack Track(TrackType type, params TrackItem[] items)
    {
        var track = new TimelineTrack { Type = type };
        track.Items.AddRange(items);
        return track;
    }

    private static TimelineTrack VideoTrack(params TrackItem[] items) => Track(TrackType.Video, items);
    private static TimelineTrack AudioTrack(params TrackItem[] items) => Track(TrackType.Audio, items);

    // ── Ready ─────────────────────────────────────────────────────────────────

    [Fact]
    public void A_timeline_whose_media_is_all_present_can_be_exported()
    {
        var readiness = ExportReadiness.Check([VideoTrack(Video(), Image()), AudioTrack(Audio())]);

        Assert.True(readiness.CanExport);
        Assert.Null(readiness.Explanation);
    }

    [Fact]
    public void An_empty_timeline_is_not_blocked_by_this()
    {
        Assert.True(ExportReadiness.Check([]).CanExport);
        Assert.True(ExportReadiness.Check(null).CanExport);
    }

    // ── Blocked ───────────────────────────────────────────────────────────────

    /// <summary>
    /// The state a project reopened on another machine arrives in.
    /// </summary>
    [Fact]
    public void A_clip_the_editor_already_knows_is_missing_blocks_the_export()
    {
        var readiness = ExportReadiness.Check([VideoTrack(Video("porch camera", missing: true))]);

        Assert.False(readiness.CanExport);
        Assert.Contains("porch camera", readiness.Explanation);
    }

    /// <summary>
    /// And the other way it ends up the same: nothing mounted for ffmpeg to read.
    /// </summary>
    [Fact]
    public void A_clip_with_no_mounted_source_blocks_the_export()
    {
        Assert.False(ExportReadiness.Check([VideoTrack(Video(memFs: null))]).CanExport);
        Assert.False(ExportReadiness.Check([VideoTrack(Video(memFs: ""))]).CanExport);
    }

    [Fact]
    public void A_missing_image_blocks_it_too() =>
        Assert.False(ExportReadiness.Check([VideoTrack(Image(missing: true))]).CanExport);

    /// <summary>
    /// A missing audio clip was skipped silently, so the export finished looking complete and was
    /// missing its narration — worse than being told.
    /// </summary>
    [Fact]
    public void A_missing_audio_clip_blocks_it_rather_than_being_dropped()
    {
        var readiness = ExportReadiness.Check([VideoTrack(Video()), AudioTrack(Audio("narration", missing: true))]);

        Assert.False(readiness.CanExport);
        Assert.Contains("narration", readiness.Explanation);
    }

    /// <summary>
    /// Titles, callouts and transitions are drawn rather than read from a file, so they have
    /// nothing to be missing and must never block a render.
    /// </summary>
    [Fact]
    public void Drawn_layers_are_not_media_and_never_block_it()
    {
        var overlay = new TextOverlay { Name = "Title", Duration = 3 };
        var callout = new CalloutClip { Name = "Callout", Duration = 3 };

        Assert.True(ExportReadiness.Check([VideoTrack(Video(), overlay, callout)]).CanExport);
    }

    // ── What it says ──────────────────────────────────────────────────────────

    /// <summary>
    /// Names, not a count: "one clip is missing its media" sends somebody hunting along a timeline.
    /// </summary>
    [Fact]
    public void The_message_names_the_clip_and_says_what_to_do()
    {
        var message = ExportReadiness.Check([VideoTrack(Video("basement pass 2", missing: true))]).Explanation;

        Assert.Contains("basement pass 2", message);
        Assert.Contains("Replace Media", message);
    }

    [Fact]
    public void One_clip_reads_as_one_and_several_read_as_several()
    {
        var one = ExportReadiness.Check([VideoTrack(Video("a", missing: true))]).Explanation;
        var two = ExportReadiness.Check([VideoTrack(Video("a", missing: true), Video("b", missing: true))]).Explanation;

        Assert.Contains("is missing its media", one);
        Assert.Contains("are missing their media", two);
    }

    /// <summary>A wall of names is its own kind of unhelpful.</summary>
    [Fact]
    public void A_long_list_is_trimmed_and_counted()
    {
        var clips = Enumerable.Range(1, 7).Select(i => (TrackItem)Video($"clip {i}", missing: true)).ToArray();

        var message = ExportReadiness.Check([VideoTrack(clips)]).Explanation;

        Assert.Contains("clip 1", message);
        Assert.DoesNotContain("clip 7", message);
        Assert.Contains("4 more", message);
    }

    [Fact]
    public void A_clip_with_no_name_is_still_pointed_at() =>
        Assert.Contains(
            "Untitled clip",
            ExportReadiness.Check([VideoTrack(Video("  ", missing: true))]).Explanation);

    [Fact]
    public void Every_blocker_is_reported_not_just_the_first()
    {
        var readiness = ExportReadiness.Check(
            [VideoTrack(Video("a", missing: true), Video("b")), AudioTrack(Audio("c", memFs: null))]);

        Assert.Equal(2, readiness.Blockers.Count);
    }
}
