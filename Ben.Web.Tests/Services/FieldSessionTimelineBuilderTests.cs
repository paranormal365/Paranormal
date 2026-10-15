using System.Text.Json;
using System.Text.Json.Nodes;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.FieldSessions;
using Ben.Service.Models.FieldSessions;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The server's reading of a session document, held to the rules both players share
/// (<c>ProjectNotes/specs/replay-semantics.md</c>) on the same fixtures the phone is held to.
/// </summary>
/// <remarks>
/// These pin what the TIMELINE carries: bounds, the base in force, where each clip sits, what is a
/// marker, what each audience is allowed to see. What a player shows at a given moment of that
/// timeline is the browser's frame function, held to the fixtures' moments separately.
/// </remarks>
public sealed class FieldSessionTimelineBuilderTests
{
    private static JsonObject Fixture(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "replay-conformance", $"{name}.json");
        return JsonNode.Parse(File.ReadAllText(path))!.AsObject();
    }

    private static string Document(JsonObject fixture) => fixture["document"]!.ToJsonString();

    private static readonly Guid Audio1 = Guid.NewGuid(), Audio2 = Guid.NewGuid(),
                                 Video1 = Guid.NewGuid(), Photo1 = Guid.NewGuid();

    /// <summary>The media fixture's files, with the video's length as the server would read it.</summary>
    private static List<TimelineFile> MediaFiles(double? videoSeconds = 6) =>
    [
        new(Audio1, "media/audio-001.m4a", null),
        new(Audio2, "media/audio-002.m4a", null),
        new(Video1, "media/video-001.mov", videoSeconds),
        new(Photo1, "media/photo-001.jpg", null),
    ];

    // ── Bounds (S1, S2) ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("walk")]
    [InlineData("media")]
    [InlineData("interrupted")]
    public void The_session_runs_where_the_fixture_says(string name)
    {
        var fixture = Fixture(name);
        var files = name == "media" ? MediaFiles() : [];
        var timeline = FieldSessionTimelineBuilder.Build(Document(fixture), files, TimelinePositionPolicy.Exact);

        var bounds = fixture["bounds"]!.AsObject();
        Assert.Equal(bounds["end_ms"]!.GetValue<long>(), timeline.DurationMs);
        Assert.Equal(0, bounds["start_ms"]!.GetValue<long>());
        Assert.Equal(DateTime.Parse("2026-09-27T01:00:00Z").ToUniversalTime(), timeline.StartUtc);
    }

    [Fact]
    public void An_interrupted_session_has_no_end_time_and_says_so()
    {
        var timeline = FieldSessionTimelineBuilder.Build(Document(Fixture("interrupted")), [], TimelinePositionPolicy.Exact);
        Assert.False(timeline.HasEndTime);
        Assert.Equal(10_000, timeline.DurationMs);
    }

    [Fact]
    public void Without_an_end_time_a_recording_past_the_last_reading_stretches_the_session()
    {
        // Readings for ten seconds, interrupted, and a sixteen-second recording from 0:00 whose
        // start a marker's reference gives. The tail of the recording must be reachable.
        var doc = Fixture("interrupted");
        var readings = doc["document"]!["readings"]!.AsArray();
        readings[1]!["audio_ref"] = new JsonObject
        {
            ["filename"] = "media/audio-001.m4a", ["start_offset_seconds"] = 2.0,
        };
        var timeline = FieldSessionTimelineBuilder.Build(
            Document(doc), [new(Audio1, "media/audio-001.m4a", 16)], TimelinePositionPolicy.Exact);

        Assert.Equal(16_000, timeline.DurationMs);
    }

    // ── The base in force (I3) and rooms (I5) ───────────────────────────────────

    [Fact]
    public void A_base_set_again_partway_applies_from_that_reading_on()
    {
        var timeline = FieldSessionTimelineBuilder.Build(Document(Fixture("walk")), [], TimelinePositionPolicy.Exact);
        Assert.Equal(new double?[] { 48, 48, 48, 46, 46, 46, 46 }, timeline.Readings.EmfBase);
        Assert.Equal(46, timeline.FallbackEmfBase);
    }

    [Fact]
    public void Rooms_are_named_once_and_each_reading_points_at_its_own()
    {
        var timeline = FieldSessionTimelineBuilder.Build(Document(Fixture("walk")), [], TimelinePositionPolicy.Exact);
        Assert.Equal(["Hall"], timeline.Rooms);
        Assert.Equal(new int?[] { 0, 0, 0, null, null, null, null }, timeline.Readings.Room);
    }

    // ── Recordings and photographs (M2–M5) ──────────────────────────────────────

    [Fact]
    public void Each_recording_sits_where_it_began()
    {
        var timeline = FieldSessionTimelineBuilder.Build(Document(Fixture("media")), MediaFiles(), TimelinePositionPolicy.Exact);
        var clips = timeline.Clips.ToDictionary(c => c.FileId);

        // The session's sound: the first reference says 4 s in at 5 s, so it began at 1 s; the
        // reading that closed it carried its length.
        Assert.Equal(1_000, clips[Audio1].StartMs);
        Assert.Equal(20_000, clips[Audio1].DurationMs);
        // A clip recorded on its own, closed at 26 s with a length of 10: it began at 16 s.
        Assert.Equal(16_000, clips[Audio2].StartMs);
        // A video noted at 14 s whose file runs 6 s: it began at 8 s.
        Assert.Equal(8_000, clips[Video1].StartMs);
        Assert.Equal(14_000, clips[Video1].NotedEndMs);
        Assert.Equal("video", clips[Video1].Kind);

        Assert.Equal(10_000, Assert.Single(timeline.Photos).T);
    }

    [Fact]
    public void A_video_whose_length_the_server_could_not_read_waits_for_the_browser()
    {
        var timeline = FieldSessionTimelineBuilder.Build(Document(Fixture("media")), MediaFiles(videoSeconds: null), TimelinePositionPolicy.Exact);
        var video = timeline.Clips.Single(c => c.FileId == Video1);
        Assert.Null(video.StartMs);
        Assert.Equal(14_000, video.NotedEndMs);
    }

    [Fact]
    public void Only_the_files_this_viewer_may_have_are_on_the_timeline()
    {
        // A share link to ONE recording: the others are not the recipient's to play.
        var timeline = FieldSessionTimelineBuilder.Build(
            Document(Fixture("media")), [new(Audio2, "media/audio-002.m4a", null)], TimelinePositionPolicy.Withheld);
        Assert.Equal([Audio2], timeline.Clips.Select(c => c.FileId));
        Assert.Empty(timeline.Photos);
    }

    // ── Markers (M1) ────────────────────────────────────────────────────────────

    [Fact]
    public void Details_added_after_the_session_reach_their_mark_and_nothing_else()
    {
        // The package carries them beside the readings (10/15/2026), naming the mark by moment and kind.
        var node = System.Text.Json.Nodes.JsonNode.Parse(Document(Fixture("media")))!;
        var mark = node["readings"]!.AsArray()
            .First(r => r!["measurements"]?["marker"]?["value"]?.GetValue<string>() == "manual_marker")!;
        node["mark_details"] = new System.Text.Json.Nodes.JsonArray(
            new System.Text.Json.Nodes.JsonObject
            {
                ["at"] = mark["at"]!.GetValue<string>(),
                ["marker"] = "manual_marker",
                ["details"] = " Left side of the stairs, about ten seconds. ",
                ["added_at"] = "2026-10-15T22:00:00Z",
            },
            // Names a moment no mark has: ignored, not attached to the nearest one.
            new System.Text.Json.Nodes.JsonObject
            {
                ["at"] = "2001-01-01T00:00:00Z", ["marker"] = "manual_marker", ["details"] = "stray",
            });

        var timeline = FieldSessionTimelineBuilder.Build(node.ToJsonString(), MediaFiles(), TimelinePositionPolicy.Exact);

        var marked = timeline.Markers.Single(m => m.Kind == "manual_marker");
        Assert.Equal("Left side of the stairs, about ten seconds.", marked.Details);
        Assert.Equal(new DateTime(2026, 10, 15, 22, 0, 0, DateTimeKind.Utc), marked.DetailsAddedAt?.ToUniversalTime());
        Assert.Equal("footsteps upstairs", marked.Note);   // the moment's own words, untouched
        Assert.All(timeline.Markers.Where(m => m.Kind != "manual_marker"), m => Assert.Null(m.Details));
    }

    [Fact]
    public void Captures_are_not_markers_and_each_marker_has_its_title()
    {
        var timeline = FieldSessionTimelineBuilder.Build(Document(Fixture("media")), MediaFiles(), TimelinePositionPolicy.Exact);
        Assert.Equal(
            [(5_000L, "Marked", false), (12_000L, "Motion detected", true),
             (28_000L, "App put away", true), (30_500L, "Back in the app", true)],
            timeline.Markers.Select(m => (m.T, m.Title, m.Automatic)));
        Assert.Equal("footsteps upstairs", timeline.Markers[0].Note);
        Assert.Equal(new TimelineAway(28_000, 30_500, null), Assert.Single(timeline.Away));
    }

    [Fact]
    public void A_marker_kind_this_server_does_not_know_is_shown_as_itself()
    {
        var doc = Fixture("interrupted");
        doc["document"]!["readings"]!.AsArray()[2]!["measurements"]!.AsObject()["marker"] =
            new JsonObject { ["value"] = "thermal_drop" };
        var timeline = FieldSessionTimelineBuilder.Build(Document(doc), [], TimelinePositionPolicy.Exact);
        var marker = Assert.Single(timeline.Markers);
        Assert.Equal("thermal_drop", marker.Title);
        Assert.False(marker.Automatic);
    }

    // ── Who sees where ──────────────────────────────────────────────────────────

    [Fact]
    public void A_link_made_without_positions_carries_no_coordinate_anywhere()
    {
        var prepared = SharedSessionDocument.Prepare(Document(Fixture("walk")), includePositions: false);
        var timeline = FieldSessionTimelineBuilder.Build(prepared!.Value.Document, [], TimelinePositionPolicy.Withheld);

        Assert.All(timeline.Readings.Lat, Assert.Null);
        Assert.All(timeline.Readings.Lon, Assert.Null);
        Assert.Empty(timeline.Track);
        // Asserted on the wire, not the object: the thing that leaves the server is the JSON.
        var json = JsonSerializer.Serialize(timeline);
        Assert.DoesNotContain("36.0001", json);
        Assert.DoesNotContain("-86", json);
    }

    [Fact]
    public void The_public_archive_draws_no_path_only_the_places_public_point()
    {
        var sanitized = System.Text.Encoding.UTF8.GetString(PublicSessionBundle.Sanitize(
            System.Text.Encoding.UTF8.GetBytes(Document(Fixture("walk"))), 36.16, -86.78));
        var timeline = FieldSessionTimelineBuilder.Build(sanitized, [], TimelinePositionPolicy.PublicPoint);

        Assert.Empty(timeline.Track);
        Assert.All(timeline.Readings.Lat.Where(l => l is not null), l => Assert.Equal(36.16, l));
    }

    [Fact]
    public void The_owner_gets_the_path_walked()
    {
        var timeline = FieldSessionTimelineBuilder.Build(Document(Fixture("walk")), [], TimelinePositionPolicy.Exact);
        Assert.Equal(5, timeline.Track.Count);   // six fixes, two of them on the same spot
        Assert.Equal([36.0, -86.0], timeline.Track[0]);
    }

    // ── A document that is not what the phone writes (S3) ───────────────────────

    [Fact]
    public void Readings_that_cannot_be_placed_are_counted_not_fatal()
    {
        var doc = Fixture("interrupted");
        var readings = doc["document"]!["readings"]!.AsArray();
        readings.Add(42);
        readings.Add(new JsonObject { ["at"] = "not a time" });
        readings[0]!["measurements"]!.AsObject()["emf"] = 48;   // a bare value, not { "value": 48 }

        var timeline = FieldSessionTimelineBuilder.Build(Document(doc), [], TimelinePositionPolicy.Exact);
        Assert.Equal(2, timeline.SkippedReadings);
        Assert.Equal(6, timeline.Readings.T.Length);
        Assert.Null(timeline.Readings.Emf[0]);
    }

    // ── Thinning ────────────────────────────────────────────────────────────────

    [Fact]
    public void A_long_trace_is_thinned_keeping_its_extremes_in_time_order()
    {
        var points = Enumerable.Range(0, 20_000).Select(i => new[] { (double)i, Math.Sin(i / 50.0) }).ToList();
        points[7_777][1] = 99;     // the spike somebody opened the page to look at
        points[12_345][1] = -99;

        var thinned = FieldSessionTimelineBuilder.Thin(points);
        Assert.True(thinned.Count <= FieldSessionTimelineBuilder.TracePoints + 2);
        Assert.Contains(thinned, p => p[1] == 99);
        Assert.Contains(thinned, p => p[1] == -99);
        Assert.Equal(thinned.Select(p => p[0]).OrderBy(x => x), thinned.Select(p => p[0]));
    }
}
