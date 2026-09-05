using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;
using Microsoft.Extensions.Options;

namespace Ben.Video.Tests.Services;

/// <summary>
/// The media bin: what you have brought in, as distinct from what you have edited.
/// </summary>
/// <remarks>
/// There was no such thing. The Media panel's three tabs listed the timeline's own items, so "your
/// media" and "your edit" were one list: declining the insert prompt left the clip nowhere,
/// removing it from the timeline meant importing the file again, and using one source twice was
/// only possible by finding a copy of it already placed (2026-09-05 audit, media-panel-3 and F8).
/// </remarks>
public sealed class MediaBinTests
{
    private static ClipStore Store() => new(Options.Create(new VideoEditorOptions
    {
        MultiTrack = true,
        AudioTracks = true,
    }));

    private static VideoClip Source(string name = "clip", double duration = 5)
        => new() { Name = name, Duration = duration };

    [Fact]
    public void Importing_puts_a_source_in_the_bin_and_leaves_the_timeline_alone()
    {
        var store = Store();

        store.AddToBin(Source());

        Assert.Single(store.MediaBin);
        Assert.Empty(store.AllVideoClips);
        Assert.Equal(0, store.TotalDuration);
    }

    [Fact]
    public void The_same_source_is_not_added_twice()
    {
        var store = Store();
        var source = Source();

        store.AddToBin(source);
        store.AddToBin(source);

        Assert.Single(store.MediaBin);
    }

    [Fact]
    public void Adding_and_removing_are_undoable()
    {
        var store = Store();
        var source = Source();

        store.AddToBin(source);
        store.Undo();
        Assert.Empty(store.MediaBin);

        store.Redo();
        Assert.Single(store.MediaBin);

        store.RemoveFromBin(source.Id);
        Assert.Empty(store.MediaBin);

        store.Undo();
        Assert.Single(store.MediaBin);
    }

    /// <summary>
    /// Removing a card removes a card. An edit already made from that source keeps working.
    /// </summary>
    [Fact]
    public void Removing_a_source_leaves_what_was_placed_from_it()
    {
        var store = Store();
        var source = Source();
        store.AddToBin(source);

        var placed = source with { Id = Guid.NewGuid(), SourceBinId = source.Id };
        store.AddClipToTrack(store.Tracks[0].Id, placed);

        store.RemoveFromBin(source.Id);

        Assert.Empty(store.MediaBin);
        Assert.Single(store.AllVideoClips);
    }

    [Fact]
    public void The_bin_counts_how_many_times_a_source_is_on_the_timeline()
    {
        var store = Store();
        var source = Source(duration: 4);
        store.AddToBin(source);

        Assert.Equal(0, store.TimesOnTimeline(source.Id));

        foreach (var _ in Enumerable.Range(0, 3))
            store.AddClipToTrack(store.Tracks[0].Id,
                source with { Id = Guid.NewGuid(), SourceBinId = source.Id, TimelinePosition = 0 });

        Assert.Equal(3, store.TimesOnTimeline(source.Id));

        // And each placement is its own clip, laid out one after another.
        Assert.Null(store.ValidateAll());
    }

    [Fact]
    public void The_bin_is_kept_by_kind()
    {
        var store = Store();
        store.AddToBin(Source("a video"));
        store.AddToBin(new AudioClip { Name = "some audio", Duration = 30 });
        store.AddToBin(new ImageClip { Name = "a picture", Duration = 5 });

        Assert.Single(store.BinVideoClips);
        Assert.Single(store.BinAudioClips);
        Assert.Single(store.BinImageClips);
    }

    // ── Saving and opening ────────────────────────────────────────────────────

    [Fact]
    public void An_unplaced_source_survives_a_save()
    {
        var store = Store();
        store.AddToBin(Source("never placed"));

        var project = new ProjectFile
        {
            Bin = new ProjectMediaBin
            {
                VideoClips = [new ProjectVideoClip { Id = Guid.NewGuid(), Name = "never placed", Duration = 5 }],
            },
        };

        var reopened = Store();
        reopened.ReplaceFromProject(project);

        Assert.Single(reopened.MediaBin);
        Assert.Equal("never placed", reopened.MediaBin[0].Name);
    }

    /// <summary>
    /// A project written before the bin existed has no Bin section. Opening it to an empty Media
    /// panel would read as having lost the footage, so the bin is seeded from the timeline.
    /// </summary>
    [Fact]
    public void An_older_project_gets_a_bin_seeded_from_its_timeline()
    {
        var project = new ProjectFile
        {
            Tracks =
            [
                new ProjectTrack
                {
                    Id = Guid.NewGuid(), Label = "Video 1", Type = TrackType.Video, Order = 0,
                    VideoClips =
                    [
                        new ProjectVideoClip { Id = Guid.NewGuid(), Name = "a.mp4", Duration = 5, OriginalFileName = "a.mp4" },
                        new ProjectVideoClip { Id = Guid.NewGuid(), Name = "b.mp4", Duration = 5, TimelinePosition = 5, OriginalFileName = "b.mp4" },
                    ],
                },
            ],
        };

        var store = Store();
        store.ReplaceFromProject(project);

        Assert.Equal(2, store.MediaBin.Count);
        Assert.Equal(2, store.AllVideoClips.Count());

        // And the seeded entries are linked, so the cards say how they are used.
        foreach (var entry in store.MediaBin)
            Assert.Equal(1, store.TimesOnTimeline(entry.Id));
    }

    /// <summary>
    /// Two placements of one file are one source, not two — the panel used to show both because it
    /// was listing the timeline.
    /// </summary>
    [Fact]
    public void Seeding_treats_two_placements_of_one_file_as_one_source()
    {
        var project = new ProjectFile
        {
            Tracks =
            [
                new ProjectTrack
                {
                    Id = Guid.NewGuid(), Label = "Video 1", Type = TrackType.Video, Order = 0,
                    VideoClips =
                    [
                        new ProjectVideoClip { Id = Guid.NewGuid(), Name = "a.mp4", Duration = 5, OriginalFileName = "a.mp4" },
                        new ProjectVideoClip { Id = Guid.NewGuid(), Name = "a.mp4", Duration = 5, TimelinePosition = 5, OriginalFileName = "a.mp4" },
                    ],
                },
            ],
        };

        var store = Store();
        store.ReplaceFromProject(project);

        Assert.Single(store.MediaBin);
        Assert.Equal(2, store.TimesOnTimeline(store.MediaBin[0].Id));
    }

    [Fact]
    public void Opening_a_project_replaces_the_bin_rather_than_adding_to_it()
    {
        var store = Store();
        store.AddToBin(Source("from the last project"));

        store.ReplaceFromProject(new ProjectFile());

        Assert.Empty(store.MediaBin);
    }
}
