using System.Text.Json;
using System.Text.Json.Serialization;
using Ben.Video.Editor.Effects;
using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;
using Microsoft.Extensions.Options;

namespace Ben.Video.Tests.Services;

public sealed class ProjectServiceTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ClipStore CreateStore(Action<VideoEditorOptions>? configure = null)
    {
        var opts = new VideoEditorOptions();
        configure?.Invoke(opts);
        return new ClipStore(Options.Create(opts));
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented               = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
        Converters                  = { new JsonStringEnumConverter() },
    };

    // Round-trip via JSON the same way ProjectService.SaveAsync does
    private static ProjectFile RoundTrip(ProjectFile file)
    {
        var json = JsonSerializer.Serialize(file, JsonOpts);
        return JsonSerializer.Deserialize<ProjectFile>(json, JsonOpts)!;
    }

    // Build a minimal ProjectFile from a store (mirrors ProjectService.BuildProjectFile)
    private static ProjectFile BuildFromStore(ClipStore store, string name = "test")
    {
        return new ProjectFile
        {
            ProjectName = name,
            SavedAt     = DateTime.UtcNow,
            Tracks      = store.Tracks.Select(t => new ProjectTrack
            {
                Id         = t.Id,
                Label      = t.Label,
                Type       = t.Type,
                Order      = t.Order,
                IsMuted    = t.IsMuted,
                IsLocked   = t.IsLocked,
                VideoClips = t.VideoClips.Select(c => new ProjectVideoClip
                {
                    Id               = c.Id,
                    Name             = c.Name,
                    TimelinePosition = c.TimelinePosition,
                    Duration         = c.Duration,
                    Order            = c.Order,
                    StartTrim        = c.StartTrim,
                    EndTrim          = c.EndTrim,
                    Speed            = c.Speed,
                    Width            = c.Width,
                    Height           = c.Height,
                    Volume           = c.Volume,
                    VolumeAutomation = c.VolumeAutomation.ToList(),
                    Effects          = c.Effects,
                    IsMediaMissing   = false,
                    OriginalFileName = c.MemFsName,
                }).ToList(),
                AudioClips = t.AudioClips.Select(a => new ProjectAudioClip
                {
                    Id               = a.Id,
                    Name             = a.Name,
                    TimelinePosition = a.TimelinePosition,
                    Duration         = a.Duration,
                    Order            = a.Order,
                    StartTrim        = a.StartTrim,
                    EndTrim          = a.EndTrim,
                    Volume           = a.Volume,
                    FadeInSeconds    = a.FadeInSeconds,
                    FadeOutSeconds   = a.FadeOutSeconds,
                    VolumeAutomation = a.VolumeAutomation.ToList(),
                    IsMediaMissing   = false,
                    OriginalFileName = a.MemFsName,
                }).ToList(),
                Transitions = t.Transitions.Select(x => new ProjectTransition
                {
                    Id               = x.Id,
                    Name             = x.Name,
                    TimelinePosition = x.TimelinePosition,
                    Duration         = x.Duration,
                    Order            = x.Order,
                    Style            = x.Style,
                    FromClipId       = x.FromClipId,
                    ToClipId         = x.ToClipId,
                }).ToList(),
                TextOverlays = t.TextOverlays.Select(o => new ProjectTextOverlay
                {
                    Id               = o.Id,
                    Name             = o.Name,
                    TimelinePosition = o.TimelinePosition,
                    Duration         = o.Duration,
                    Order            = o.Order,
                    Text             = o.Text,
                    FontFamily       = o.FontFamily,
                    FontSize         = o.FontSize,
                    FontColor        = o.FontColor,
                    FontBold         = o.FontBold,
                    FontUnderline    = o.FontUnderline,
                    Runs             = o.Runs?.Select(ToProjectTextRun).ToList(),
                    HorizontalAlign  = o.HorizontalAlign.ToString().ToLowerInvariant(),
                    VerticalAlign    = o.VerticalAlign.ToString().ToLowerInvariant(),
                    OffsetX          = o.OffsetX,
                    OffsetY          = o.OffsetY,
                    FadeInSeconds    = o.FadeInSeconds,
                    FadeOutSeconds   = o.FadeOutSeconds,
                }).ToList(),
                CalloutClips = t.CalloutClips.Select(c => new ProjectCalloutClip
                {
                    Id                 = c.Id,
                    Name               = c.Name,
                    TimelinePosition   = c.TimelinePosition,
                    Duration           = c.Duration,
                    Order              = c.Order,
                    Shape              = c.Shape,
                    X                  = c.X,
                    Y                  = c.Y,
                    Width              = c.Width,
                    Height             = c.Height,
                    Rotation           = c.Rotation,
                    FillColor          = c.FillColor,
                    StrokeColor        = c.StrokeColor,
                    StrokeWidth        = c.StrokeWidth,
                    Opacity            = c.Opacity,
                    ShadowColor        = c.ShadowColor,
                    ShadowOffsetX      = c.ShadowOffsetX,
                    ShadowOffsetY      = c.ShadowOffsetY,
                    ShadowBlur         = c.ShadowBlur,
                    Text               = c.Text,
                    FontFamily         = c.FontFamily,
                    FontSize           = c.FontSize,
                    FontColor          = c.FontColor,
                    FontBold           = c.FontBold,
                    FontUnderline      = c.FontUnderline,
                    Runs               = c.Runs?.Select(ToProjectTextRun).ToList(),
                    FadeInSeconds      = c.FadeInSeconds,
                    FadeOutSeconds     = c.FadeOutSeconds,
                    ControlPointValues = new Dictionary<string, double>(c.ControlPointValues),
                }).ToList(),
            }).ToList(),
            Markers = store.Markers.ToList(),
        };
    }

    private static ProjectTextRun ToProjectTextRun(TextRun r) => new()
    {
        Text        = r.Text,
        Bold        = r.Bold,
        Underline   = r.Underline,
        Subscript   = r.Subscript,
        Superscript = r.Superscript,
        Color       = r.Color,
    };

    // ── Schema ────────────────────────────────────────────────────────────────

    [Fact]
    public void ProjectFile_DefaultSchemaVersion_Is1()
    {
        var file = new ProjectFile();
        Assert.Equal(1, file.SchemaVersion);
    }

    [Fact]
    public void ProjectFile_DefaultProjectName_IsUntitled()
    {
        var file = new ProjectFile();
        Assert.Equal("Untitled Project", file.ProjectName);
    }

    // ── JSON round-trip ───────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_EmptyProject_Preserves_SchemaAndName()
    {
        var original = new ProjectFile { ProjectName = "MyProject" };
        var restored = RoundTrip(original);

        Assert.Equal(1, restored.SchemaVersion);
        Assert.Equal("MyProject", restored.ProjectName);
        Assert.Empty(restored.Tracks);
        Assert.Empty(restored.Markers);
    }

    [Fact]
    public void RoundTrip_VideoClip_PreservesAllFields()
    {
        var store = CreateStore();
        var clipId = Guid.NewGuid();
        store.PrimaryVideoTrack!.Items.Add(new VideoClip
        {
            Id               = clipId,
            Name             = "clip.mp4",
            TimelinePosition = 1.5,
            Duration         = 10.0,
            Order            = 0,
            StartTrim        = 0.5,
            EndTrim          = 9.5,
            Speed            = 2.0,
            Width            = 1920,
            Height           = 1080,
            Volume           = 0.8,
            Effects = new ClipEffects
            {
                Brightness = 0.1,
                Contrast   = 1.2,
                Saturation = 1.5,
            },
        });

        var file     = BuildFromStore(store);
        var restored = RoundTrip(file);

        var vc = Assert.Single(restored.Tracks[0].VideoClips);
        Assert.Equal(clipId,  vc.Id);
        Assert.Equal("clip.mp4", vc.Name);
        Assert.Equal(1.5,     vc.TimelinePosition);
        Assert.Equal(10.0,    vc.Duration);
        Assert.Equal(0.5,     vc.StartTrim);
        Assert.Equal(9.5,     vc.EndTrim);
        Assert.Equal(2.0,     vc.Speed);
        Assert.Equal(1920,    vc.Width);
        Assert.Equal(1080,    vc.Height);
        Assert.Equal(0.8,     vc.Volume);
        Assert.Equal(0.1,     vc.Effects.Brightness, 6);
        Assert.Equal(1.2,     vc.Effects.Contrast,   6);
        Assert.Equal(1.5,     vc.Effects.Saturation, 6);
    }

    [Fact]
    public void RoundTrip_AudioClip_PreservesAllFields()
    {
        var store = CreateStore(o => o.AudioTracks = true);
        var audioTrack = store.Tracks.First(t => t.Type == TrackType.Audio);
        var clipId = Guid.NewGuid();
        audioTrack.Items.Add(new AudioClip
        {
            Id               = clipId,
            Name             = "music.mp3",
            TimelinePosition = 3.0,
            Duration         = 60.0,
            Order            = 0,
            Volume           = 0.6,
            FadeInSeconds    = 1.5,
            FadeOutSeconds   = 2.0,
        });

        var file     = BuildFromStore(store);
        var restored = RoundTrip(file);

        var audioProjectTrack = restored.Tracks.First(t => t.Type == TrackType.Audio);
        var ac = Assert.Single(audioProjectTrack.AudioClips);
        Assert.Equal(clipId,   ac.Id);
        Assert.Equal("music.mp3", ac.Name);
        Assert.Equal(3.0,      ac.TimelinePosition);
        Assert.Equal(60.0,     ac.Duration);
        Assert.Equal(0.6,      ac.Volume);
        Assert.Equal(1.5,      ac.FadeInSeconds);
        Assert.Equal(2.0,      ac.FadeOutSeconds);
    }

    [Fact]
    public void RoundTrip_Markers_Preserved()
    {
        var store = CreateStore();
        store.AddMarker(5.0, "Intro");
        store.AddMarker(30.0, "Chorus");

        var file     = BuildFromStore(store);
        var restored = RoundTrip(file);

        Assert.Equal(2, restored.Markers.Count);
        Assert.Contains(restored.Markers, m => m.Label == "Intro");
        Assert.Contains(restored.Markers, m => m.Label == "Chorus");
    }

    [Fact]
    public void RoundTrip_Transition_PreservesStyle()
    {
        var store = CreateStore();
        var track = store.PrimaryVideoTrack!;
        var fromId = Guid.NewGuid();
        var toId   = Guid.NewGuid();
        track.Items.Add(new Transition
        {
            Id         = Guid.NewGuid(),
            Name       = "Dissolve 1",
            Style      = TransitionStyle.Dissolve,
            Duration   = 1.0,
            FromClipId = fromId,
            ToClipId   = toId,
        });

        var file     = BuildFromStore(store);
        var restored = RoundTrip(file);

        var tx = Assert.Single(restored.Tracks[0].Transitions);
        Assert.Equal(TransitionStyle.Dissolve, tx.Style);
        Assert.Equal(fromId, tx.FromClipId);
        Assert.Equal(toId,   tx.ToClipId);
    }

    /// <summary>Item #57 T5 — the curated extra styles are new enum members inserted after the
    /// original 7; since <see cref="TransitionStyle"/> round-trips as its string NAME (not
    /// ordinal — see <c>ProjectService._jsonOptions</c>'s <c>JsonStringEnumConverter</c>), this
    /// confirms a new member specifically (not just the mechanism generically, already proven by
    /// <see cref="RoundTrip_Transition_PreservesStyle"/> above) actually round-trips correctly.</summary>
    [Fact]
    public void RoundTrip_Transition_PreservesNewCuratedStyle()
    {
        var store = CreateStore();
        var track = store.PrimaryVideoTrack!;
        track.Items.Add(new Transition
        {
            Id         = Guid.NewGuid(),
            Name       = "Circle Open 1",
            Style      = TransitionStyle.CircleOpen,
            Duration   = 1.0,
            FromClipId = Guid.NewGuid(),
            ToClipId   = Guid.NewGuid(),
        });

        var file     = BuildFromStore(store);
        var restored = RoundTrip(file);

        var tx = Assert.Single(restored.Tracks[0].Transitions);
        Assert.Equal(TransitionStyle.CircleOpen, tx.Style);
    }

    [Fact]
    public void RoundTrip_CalloutClip_PreservesTextFields()
    {
        var store = CreateStore();
        var track = store.PrimaryVideoTrack!;
        track.Items.Add(new CalloutClip
        {
            Id               = Guid.NewGuid(),
            Name             = "Label",
            Shape            = ShapeType.Rectangle,
            TimelinePosition = 0,
            Duration         = 3.0,
            Text             = "Callout label\nsecond line",
            FontFamily       = "Georgia",
            FontSize         = 36,
            FontColor        = ColorHelper.Pack(10, 20, 30, 255),
            FadeInSeconds    = 0.4,
            FadeOutSeconds   = 0.7,
            FontBold         = true,
            FontUnderline    = true,
            Runs             = [new TextRun { Text = "Callout label", Superscript = true, Color = "#00FF00" }],
        });

        var file     = BuildFromStore(store);
        var restored = RoundTrip(file);

        var cc = Assert.Single(restored.Tracks[0].CalloutClips);
        Assert.Equal("Callout label\nsecond line", cc.Text);
        Assert.Equal("Georgia", cc.FontFamily);
        Assert.Equal(36,        cc.FontSize);
        Assert.Equal(ColorHelper.Pack(10, 20, 30, 255), cc.FontColor);
        Assert.Equal(0.4, cc.FadeInSeconds);
        Assert.Equal(0.7, cc.FadeOutSeconds);
        Assert.True(cc.FontBold);
        Assert.True(cc.FontUnderline);
        var run = Assert.Single(cc.Runs!);
        Assert.Equal("Callout label", run.Text);
        Assert.True(run.Superscript);
        Assert.Equal("#00FF00", run.Color);
    }

    [Fact]
    public void RoundTrip_CalloutClip_NullText_StaysNull()
    {
        var store = CreateStore();
        var track = store.PrimaryVideoTrack!;
        track.Items.Add(new CalloutClip
        {
            Id               = Guid.NewGuid(),
            Shape            = ShapeType.Rectangle,
            TimelinePosition = 0,
            Duration         = 3.0,
            Text             = null,
        });

        var file     = BuildFromStore(store);
        var restored = RoundTrip(file);

        var cc = Assert.Single(restored.Tracks[0].CalloutClips);
        Assert.Null(cc.Text);
    }

    [Fact]
    public void RoundTrip_TextOverlay_PreservesFields()
    {
        var store = CreateStore();
        var track = store.PrimaryVideoTrack!;
        track.Items.Add(new TextOverlay
        {
            Id               = Guid.NewGuid(),
            Name             = "Title",
            Text             = "Hello World",
            FontFamily       = "Arial",
            FontSize         = 48,
            FontColor        = "#ff0000",
            TimelinePosition = 0,
            Duration         = 5.0,
            FontBold         = true,
            FontUnderline    = true,
            Runs             = [new TextRun { Text = "Hello ", Bold = true }, new TextRun { Text = "World" }],
        });

        var file     = BuildFromStore(store);
        var restored = RoundTrip(file);

        var to = Assert.Single(restored.Tracks[0].TextOverlays);
        Assert.Equal("Hello World", to.Text);
        Assert.Equal("Arial",       to.FontFamily);
        Assert.Equal(48,            to.FontSize);
        Assert.Equal("#ff0000",     to.FontColor);
        Assert.True(to.FontBold);
        Assert.True(to.FontUnderline);
        Assert.Equal(2, to.Runs!.Count);
        Assert.Equal("Hello ", to.Runs[0].Text);
        Assert.True(to.Runs[0].Bold);
        Assert.Equal("World", to.Runs[1].Text);
        Assert.False(to.Runs[1].Bold);
    }

    // ── ClipStore.ReplaceFromProject ──────────────────────────────────────────

    [Fact]
    public void ReplaceFromProject_SetsIsMediaMissing_True()
    {
        var store = CreateStore();
        var original = new ProjectFile
        {
            Tracks =
            [
                new ProjectTrack
                {
                    Id    = Guid.NewGuid(),
                    Label = "Video 1",
                    Type  = TrackType.Video,
                    Order = 0,
                    VideoClips =
                    [
                        new ProjectVideoClip
                        {
                            Id             = Guid.NewGuid(),
                            Name           = "clip.mp4",
                            Duration       = 5.0,
                            IsMediaMissing = false,  // was false in saved file
                        }
                    ],
                }
            ],
        };

        store.ReplaceFromProject(original);

        var clip = Assert.Single(store.PrimaryVideoTrack!.VideoClips);
        Assert.True(clip.IsMediaMissing);
    }

    [Fact]
    public void ReplaceFromProject_ClearsUndoRedoStacks()
    {
        var store = CreateStore();

        // Build some undo history
        var clipId = Guid.NewGuid();
        store.PrimaryVideoTrack!.Items.Add(new VideoClip
            { Id = clipId, Name = "c", Duration = 5 });
        store.RemoveClip(clipId);
        Assert.True(store.CanUndo);

        var project = new ProjectFile
        {
            Tracks = [new ProjectTrack
            {
                Id    = Guid.NewGuid(),
                Label = "Video 1",
                Type  = TrackType.Video,
                Order = 0,
            }]
        };

        store.ReplaceFromProject(project);

        Assert.False(store.CanUndo);
        Assert.False(store.CanRedo);
    }

    [Fact]
    public void ReplaceFromProject_ReplacesAllTracks()
    {
        var store = CreateStore();
        // Add an extra clip to the default track
        store.PrimaryVideoTrack!.Items.Add(new VideoClip
            { Id = Guid.NewGuid(), Name = "old.mp4", Duration = 5 });

        var newTrackId = Guid.NewGuid();
        var project = new ProjectFile
        {
            Tracks = [new ProjectTrack
            {
                Id    = newTrackId,
                Label = "Imported Track",
                Type  = TrackType.Video,
                Order = 0,
                VideoClips = [new ProjectVideoClip
                {
                    Id       = Guid.NewGuid(),
                    Name     = "new.mp4",
                    Duration = 8.0,
                }],
            }],
        };

        store.ReplaceFromProject(project);

        Assert.Single(store.VideoTracks);
        var vt = store.VideoTracks.First();
        Assert.Equal(newTrackId, vt.Id);
        Assert.Equal("Imported Track", vt.Label);
        var clip = Assert.Single(vt.VideoClips);
        Assert.Equal("new.mp4", clip.Name);
        Assert.True(clip.IsMediaMissing);
    }

    [Fact]
    public void ReplaceFromProject_RestoresTextOverlayAndCalloutRuns()
    {
        // Exercises ClipStore.RestoreTextOverlay/RestoreCalloutClip directly (item #16, phase 115) —
        // unlike BuildFromStore's round-trip tests above, which only prove the DTO/JSON shape.
        var store = CreateStore();
        var project = new ProjectFile
        {
            Tracks = [new ProjectTrack
            {
                Id    = Guid.NewGuid(),
                Label = "V1",
                Type  = TrackType.Video,
                Order = 0,
                TextOverlays = [new ProjectTextOverlay
                {
                    Id            = Guid.NewGuid(),
                    Text          = "Hello",
                    FontBold      = true,
                    FontUnderline = true,
                    Runs = [new ProjectTextRun { Text = "Hello", Subscript = true, Color = "#123456" }],
                }],
                CalloutClips = [new ProjectCalloutClip
                {
                    Id            = Guid.NewGuid(),
                    Shape         = ShapeType.Star,
                    Text          = "Label",
                    FontBold      = true,
                    FontUnderline = true,
                    Runs = [new ProjectTextRun { Text = "Label", Superscript = true }],
                }],
            }],
        };

        store.ReplaceFromProject(project);

        var overlay = Assert.Single(store.AllTextOverlays);
        Assert.True(overlay.FontBold);
        Assert.True(overlay.FontUnderline);
        var overlayRun = Assert.Single(overlay.Runs!);
        Assert.True(overlayRun.Subscript);
        Assert.Equal("#123456", overlayRun.Color);

        var callout = Assert.Single(store.AllCalloutClips);
        Assert.True(callout.FontBold);
        Assert.True(callout.FontUnderline);
        var calloutRun = Assert.Single(callout.Runs!);
        Assert.True(calloutRun.Superscript);
    }

    [Fact]
    public void ReplaceFromProject_RestoresMarkers()
    {
        var store   = CreateStore();
        var markerId = Guid.NewGuid();
        var project = new ProjectFile
        {
            Tracks  = [new ProjectTrack { Id = Guid.NewGuid(), Label = "V1",
                                          Type = TrackType.Video, Order = 0 }],
            Markers = [new TimelineMarker
            {
                Id          = markerId,
                Label       = "Restored Marker",
                TimeSeconds = 12.5,
                Color       = "#3b82f6",
            }],
        };

        store.ReplaceFromProject(project);

        var m = Assert.Single(store.Markers);
        Assert.Equal(markerId,   m.Id);
        Assert.Equal("Restored Marker", m.Label);
        Assert.Equal(12.5, m.TimeSeconds);
    }

    // ── ProjectOptionsSnapshot ────────────────────────────────────────────────

    [Fact]
    public void ProjectOptionsSnapshot_Defaults_MatchOptions()
    {
        var snap = new ProjectOptionsSnapshot();
        Assert.False(snap.MultiTrack);
        Assert.False(snap.AudioTracks);
        Assert.False(snap.Transitions);
        Assert.False(snap.TextOverlays);
        Assert.False(snap.VideoEffects);
        Assert.True(snap.Markers);          // matches VideoEditorOptions default
        Assert.False(snap.InlineTrimming);
    }

    // ── ProjectTrack helpers ──────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_MultipleTracksInOrder()
    {
        var file = new ProjectFile
        {
            Tracks =
            [
                new ProjectTrack { Id = Guid.NewGuid(), Label = "V2", Type = TrackType.Video, Order = 1 },
                new ProjectTrack { Id = Guid.NewGuid(), Label = "V1", Type = TrackType.Video, Order = 0 },
                new ProjectTrack { Id = Guid.NewGuid(), Label = "A1", Type = TrackType.Audio, Order = 2 },
            ],
        };

        var store = CreateStore();
        store.ReplaceFromProject(file);

        // Tracks should be sorted by Order on restore
        Assert.Equal(3, store.Tracks.Count);
        Assert.Equal("V1", store.Tracks[0].Label);
        Assert.Equal("V2", store.Tracks[1].Label);
        Assert.Equal("A1", store.Tracks[2].Label);
    }
}
