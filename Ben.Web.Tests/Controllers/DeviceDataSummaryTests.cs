using Ben.Data.WebApi.Controllers;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// What a session's document is counted as, for every list that shows "N marked".
/// </summary>
public sealed class DeviceDataSummaryTests
{
    private static string Document(params string[] markers)
    {
        var marked = string.Concat(markers.Select(m =>
            ",{\"at\":\"2026-09-27T21:00:01Z\",\"measurements\":{\"marker\":{\"value\":\"" + m + "\"}}}"));
        return "{\"format_version\":\"1.0.0\",\"device\":{\"model\":\"iPhone16,2\"},"
             + "\"session\":{\"started_at\":\"2026-09-27T21:00:00Z\"},"
             + "\"readings\":[{\"at\":\"2026-09-27T21:00:00Z\",\"measurements\":{\"emf\":{\"value\":48}}}"
             + marked + "]}";
    }

    [Fact]
    public void Photos_videos_and_sound_are_noted_but_not_counted_as_marks()
    {
        // The shape of a real session: two sightings, a mark, three photographs, a video and its sound.
        var summary = DeviceDataSummary.Read(Document(
            "scene_motion", "scene_motion", "manual_marker", "photo", "photo", "photo", "video", "audio"));

        Assert.Equal(9, summary.ReadingCount);
        Assert.Equal(3, summary.MarkerCount);
    }

    [Fact]
    public void Every_real_kind_of_mark_still_counts()
    {
        var summary = DeviceDataSummary.Read(Document(
            "manual_marker", "sentry_emf", "sentry_sound", "device_moved", "scene_motion",
            "evp_question", "evp_wait_end", "app_backgrounded", "app_returned"));
        Assert.Equal(9, summary.MarkerCount);
    }

    [Fact]
    public void A_sessions_times_are_stored_as_the_UTC_instant_whatever_clock_they_were_written_on()
    {
        // Ben, 2026-09-28: "saves in utc ... make sure this tracks with the .ben file here and on the
        // server". The phone writes Z; another device may write an offset. Either way the row holds
        // the same instant in UTC, never the server's own clock.
        const string zulu = "{\"format_version\":\"1.0.0\",\"device\":{\"model\":\"iPhone16,2\"},"
            + "\"session\":{\"started_at\":\"2026-09-27T21:00:00Z\",\"ended_at\":\"2026-09-27T22:30:00Z\"},\"readings\":[]}";
        const string offset = "{\"format_version\":\"1.0.0\",\"device\":{\"model\":\"iPhone16,2\"},"
            + "\"session\":{\"started_at\":\"2026-09-27T16:00:00-05:00\",\"ended_at\":\"2026-09-27T17:30:00-05:00\"},\"readings\":[]}";

        foreach (var doc in new[] { zulu, offset })
        {
            var summary = DeviceDataSummary.Read(doc);
            Assert.Equal(DateTimeKind.Utc, summary.StartedAt.Kind);
            Assert.Equal(new DateTime(2026, 9, 27, 21, 0, 0, DateTimeKind.Utc), summary.StartedAt);
            Assert.Equal(new DateTime(2026, 9, 27, 22, 30, 0, DateTimeKind.Utc), summary.EndedAt);
        }
    }
}
