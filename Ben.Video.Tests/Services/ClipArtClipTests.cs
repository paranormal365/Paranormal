using Ben.Video.Editor.Models;
using Ben.Video.Editor.Models.Assets;
using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

public sealed class ClipArtClipTests
{
    // ── ClipArtClip model ──────────────────────────────────────────────────────

    [Fact]
    public void ClipArtClip_Defaults_HaveExpectedValues()
    {
        var clip = new ClipArtClip();
        Assert.Equal(0.1,  clip.X);
        Assert.Equal(0.1,  clip.Y);
        Assert.Equal(0.2,  clip.Width);
        Assert.Equal(-1.0, clip.Height);     // preserve aspect ratio
        Assert.Equal(0.0,  clip.Rotation);
        Assert.Equal(1.0,  clip.Opacity);
        Assert.Null(clip.TintColor);
        Assert.Empty(clip.ControlPointValues);
        Assert.Empty(clip.ControlPointColors);
        Assert.Equal(AssetSource.SharedCatalog, clip.AssetSource);
    }

    [Fact]
    public void ClipArtClip_ControlPointValues_CanBeSet()
    {
        var clip = new ClipArtClip();
        clip.ControlPointValues["stroke"] = 0.0;
        clip.ControlPointValues["fill"]   = 1.0;
        Assert.Equal(0.0, clip.ControlPointValues["stroke"]);
        Assert.Equal(1.0, clip.ControlPointValues["fill"]);
    }

    [Fact]
    public void ClipArtClip_ControlPointColors_CanBeSet()
    {
        var clip = new ClipArtClip();
        clip.ControlPointColors["outline"] = "#FF0000";
        Assert.Equal("#FF0000", clip.ControlPointColors["outline"]);
    }

    [Fact]
    public void ClipArtClip_SettingsSnapshot_StoredAtAddTime()
    {
        var settings = new VideoAssetSettings
        {
            AllowResize   = true,
            AllowMotion   = true,
            AllowOpacity  = false,
        };
        var clip = new ClipArtClip { Settings = settings };
        Assert.True(clip.Settings.AllowResize);
        Assert.True(clip.Settings.AllowMotion);
        Assert.False(clip.Settings.AllowOpacity);
    }

    [Fact]
    public void ClipArtClip_ControlPoints_NullByDefault()
    {
        var clip = new ClipArtClip();
        Assert.Null(clip.ControlPoints);
    }

    [Fact]
    public void ClipArtClip_ControlPoints_CanHoldSvgPoints()
    {
        var clip = new ClipArtClip
        {
            ControlPoints = new List<SvgControlPoint>
            {
                new() { PointId = "arm",     Type = SvgControlPointType.Move },
                new() { PointId = "outline", Type = SvgControlPointType.StrokeAlpha, MinValue = 0, MaxValue = 1, DefaultValue = 1 },
                new() { PointId = "fill",    Type = SvgControlPointType.FillAlpha,   MinValue = 0, MaxValue = 1, DefaultValue = 1 },
            },
        };
        Assert.Equal(3, clip.ControlPoints!.Count);
        Assert.Equal(SvgControlPointType.StrokeAlpha, clip.ControlPoints[1].Type);
        Assert.Equal(SvgControlPointType.FillAlpha,   clip.ControlPoints[2].Type);
    }

    // ── ClipStore operations ───────────────────────────────────────────────────

    private static ClipStore MakeStore()
    {
        return new ClipStore(
            Microsoft.Extensions.Options.Options.Create(new VideoEditorOptions()));
    }

    [Fact]
    public void AddClipArtClip_AppearsInAllClipArtClips()
    {
        var store = MakeStore();
        var clip  = new ClipArtClip { Name = "Ghost", AssetId = "asset-1", Duration = 5 };
        store.AddClipArtClip(clip);
        Assert.Single(store.AllClipArtClips);
        Assert.Equal("Ghost", store.AllClipArtClips.First().Name);
    }

    [Fact]
    public void AddClipArtClip_AssignsOrder()
    {
        var store = MakeStore();
        store.AddClipArtClip(new ClipArtClip { AssetId = "a", Duration = 2 });
        store.AddClipArtClip(new ClipArtClip { AssetId = "b", Duration = 2 });
        var clips = store.AllClipArtClips.ToList();
        Assert.Equal(0, clips[0].Order);
        Assert.Equal(1, clips[1].Order);
    }

    [Fact]
    public void RemoveClipArtClip_RemovesFromStore()
    {
        var store = MakeStore();
        var clip  = new ClipArtClip { AssetId = "x", Duration = 3 };
        store.AddClipArtClip(clip);
        store.RemoveClipArtClip(clip.Id);
        Assert.Empty(store.AllClipArtClips);
    }

    [Fact]
    public void UpdateClipArtClip_MutatesInPlace()
    {
        var store = MakeStore();
        var clip  = new ClipArtClip { AssetId = "x", Duration = 3, Opacity = 1.0 };
        store.AddClipArtClip(clip);
        store.UpdateClipArtClip(clip.Id, c => c.Opacity = 0.5);
        Assert.Equal(0.5, store.AllClipArtClips.First().Opacity);
    }

