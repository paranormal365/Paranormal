using Ben.Video.Sidecar.Storage;

namespace Ben.Video.Sidecar.Api;

/// <summary>
/// Retained-segment lifecycle — item #70 phase 160.
///
/// <para>Only a delete is exposed. There is deliberately no "list" and no "download": retained
/// segments exist so <i>the sidecar</i> can use them as job inputs, and the browser already holds
/// its own copy of every one of them (dual residency). Adding read endpoints would create a second
/// way to obtain bytes the client already has, for no benefit.</para>
///
/// <para>The client calls DELETE when it drops a segment (superseded render, edited clip). The
/// store's LRU is the safety net for everything that never gets an explicit delete — a tab that
/// closes mid-session must not strand disk forever.</para>
/// </summary>
public static class SegmentEndpoints
{
    public static void MapSegmentEndpoints(this IEndpointRouteBuilder app)
    {
        // Idempotent by design: 204 whether or not the id was there. A client cleaning up after a
        // sidecar restart (which drops everything) would otherwise see a flood of spurious 404s
        // for work it correctly believes it should be tidying.
        app.MapDelete("/v1/segments/{segmentId:guid}", (Guid segmentId, RenderedSegmentStore segments) =>
        {
            segments.Delete(segmentId);
            return Results.NoContent();
        });
    }
}
