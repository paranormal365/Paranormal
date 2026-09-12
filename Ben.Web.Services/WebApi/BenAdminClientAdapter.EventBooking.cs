using Ben.Service.Models.Entities;

namespace Ben.Web.Services.WebApi;

/// <summary>The hosted-event booking slice — see <see cref="IBenEventBookingClient"/> for the contract.</summary>
/// <remarks>
/// <para>Nothing in this file calls the flattening GetAsync helper. It answers a 401, a 403, a 404 and an
/// empty success with the same <c>null</c>, and the plan of record's rule for every hosted read is
/// that a refusal must never render as "nothing here" — a member refused the dietary sheet is not
/// looking at an event with no guests. <c>EventBookingClientRouteTests</c> pins both that and the
/// addresses themselves, because a typo in a route is a 404 and a 404 here is a screen politely
/// reporting an empty weekend.</para>
///
/// <para>A route shared by more than one verb is written once, as a private helper, so that the
/// address of each door appears in this file exactly one time and the test can count them.</para>
/// </remarks>
public sealed partial class BenAdminClientAdapter
{
    private static string LayoutUrl(Guid orgId, Guid eventId)
        => $"/api/organizations/{orgId}/events/{eventId}/layout";

    private static string MenusUrl(Guid orgId, Guid eventId)
        => $"/api/organizations/{orgId}/events/{eventId}/menus";

    private static string MyBookingUrl(Guid eventId)
        => $"/api/public/hosted-events/{eventId}/my-booking";

    // ── the plan ─────────────────────────────────────────────────────────────

    public Task<ItemResult<HostedEventLayoutRecord>> GetEventLayoutAsync(
        Guid orgId, Guid eventId, CancellationToken token = default)
        => _api.GetItemAsync<HostedEventLayoutRecord>(LayoutUrl(orgId, eventId), token);

    public Task<(HostedEventLayoutRecord? Result, string? Error, LayoutRefusalRecord? Conflict)> SetEventLayoutAsync(
        Guid orgId, Guid eventId, SetHostedEventLayoutRequest request, CancellationToken token = default)
        => _api.SendExpectingConflictAsync<SetHostedEventLayoutRequest, HostedEventLayoutRecord, LayoutRefusalRecord>(
               HttpMethod.Put, LayoutUrl(orgId, eventId), request, token);

    // ── the board ────────────────────────────────────────────────────────────

    public Task<ItemResult<HostedEventBookingBoardRecord>> GetEventBookingBoardAsync(
        Guid orgId, Guid eventId, CancellationToken token = default)
        => _api.GetItemAsync<HostedEventBookingBoardRecord>(
               $"/api/organizations/{orgId}/events/{eventId}/bookings", token);

    public Task<(HostedEventBookingRecord? Result, string? Error)> ConfirmEventBookingAsync(
        Guid orgId, Guid eventId, Guid bookingId, ConfirmHostedEventBookingRequest request,
        CancellationToken token = default)
        => _api.SendExpectingReasonAsync<ConfirmHostedEventBookingRequest, HostedEventBookingRecord>(
               HttpMethod.Post,
               $"/api/organizations/{orgId}/events/{eventId}/bookings/{bookingId}/confirm",
               request, token);

