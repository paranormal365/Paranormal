using System.Text.Json.Nodes;
using Ben.Data.WebApi.Services.FieldSessions;
using Ben.Service.Models.FieldSessions;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// What the latest phone writes that the session page now shows (10/28/2026): the zone it was
/// recorded in, the battery at the start, a photo's room, a question's listening window, and the
/// recordings it names that never arrived.
/// </summary>
public sealed class FieldSessionTimelineReleaseTests
{
    private static readonly DateTime Start = new(2026, 10, 23, 12, 48, 0, DateTimeKind.Utc);
    private static string At(int seconds) => Start.AddSeconds(seconds).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");

    private static JsonObject Reading(int seconds, string? marker = null, string? note = null, string? room = null, JsonObject? audio = null)
    {
        var measurements = new JsonObject();
        if (marker is not null) measurements["marker"] = new JsonObject { ["value"] = marker };
        if (room is not null) measurements["room"] = new JsonObject { ["value"] = room };
        var reading = new JsonObject { ["at"] = At(seconds), ["measurements"] = measurements };
        if (note is not null) reading["note"] = note;
        if (audio is not null) reading["audio_ref"] = audio;
        return reading;
    }

    private static string Document(JsonObject session, params JsonObject[] readings)
    {
        session["started_at"] = At(0);
        session["ended_at"] = At(120);
        return new JsonObject
        {
            ["format_version"] = "1.0.0",
            ["session"] = session,
            ["readings"] = new JsonArray(readings),
        }.ToJsonString();
    }

    [Fact]
    public void The_zone_it_was_recorded_in_and_the_battery_are_carried()
    {
        var timeline = FieldSessionTimelineBuilder.Build(
            Document(new JsonObject { ["timezone"] = "America/Chicago", ["battery_percent_at_start"] = 82.4 }, Reading(0)),
            [], TimelinePositionPolicy.Exact);

        Assert.Equal("America/Chicago", timeline.TimeZoneId);
        Assert.Equal(82, timeline.BatteryAtStart);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Not/AZone")]
    public void A_zone_nobody_knows_is_left_out_rather_than_guessed(string? zone)
    {
        var session = new JsonObject();
        if (zone is not null) session["timezone"] = zone;
        var timeline = FieldSessionTimelineBuilder.Build(Document(session, Reading(0)), [], TimelinePositionPolicy.Exact);
        Assert.Null(timeline.TimeZoneId);
    }

    [Fact]
    public void A_photo_carries_the_room_it_was_taken_in()
    {
        var photo = Guid.NewGuid();
        var timeline = FieldSessionTimelineBuilder.Build(
            Document(new JsonObject(),
                Reading(0, room: "Hall"),
                Reading(10, marker: "photo", note: "photo: media/photo-001.jpg", room: "Back bedroom")),
            [new TimelineFile(photo, "media/photo-001.jpg", null)], TimelinePositionPolicy.Exact);

        Assert.Equal("Back bedroom", Assert.Single(timeline.Photos).Room);
    }

    [Fact]
    public void A_question_is_listened_to_until_the_wait_ends_or_the_next_question()
    {
        var timeline = FieldSessionTimelineBuilder.Build(
            Document(new JsonObject(),
                Reading(10, marker: "evp_question", note: "Is anybody here?"),
                Reading(25, marker: "evp_wait_end", note: "waited 15s after: Is anybody here?"),
                Reading(40, marker: "evp_question", note: "What is your name?"),
                Reading(70, marker: "evp_question", note: "Can you knock?")),
            [], TimelinePositionPolicy.Exact);

        var asked = timeline.Markers.Where(m => m.Kind == "evp_question").ToList();
        Assert.Equal(25_000, asked[0].ListenUntilMs);   // the end of waiting
        Assert.Equal(70_000, asked[1].ListenUntilMs);   // the next question came first
        Assert.Equal(130_000, asked[2].ListenUntilMs);  // neither: a minute on
        Assert.Null(timeline.Markers.Single(m => m.Kind == "evp_wait_end").ListenUntilMs);
    }

    /// <summary>
    /// Ben's 10/23 session: the document names the sound recording and the phone never sent it.
    /// The page said nothing at all about it.
    /// </summary>
    [Fact]
    public void A_recording_the_document_names_and_the_session_never_brought_is_listed_as_missing()
    {
        var video = Guid.NewGuid();
        var timeline = FieldSessionTimelineBuilder.Build(
            Document(new JsonObject(),
                Reading(2, marker: "manual_marker", audio: new JsonObject { ["filename"] = "media/audio-001.m4a", ["start_offset_seconds"] = 2.0 }),
                Reading(57, marker: "video", note: "video: media/video-001.mov")),
            [new TimelineFile(video, "media/video-001.mov", 57)], TimelinePositionPolicy.Exact);

        var missing = Assert.Single(timeline.Missing!);
        Assert.Equal("media/audio-001.m4a", missing.Path);
        Assert.Equal("audio", missing.Kind);
        Assert.Equal(2_000, missing.T);
    }

    [Fact]
    public void A_recording_this_audience_may_not_play_is_not_called_missing()
    {
        // The public archive passes only approved files to play, and every file the session
        // brought as arrived: an unapproved recording is not "never sent".
        var timeline = FieldSessionTimelineBuilder.Build(
            Document(new JsonObject(),
                Reading(2, audio: new JsonObject { ["filename"] = "media/audio-001.m4a", ["start_offset_seconds"] = 2.0 })),
            [], TimelinePositionPolicy.PublicPoint, arrived: ["media/audio-001.m4a"]);

        Assert.Empty(timeline.Missing!);
    }
}