    [Fact]
    public void UpdateClipArtClip_LockedTrack_NoChange()
    {
        var store = MakeStore();
        var clip  = new ClipArtClip { AssetId = "x", Duration = 3, Opacity = 1.0 };
        store.AddClipArtClip(clip);
        store.LockTrack(store.PrimaryVideoTrack.Id, true);
        store.UpdateClipArtClip(clip.Id, c => c.Opacity = 0.0);
        Assert.Equal(1.0, store.AllClipArtClips.First().Opacity);
    }

    [Fact]
    public void AddClipArtClip_LockedTrack_DoesNotAdd()
    {
        var store = MakeStore();
        store.LockTrack(store.PrimaryVideoTrack.Id, true);
        store.AddClipArtClip(new ClipArtClip { AssetId = "x", Duration = 3 });
        Assert.Empty(store.AllClipArtClips);
    }

    [Fact]
    public void RemoveClipArtClip_LockedTrack_DoesNotRemove()
    {
        var store = MakeStore();
        var clip  = new ClipArtClip { AssetId = "x", Duration = 3 };
        store.AddClipArtClip(clip);
        store.LockTrack(store.PrimaryVideoTrack.Id, true);
        store.RemoveClipArtClip(clip.Id);
        Assert.Single(store.AllClipArtClips);
    }

    [Fact]
    public void AddClipArtClip_Undo_RemovesClip()
    {
        var store = MakeStore();
        store.AddClipArtClip(new ClipArtClip { AssetId = "x", Duration = 3 });
        Assert.Single(store.AllClipArtClips);
        store.Undo();
        Assert.Empty(store.AllClipArtClips);
    }

    // ── ProjectFile round-trip ────────────────────────────────────────────────

    [Fact]
    public void ProjectClipArtClip_RoundTrips_AllFields()
    {
        var p = new ProjectClipArtClip
        {
            Id               = Guid.NewGuid(),
            Name             = "Star",
            TimelinePosition = 3.0,
            Duration         = 5.0,
            Order            = 1,
            AssetId          = "asset-abc",
            AssetSource      = AssetSource.SharedCatalog,
            AssetFormat      = VideoAssetFormat.Svg,
            X = 0.2, Y = 0.3, Width = 0.4, Height = -1, Rotation = 45,
            Opacity  = 0.8,
            TintColor = 1.0,
            ControlPointValues = new Dictionary<string, double> { ["stroke"] = 0.5 },
            ControlPointColors = new Dictionary<string, string> { ["fill"] = "#00FF00" },
            SettingsAllowResize        = true,
            SettingsAllowMotion        = true,
            SettingsAllowControlPoints = true,
        };

        var json    = System.Text.Json.JsonSerializer.Serialize(p);
        var restored = System.Text.Json.JsonSerializer.Deserialize<ProjectClipArtClip>(json)!;

        Assert.Equal(p.Id,          restored.Id);
        Assert.Equal("Star",        restored.Name);
        Assert.Equal(AssetSource.SharedCatalog, restored.AssetSource);
        Assert.Equal(VideoAssetFormat.Svg,      restored.AssetFormat);
        Assert.Equal(45.0,          restored.Rotation);
        Assert.Equal(0.8,           restored.Opacity);
        Assert.Equal(0.5,           restored.ControlPointValues["stroke"]);
        Assert.Equal("#00FF00",     restored.ControlPointColors["fill"]);
        Assert.True(restored.SettingsAllowMotion);
        Assert.True(restored.SettingsAllowControlPoints);
    }

    // ── TimelineTrack helper ───────────────────────────────────────────────────

    [Fact]
    public void TimelineTrack_ClipArtClips_EnumeratesCorrectly()
    {
        var track = new TimelineTrack { Type = TrackType.Video };
        track.Items.Add(new ClipArtClip { AssetId = "a", Duration = 1, Order = 0 });
        track.Items.Add(new VideoClip   { Duration = 2, Order = 1 });
        track.Items.Add(new ClipArtClip { AssetId = "b", Duration = 1, Order = 2 });

        var artClips = track.ClipArtClips.ToList();
        Assert.Equal(2, artClips.Count);
        Assert.DoesNotContain(track.ClipArtClips, c => c.AssetId == "");
    }

    // ── ClipStore control-point operations ────────────────────────────────────

    [Fact]
    public void UpdateClipArtClip_ControlPointValues_Persist()
    {
        var store = MakeStore();
        var clip = new ClipArtClip
        {
            AssetId   = "svg-1",
            Duration  = 5,
            ControlPoints = new List<SvgControlPoint>
            {
                new() { PointId = "stroke", Type = SvgControlPointType.StrokeAlpha, DefaultValue = 1.0 },
                new() { PointId = "fill",   Type = SvgControlPointType.FillAlpha,   DefaultValue = 1.0 },
            },
        };
        store.AddClipArtClip(clip);

        // Independently set stroke to 0 (fade outline) while fill stays 1
        store.UpdateClipArtClip(clip.Id, c =>
        {
            c.ControlPointValues["stroke"] = 0.0;
            c.ControlPointValues["fill"]   = 1.0;
        });

        var result = store.AllClipArtClips.First();
        Assert.Equal(0.0, result.ControlPointValues["stroke"]);
        Assert.Equal(1.0, result.ControlPointValues["fill"]);
    }

