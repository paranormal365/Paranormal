using System.Reflection;
using Ben.Video.Editor.Models;
using Ben.Video.Editor.Models.Assets;
using Ben.Video.Editor.Services;
using Microsoft.Extensions.Options;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Everything a person can set on the timeline survives being saved and opened again.
/// </summary>
/// <remarks>
/// <para>There was a round-trip test already, and it could not catch this class of bug: it built a
/// project file by hand, listing the fields it expected, and checked those came back. A field the
/// mapper had never learned about was equally absent from the test, so the two agreed with each
/// other and disagreed with the model. Per-axis scale and rotation keyframes, a callout's text
/// layout, a clip's mute and link state — all silently dropped on save, with the test green
/// (2026-09-05 audit, persistence-2, motion-1, audio-7, callouts-1).</para>
///
/// <para>This one reflects over the model instead. Every settable property is given a distinctive
/// value, the project is built and restored, and anything that comes back different is named. A
/// new property fails it until somebody decides whether it belongs in the file — which is the only
/// way this stays true after today.</para>
/// </remarks>
public sealed class ProjectRoundTripParityTests
{
    /// <summary>
    /// Properties that deliberately do not survive, with the reason.
    /// </summary>
    /// <remarks>
    /// Listing the exceptions rather than the members is what makes a forgotten new field fail
    /// closed instead of being quietly ignored.
    /// </remarks>
    private static readonly Dictionary<string, string> NotPersisted = new()
    {
        // Declared on each clip type, not on TrackItem — these keys used to say TrackItem and
        // therefore matched nothing. Parity passed anyway because both sides default to null,
        // which is exactly the blind spot Every_fixture_gives_each_property_a_distinctive_value
        // exists to close.
        ["VideoClip.MemFsName"] =
            "where the media sits in this browser session's filesystem, remounted on open",
        ["AudioClip.MemFsName"] =
            "where the media sits in this browser session's filesystem, remounted on open",
        ["ImageClip.MemFsName"] =
            "where the media sits in this browser session's filesystem, remounted on open",
        ["AudioClip.BlobUrl"] =
            "a blob URL belonging to this page; recreated when the audio is remounted",
        ["TrackItem.IsMediaMissing"] =
            "true until the media is remounted, so it is decided on open rather than restored",
        ["ImageClip.ThumbnailUrl"] =
            "a blob URL belonging to this page; regenerated on open",
        ["VideoClip.ThumbnailUrls"] =
            "blob URLs for the filmstrip; regenerated on open",
        ["AudioClip.WaveformPeaks"] =
            "derived from the audio itself and recomputed on open",
        ["CalloutClip.AssetMissing"] =
            "decided when the asset is resolved on open",
        ["TrackItem.SourceBinId"] =
            "re-linked to the media bin entry on open, which for an older project is created then",
        ["TrackItem.LayerIndex"] =
            "renumbered to a dense sequence on open, so it is relative and not restored verbatim; "
            + "the ordering it encodes is covered by Overlay_stacking_order_survives",
    };

    // ── The round trip ────────────────────────────────────────────────────────

    private static (ClipStore Source, ClipStore Restored) RoundTrip(Action<ClipStore> populate)
    {
        var opts = Options.Create(new VideoEditorOptions
        {
            MultiTrack = true, AudioTracks = true, Transitions = true, TextOverlays = true,
        });

        var source       = new ClipStore(opts);
        var sourceMotion = new MotionKeyframeService();
        populate(source);

        var file = new ProjectService(source, sourceMotion, new NoJs(), new NoHttp(), opts)
            .BuildCurrentProjectFile("Parity");

        // Serialised and parsed, not handed over as an object: a field that survives in memory and
        // dies in JSON is exactly the failure this is looking for.
        var json   = ProjectSerializer.Serialize(file);
        var parsed = ProjectSerializer.Deserialize(json);
        Assert.NotNull(parsed);

        var restored = new ClipStore(opts);
        new ProjectService(restored, new MotionKeyframeService(), new NoJs(), new NoHttp(), opts)
            .RestoreAsync(parsed!);

        return (source, restored);
    }