    public Task<(HostedEventBookingRecord? Result, string? Error)> TurnDownEventBookingAsync(
        Guid orgId, Guid eventId, Guid bookingId, string? decisionNote, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<DecideHostedEventBookingRequest, HostedEventBookingRecord>(
               HttpMethod.Post,
               $"/api/organizations/{orgId}/events/{eventId}/bookings/{bookingId}/turn-down",
               new DecideHostedEventBookingRequest(decisionNote), token);

    public Task<(HostedEventBookingRecord? Result, string? Error)> CancelEventBookingAsync(
        Guid orgId, Guid eventId, Guid bookingId, string? decisionNote, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<DecideHostedEventBookingRequest, HostedEventBookingRecord>(
               HttpMethod.Post,
               $"/api/organizations/{orgId}/events/{eventId}/bookings/{bookingId}/cancel",
               new DecideHostedEventBookingRequest(decisionNote), token);

    public Task<(HostedEventBookingRecord? Result, string? Error)> EditEventBookingAsync(
        Guid orgId, Guid eventId, Guid bookingId, EditHostedEventBookingRequest request,
        CancellationToken token = default)
        => _api.SendExpectingReasonAsync<EditHostedEventBookingRequest, HostedEventBookingRecord>(
               HttpMethod.Put,
               $"/api/organizations/{orgId}/events/{eventId}/bookings/{bookingId}",
               request, token);

    public Task<(HostedEventBookingRecord? Result, string? Error)> CreateEventBookingOnBehalfAsync(
        Guid orgId, Guid eventId, CreateHostedEventBookingOnBehalfRequest request,
        CancellationToken token = default)
        => _api.SendExpectingReasonAsync<CreateHostedEventBookingOnBehalfRequest, HostedEventBookingRecord>(
               HttpMethod.Post,
               $"/api/organizations/{orgId}/events/{eventId}/bookings/on-behalf",
               request, token);

    public Task<(HostedEventGuestInviteRecord? Result, string? Error)> InviteEventGuestAsync(
        Guid orgId, Guid eventId, InviteHostedEventGuestRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<InviteHostedEventGuestRequest, HostedEventGuestInviteRecord>(
               HttpMethod.Post,
               $"/api/organizations/{orgId}/events/{eventId}/bookings/on-behalf/invite",
               request, token);

    // ── the kitchen ──────────────────────────────────────────────────────────

    public Task<ItemResult<HostedEventDietaryRecord>> GetEventDietaryAsync(
        Guid orgId, Guid eventId, bool includeRequests, CancellationToken token = default)
    {
        // Spelled out rather than interpolating the bool: C# prints "True", and while the model
        // binder happens to accept that, the address a person reads in a log should be the one
        // the API documents.
        var flag = includeRequests ? "true" : "false";
        return _api.GetItemAsync<HostedEventDietaryRecord>(
            $"/api/organizations/{orgId}/events/{eventId}/bookings/dietary?includeRequests={flag}", token);
    }

    public Task<ItemResult<HostedEventMenusRecord>> GetEventMenusAsync(
        Guid orgId, Guid eventId, CancellationToken token = default)
        => _api.GetItemAsync<HostedEventMenusRecord>(MenusUrl(orgId, eventId), token);

    public Task<(HostedEventMenusRecord? Result, string? Error)> SetEventMenusAsync(
        Guid orgId, Guid eventId, SetHostedEventMenusRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<SetHostedEventMenusRequest, HostedEventMenusRecord>(
               HttpMethod.Put, MenusUrl(orgId, eventId), request, token);

    // ── passes and the door ──────────────────────────────────────────────────

    public Task<(HostedEventPassRecord? Result, string? Error)> IssueEventPassAsync(
        Guid orgId, Guid eventId, Guid bookingId, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<object, HostedEventPassRecord>(
               HttpMethod.Post,
               $"/api/organizations/{orgId}/events/{eventId}/bookings/{bookingId}/pass",
               new { }, token);

    public Task<(HostedEventPassRecord? Result, string? Error)> RevokeEventPassAsync(
        Guid orgId, Guid eventId, Guid bookingId, string reason, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<RevokeHostedEventPassRequest, HostedEventPassRecord>(
               HttpMethod.Post,
               $"/api/organizations/{orgId}/events/{eventId}/bookings/{bookingId}/pass/revoke",
               new RevokeHostedEventPassRequest(reason), token);

    public Task<(HostedEventPassRecord? Result, string? Error)> ReissueEventPassAsync(
        Guid orgId, Guid eventId, Guid bookingId, string? reason, CancellationToken token = default)
        // The endpoint's body is optional, but a JSON "null" body and a missing one are bound
        // differently by different framework versions; an object with a blank reason is read the
        // same way everywhere, and the server treats blank as "use the standard sentence".
        => _api.SendExpectingReasonAsync<RevokeHostedEventPassRequest, HostedEventPassRecord>(
               HttpMethod.Post,
               $"/api/organizations/{orgId}/events/{eventId}/bookings/{bookingId}/pass/reissue",
               new RevokeHostedEventPassRequest(reason ?? ""), token);

    public Task<(HostedEventPassRecord? Result, string? Error)> EmailEventPassAsync(
        Guid orgId, Guid eventId, Guid bookingId, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<object, HostedEventPassRecord>(
               HttpMethod.Post,
               $"/api/organizations/{orgId}/events/{eventId}/bookings/{bookingId}/pass/email",
               new { }, token);

    public Task<(HostedEventScanResult? Result, string? Error)> ScanEventPassAsync(
        Guid orgId, Guid eventId, string code, bool checkIn, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<ScanHostedEventPassRequest, HostedEventScanResult>(
               HttpMethod.Post,
               $"/api/organizations/{orgId}/events/{eventId}/bookings/door/scan",
               new ScanHostedEventPassRequest(code, checkIn), token);

    // ── the guest's own weekend ──────────────────────────────────────────────

    public Task<LoadResult<MyHostedEventBookingRecord>> GetMyHostedEventBookingsAsync(CancellationToken token = default)
        => _api.GetListAsync<MyHostedEventBookingRecord>("/api/public/hosted-events/mine", token);

    public Task<ItemResult<MyHostedEventBookingRecord>> GetMyHostedEventBookingAsync(
        Guid eventId, CancellationToken token = default)
        => _api.GetItemAsync<MyHostedEventBookingRecord>(MyBookingUrl(eventId), token);

    public Task<(MyHostedEventBookingRecord? Result, string? Error)> RequestHostedEventBookingAsync(
        Guid eventId, RequestHostedEventBookingRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<RequestHostedEventBookingRequest, MyHostedEventBookingRecord>(
               HttpMethod.Post, $"/api/public/hosted-events/{eventId}/bookings", request, token);

    public Task<(MyHostedEventBookingRecord? Result, string? Error)> UpdateMyHostedEventBookingAsync(
        Guid eventId, EditHostedEventBookingRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<EditHostedEventBookingRequest, MyHostedEventBookingRecord>(
               HttpMethod.Put, MyBookingUrl(eventId), request, token);

    public Task<(MyHostedEventBookingRecord? Result, string? Error)> AcknowledgeMyHostedEventBookingAsync(
        Guid eventId, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<object, MyHostedEventBookingRecord>(
               HttpMethod.Post, $"/api/public/hosted-events/{eventId}/my-booking/acknowledge",
               new { }, token);

    public Task<(bool Withdrawn, string? Error)> WithdrawMyHostedEventBookingAsync(
        Guid eventId, string? reason, CancellationToken token = default)
    {
        // DELETE carries no body, so the guest's reason travels as a query string — escaped,
        // because "can't make it, my mother's ill & the car's gone" is a perfectly ordinary reason
        // and an ampersand in a raw query string would silently hand the server half of it.
        var query = string.IsNullOrWhiteSpace(reason) ? "" : "?reason=" + Uri.EscapeDataString(reason.Trim());
        return _api.DeleteExpectingReasonAsync(MyBookingUrl(eventId) + query, token);
    }

    public Task<ItemResult<MyHostedEventPassRecord>> GetMyHostedEventPassAsync(
        Guid eventId, CancellationToken token = default)
        => _api.GetItemAsync<MyHostedEventPassRecord>(
               $"/api/public/hosted-events/{eventId}/my-booking/pass", token);

    public Task<ItemResult<HostedEventMenusRecord>> GetMyHostedEventMenusAsync(
        Guid eventId, CancellationToken token = default)
        => _api.GetItemAsync<HostedEventMenusRecord>(
               $"/api/public/hosted-events/{eventId}/menus", token);
}