    [Fact]
    public void UpdateClipArtClip_ControlPointColors_Persist()
    {
        var store = MakeStore();
        var clip = new ClipArtClip { AssetId = "svg-2", Duration = 3 };
        store.AddClipArtClip(clip);

        store.UpdateClipArtClip(clip.Id, c =>
            c.ControlPointColors["outline"] = "#FF0000");

        Assert.Equal("#FF0000", store.AllClipArtClips.First().ControlPointColors["outline"]);
    }

    // ── ProjectService round-trip via ClipStore.ReplaceFromProject ────────────

    [Fact]
    public void ReplaceFromProject_RestoresClipArtClip()
    {
        var store = MakeStore();

        // Build a project file with a ClipArtClip
        var project = new ProjectFile
        {
            Tracks =
            [
                new ProjectTrack
                {
                    Id    = Guid.NewGuid(),
                    Label = "Video 1",
                    Type  = TrackType.Video,
                    Order = 0,
                    ClipArtClips =
                    [
                        new ProjectClipArtClip
                        {
                            Id               = Guid.NewGuid(),
                            Name             = "Ghost",
                            TimelinePosition = 2.0,
                            Duration         = 5.0,
                            Order            = 0,
                            AssetId          = "asset-xyz",
                            AssetSource      = AssetSource.SharedCatalog,
                            AssetFormat      = VideoAssetFormat.Svg,
                            X = 0.3, Y = 0.2, Width = 0.4, Opacity = 0.8,
                            ControlPointValues = new Dictionary<string, double> { ["stroke"] = 0.0 },
                            ControlPointColors = new Dictionary<string, string> { ["fill"]   = "#00FFFF" },
                            SettingsAllowMotion        = true,
                            SettingsAllowControlPoints = true,
                        }
                    ],
                }
            ],
        };

        store.ReplaceFromProject(project);

        var clip = store.AllClipArtClips.Single();
        Assert.Equal("Ghost",              clip.Name);
        Assert.Equal("asset-xyz",          clip.AssetId);
        Assert.Equal(AssetSource.SharedCatalog, clip.AssetSource);
        Assert.Equal(VideoAssetFormat.Svg, clip.AssetFormat);
        Assert.Equal(0.8,                  clip.Opacity);
        Assert.Equal(0.0,                  clip.ControlPointValues["stroke"]);
        Assert.Equal("#00FFFF",            clip.ControlPointColors["fill"]);
        Assert.True(clip.Settings.AllowMotion);
        Assert.True(clip.Settings.AllowControlPoints);
    }

    [Fact]
    public void ReplaceFromProject_EmptyClipArtList_NoClipsAdded()
    {
        var store = MakeStore();
        var project = new ProjectFile
        {
            Tracks = [ new ProjectTrack { Id = Guid.NewGuid(), Type = TrackType.Video, Order = 0 } ],
        };
        store.ReplaceFromProject(project);
        Assert.Empty(store.AllClipArtClips);
    }

    // ── AssetSource enum completeness ─────────────────────────────────────────

    [Theory]
    [InlineData(AssetSource.LocalOpfs)]
    [InlineData(AssetSource.AccountLibrary)]
    [InlineData(AssetSource.SharedCatalog)]
    public void ClipArtClip_AllAssetSources_CanBeAssigned(AssetSource source)
    {
        var clip = new ClipArtClip { AssetSource = source };
        Assert.Equal(source, clip.AssetSource);
    }

    // ── Settings snapshot immutability ────────────────────────────────────────

    [Fact]
    public void ClipArtClip_SettingsSnapshot_IndependentOfServerChanges()
    {
        // Simulate: server grants AllowMotion, user adds clip
        var serverSettings = new VideoAssetSettings { AllowMotion = true, AllowResize = true };
        var clip = new ClipArtClip
        {
            AssetId  = "x",
            Duration = 5,
            Settings = serverSettings with { },   // snapshot
        };
        // Server "removes" AllowMotion — clip still has its snapshot
        Assert.True(clip.Settings.AllowMotion);
        Assert.True(clip.Settings.AllowResize);
    }

    // ── RasterClipArtFrame (backlog #56) ──────────────────────────────────────

    [Fact]
    public void RasterClipArtFrame_Defaults_NoRotationOrTint()
    {
        var frame = new RasterClipArtFrame(10, 20, 100, 50, 0.8);
        Assert.Equal(0.0, frame.Rotation);
        Assert.Null(frame.TintColor);
    }

    [Fact]
    public void RasterClipArtFrame_CarriesRotationAndTint()
    {
        var frame = new RasterClipArtFrame(10, 20, 100, 50, 0.8, Rotation: 45.0, TintColor: 123.0);
        Assert.Equal(45.0, frame.Rotation);
        Assert.Equal(123.0, frame.TintColor);
    }
}