    private static IEnumerable<PropertyInfo> SettableProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0);

    /// <summary>
    /// Names every property whose value did not come back, so a failure says what to fix.
    /// </summary>
    private static List<string> Differences(object before, object after, string typeName)
    {
        var problems = new List<string>();

        foreach (var property in SettableProperties(before.GetType()))
        {
            var key = $"{property.DeclaringType!.Name}.{property.Name}";
            if (NotPersisted.ContainsKey(key)) continue;
            if (NotPersisted.ContainsKey($"{typeName}.{property.Name}")) continue;

            var a = property.GetValue(before);
            var b = property.GetValue(after);

            if (!Same(a, b))
                problems.Add($"{typeName}.{property.Name}: saved {Describe(a)}, came back {Describe(b)}");
        }

        return problems;
    }

    private static bool Same(object? a, object? b)
    {
        if (a is null || b is null) return Equals(a, b);

        // Collections compare by their contents; the DTO rebuilds them as new instances.
        if (a is System.Collections.IEnumerable ea and not string &&
            b is System.Collections.IEnumerable eb)
        {
            var left  = ea.Cast<object?>().ToList();
            var right = eb.Cast<object?>().ToList();
            return left.Count == right.Count
                && left.Zip(right).All(pair => Same(pair.First, pair.Second));
        }

        if (a is double da && b is double db) return Math.Abs(da - db) < 1e-9;

        // Anything without value semantics of its own is compared field by field.
        var type = a.GetType();
        if (!type.IsPrimitive && !type.IsEnum && type != typeof(string) && type != typeof(Guid)
            && Nullable.GetUnderlyingType(type) is null && !type.IsValueType)
        {
            return SettableProperties(type).All(p => Same(p.GetValue(a), p.GetValue(b)));
        }

        return Equals(a, b);
    }

    private static string Describe(object? value) => value switch
    {
        null => "null",
        string s => $"\"{s}\"",
        System.Collections.IEnumerable e and not string => $"[{e.Cast<object?>().Count()} item(s)]",
        _ => value.ToString() ?? "?",
    };

    // ── One test per kind of thing on the timeline ────────────────────────────

    /// <summary>
    /// Every fixture actually says something about every property it is meant to cover.
    /// </summary>
    /// <remarks>
    /// <para>The parity check compares a saved item with a restored one. A property the fixture
    /// leaves at its default round-trips whatever the mapper does, because both sides land on the
    /// same default — so an untouched property makes the parity test look green while proving
    /// nothing at all.</para>
    ///
    /// <para>This is not hypothetical. Three new source-file properties were added, mapped on both
    /// sides, and the parity test passed just as happily with the save mapper's line deleted. The
    /// fixture's own doc comment already warned about this; nothing enforced it.</para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(EveryFixture))]
    public void Every_fixture_gives_each_property_a_distinctive_value(string label, object fixture)
    {
        var pristine = Activator.CreateInstance(fixture.GetType())!;

        var untouched = SettableProperties(fixture.GetType())
            .Where(p => !NotPersisted.ContainsKey($"{p.DeclaringType!.Name}.{p.Name}"))
            .Where(p => !NotPersisted.ContainsKey($"{label}.{p.Name}"))
            .Where(p => !MayBeLeftAtDefault(p, label))
            .Where(p => Same(p.GetValue(fixture), p.GetValue(pristine)))
            .Select(p => $"{label}.{p.Name} is still its default, so parity proves nothing about it")
            .ToList();

        Assert.Empty(untouched);
    }

    public static TheoryData<string, object> EveryFixture() => new()
    {
        { nameof(VideoClip),    Fixtures.VideoClip()    },
        { nameof(AudioClip),    Fixtures.AudioClip()    },
        { nameof(ImageClip),    Fixtures.ImageClip()    },
        { nameof(CalloutClip),  Fixtures.Callout()      },
        { nameof(TextOverlay),  Fixtures.TextOverlay()  },
        { nameof(ClipArtClip),  Fixtures.ClipArt()      },
    };

    /// <summary>
    /// Overlays — callouts, titles, clip art — that are drawn rather than imported.
    /// </summary>
    private static readonly HashSet<string> DrawnNotImported =
        [nameof(CalloutClip), nameof(TextOverlay), nameof(ClipArtClip)];

    /// <summary>
    /// Properties on <c>TrackItem</c> that only mean anything for imported media.
    /// </summary>
    /// <remarks>
    /// They live on the base type because every item carries them, but a title has no source file
    /// and no media bin entry, so a fixture leaving them alone is right rather than incomplete.
    /// </remarks>
    private static readonly HashSet<string> AboutImportedMedia =
    [
        nameof(TrackItem.SourceFileId), nameof(TrackItem.SourceFileSize),
        nameof(TrackItem.SourceContentHash), nameof(TrackItem.SourceBinId),
        nameof(TrackItem.OriginalFileName), nameof(TrackItem.OpfsExt),
        nameof(TrackItem.LinkedClipId),
    ];

    /// <summary>
    /// Anything else a fixture may leave alone, each with its reason.
    /// </summary>
    private static readonly HashSet<string> AllowedAtDefault =
    [
        // Assigned by the model itself, so there is no default to differ from.
        "VideoClip.Id", "AudioClip.Id", "ImageClip.Id", "CalloutClip.Id",
        "TextOverlay.Id", "ClipArtClip.Id",

        // An image is never the other half of a link; only picture and sound are paired.
        "ImageClip.LinkedClipId",

        // Read from the asset when it resolves on open, like the two native dimensions beside them.
        "ClipArtClip.Settings", "ClipArtClip.ControlPoints",
    ];

    private static bool MayBeLeftAtDefault(PropertyInfo property, string typeName) =>
        AllowedAtDefault.Contains($"{typeName}.{property.Name}")
        || (DrawnNotImported.Contains(typeName) && AboutImportedMedia.Contains(property.Name));

    [Fact]
    public void A_video_clip_comes_back_as_it_was_saved()
    {
        var clip = Fixtures.VideoClip();

        var (_, restored) = RoundTrip(s => s.AddClip(clip));

        var back = Assert.Single(restored.AllVideoClips);
        Assert.Empty(Differences(clip, back, nameof(VideoClip)));
    }

    [Fact]
    public void An_audio_clip_comes_back_as_it_was_saved()
    {
        var clip = Fixtures.AudioClip();

        var (_, restored) = RoundTrip(s =>
        {
            var track = s.AddAudioTrack();
            s.AddClipToTrack(track.Id, clip);
        });

        var back = Assert.Single(restored.AllAudioClips);
        Assert.Empty(Differences(clip, back, nameof(AudioClip)));
    }

    [Fact]
    public void An_image_clip_comes_back_as_it_was_saved()
    {
        var clip = Fixtures.ImageClip();

        var (_, restored) = RoundTrip(s => s.AddClipToTrack(s.PrimaryVideoTrack.Id, clip));

        var back = Assert.Single(restored.AllImageClips);
        Assert.Empty(Differences(clip, back, nameof(ImageClip)));
    }

    [Fact]
    public void A_callout_comes_back_as_it_was_saved()
    {
        var callout = Fixtures.Callout();

        var (_, restored) = RoundTrip(s => s.AddClipToTrack(s.PrimaryVideoTrack.Id, callout));

        var back = Assert.Single(restored.AllCalloutClips);
        Assert.Empty(Differences(callout, back, nameof(CalloutClip)));
    }

    [Fact]
    public void A_title_comes_back_as_it_was_saved()
    {
        var title = Fixtures.TextOverlay();

        var (_, restored) = RoundTrip(s => s.AddClipToTrack(s.PrimaryVideoTrack.Id, title));

        var back = Assert.Single(restored.AllTextOverlays);
        Assert.Empty(Differences(title, back, nameof(TextOverlay)));
    }

    [Fact]
    public void A_piece_of_clip_art_comes_back_as_it_was_saved()
    {
        var art = Fixtures.ClipArt();

        var (_, restored) = RoundTrip(s => s.AddClipToTrack(s.PrimaryVideoTrack.Id, art));

        var back = Assert.Single(restored.AllClipArtClips);
        Assert.Empty(Differences(art, back, nameof(ClipArtClip)));
    }

    [Fact]
    public void A_transition_comes_back_as_it_was_saved()
    {
        var transition = Fixtures.Transition();

        var (_, restored) = RoundTrip(s => s.AddClipToTrack(s.PrimaryVideoTrack.Id, transition));

        var back = Assert.Single(restored.AllTransitions);
        Assert.Empty(Differences(transition, back, nameof(Transition)));
    }

    /// <summary>
    /// A layer's animation comes back as it was.
    /// </summary>
    /// <remarks>
    /// Per-axis scale and rotation were on the keyframe and not in its DTO, so stretching a layer
    /// on one axis or rotating it looked right until the project was saved and opened again, at
    /// which point the animation came back uniform and upright (2026-09-05 audit, motion-1).
    /// </remarks>
    [Fact]
    public void A_motion_keyframe_comes_back_as_it_was_saved()
    {
        var callout = Fixtures.Callout();
        var first   = Fixtures.Keyframe(0.0);
        var second  = Fixtures.Keyframe(2.5);

        var opts = Options.Create(new VideoEditorOptions { TextOverlays = true });

        var source       = new ClipStore(opts);
        var sourceMotion = new MotionKeyframeService();
        source.AddClipToTrack(source.PrimaryVideoTrack.Id, callout);
        sourceMotion.RestoreAll([new MotionPath
        {
            LayerId   = callout.Id,
            LayerType = nameof(CalloutClip),
            Keyframes = [first, second],
        }]);

        var file = new ProjectService(source, sourceMotion, new NoJs(), new NoHttp(), opts)
            .BuildCurrentProjectFile("Parity");

        var parsed = ProjectSerializer.Deserialize(ProjectSerializer.Serialize(file));

        var restoredMotion = new MotionKeyframeService();
        new ProjectService(new ClipStore(opts), restoredMotion, new NoJs(), new NoHttp(), opts)
            .RestoreAsync(parsed!);

        var path = Assert.Single(restoredMotion.AllPaths);
        Assert.Equal(2, path.Keyframes.Count);
        Assert.Empty(Differences(first, path.Keyframes[0], nameof(MotionKeyframe)));
        Assert.Empty(Differences(second, path.Keyframes[1], nameof(MotionKeyframe)));
    }

    /// <summary>
    /// Overlays come back stacked in the order they were in.
    /// </summary>
    /// <remarks>
    /// <see cref="TrackItem.LayerIndex"/> is renumbered to a dense sequence on open, so the number
    /// itself is not restored. What has to survive is the ordering it encodes — which is what
    /// decides who draws on top of whom.
    /// </remarks>
    [Fact]
    public void Overlay_stacking_order_survives()
    {
        var bottom = Fixtures.Callout();
        var middle = Fixtures.TextOverlay();
        var top    = Fixtures.ClipArt();

        bottom.LayerIndex = 0;
        middle.LayerIndex = 1;
        top.LayerIndex    = 2;

        var (_, restored) = RoundTrip(s =>
        {
            s.AddClipToTrack(s.PrimaryVideoTrack.Id, bottom);
            s.AddClipToTrack(s.PrimaryVideoTrack.Id, middle);
            s.AddClipToTrack(s.PrimaryVideoTrack.Id, top);
        });

        var order = restored.PrimaryVideoTrack.Items
            .Where(i => i is CalloutClip or TextOverlay or ClipArtClip)
            .OrderBy(i => i.LayerIndex)
            .Select(i => i.Id)
            .ToList();

        Assert.Equal([bottom.Id, middle.Id, top.Id], order);
    }

    /// <summary>
    /// The hint offered when media has to be re-linked is the person's own filename.
    /// </summary>
    /// <remarks>
    /// It used to be saved from the session-local MEMFS path, so reopening a project offered a
    /// hint like "vid_3f2a91c4….mp4" — a name that appears nowhere on the person's machine
    /// (2026-09-05 audit, persistence-2).
    /// </remarks>
    [Fact]
    public void The_relink_hint_is_the_file_the_person_chose()
    {
        var clip = Fixtures.VideoClip();
        clip.MemFsName = "vid_3f2a91c4deadbeef.mp4";

        var (_, restored) = RoundTrip(s => s.AddClip(clip));

        Assert.Equal("porch.mp4", Assert.Single(restored.AllVideoClips).OriginalFileName);
    }

    // ── Support ───────────────────────────────────────────────────────────────

    private sealed class NoJs : Microsoft.JSInterop.IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => throw new NotSupportedException("the round trip does not touch the browser");

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier, CancellationToken ct, object?[]? args)
            => InvokeAsync<TValue>(identifier, args);
    }

    private sealed class NoHttp : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => throw new NotSupportedException("the round trip does not touch the network");
    }
}
