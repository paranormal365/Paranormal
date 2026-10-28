using Ben.Data.WebApi.Controllers;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.FieldSessions;
using Ben.Service.Models.FieldSessions;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// A session document carrying the 1.2 channels — room scans and the person map — is taken like
/// any other, and its marks reach the timeline by name (LiDAR plan; spec 1.2).
/// </summary>
/// <remarks>
/// The channels carry fields beside their <c>value</c>, which every 1.x reader must skip rather
/// than refuse (the spec's rule 6). The guard's only measurement rule is that each is an object
/// with a value — so the test that matters is that it still says nothing about these.
/// </remarks>
public sealed class RoomScanChannelsDocumentTests
{
    private const string Document = """
    {
      "format_version": "1.2.0",
      "device": { "manufacturer": "Apple", "model": "iPhone16,1" },
      "session": { "started_at": "2026-10-28T21:00:00Z", "ended_at": "2026-10-28T22:00:00Z",
                   "trigger": { "mode": "event", "event_description": "a person did it" } },
      "readings": [
        { "at": "2026-10-28T21:12:00Z", "triggered_by": "manual",
          "measurements": {
            "marker": { "value": "room_scan" },
            "room": { "value": "Bedroom 1" },
            "room_scan": { "value": "Bedroom 1", "place_scan_id": "3c3c4798-a03d-4d98-9fb1-684a42f91c30",
                           "version": 2, "outcome": "replaced", "duration_seconds": 722 } } },
        { "at": "2026-10-28T21:30:00Z", "triggered_by": "event",
          "measurements": {
            "marker": { "value": "scan_change" },
            "scan_change": { "value": "moved", "object": "chair", "distance_cm": 42,
                             "where": [1.5, 0, -2.25], "picture": "scans/compare-001.scan#changes/01.jpg" } } },
        { "at": "2026-10-28T21:40:00Z", "triggered_by": "event",
          "measurements": {
            "marker": { "value": "figure_seen" },
            "figure": { "value": "figures/figure-001.json", "from": 12.5, "to": 15, "peak_confidence": 0.8 } } },
        { "at": "2026-10-28T21:50:00Z", "triggered_by": "event",
          "measurements": {
            "marker": { "value": "unexplained_shape" },
            "shape": { "value": "45 × 160 × 30 cm", "size_cm": [45, 160, 30], "distance_m": 2.4, "from": 30, "to": 32.5 } } }
      ]
    }
    """;

    [Fact]
    public void The_guard_takes_a_document_carrying_the_new_channels()
    {
        Assert.Null(FieldSessionDocumentGuard.Refusal(Document));

        var summary = DeviceDataSummary.Read(Document);
        Assert.Equal(4, summary.ReadingCount);
    }

    [Fact]
    public void The_new_marks_reach_the_timeline_by_name()
    {
        var timeline = FieldSessionTimelineBuilder.Build(Document, [], TimelinePositionPolicy.Withheld);

        Assert.Equal(
            ["Room scanned", "Something changed", "Figure seen", "Unexplained shape"],
            timeline.Markers.Select(m => FieldSessionTimelineBuilder.Title(m.Kind)));
        Assert.Equal(0, timeline.SkippedReadings);
    }
}
