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

    public Task<(HostedEventBookingRecord? Result, string? Error)> ExtendEventHoldAsync(
        Guid orgId, Guid eventId, Guid bookingId, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<object, HostedEventBookingRecord>(
               HttpMethod.Post,
               $"/api/organizations/{orgId}/events/{eventId}/bookings/{bookingId}/hold/extend",
               new { }, token);

    public Task<(HostedEventBookingBoardRecord? Result, string? Error)> ReleaseLapsedHoldsAsync(
        Guid orgId, Guid eventId, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<object, HostedEventBookingBoardRecord>(
               HttpMethod.Post,
               $"/api/organizations/{orgId}/events/{eventId}/bookings/holds/release-lapsed",
               new { }, token);

    public Task<ItemResult<HostedEventDietaryRecord>> GetEventDietaryAsync(
        Guid orgId, Guid eventId, bool includeUnconfirmed, Guid? night = null,
        CancellationToken token = default)
    {
        // Spelled out rather than interpolating the bool: C# prints "True", and while the model
        // binder happens to accept that, the address a person reads in a log should be the one
        // the API documents.
        var flag = includeUnconfirmed ? "true" : "false";
        var oneNight = night is { } id ? $"&night={id}" : "";
        return _api.GetItemAsync<HostedEventDietaryRecord>(
            $"/api/organizations/{orgId}/events/{eventId}/bookings/dietary?includeUnconfirmed={flag}"
            + oneNight, token);
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
        Guid orgId, Guid eventId, string code, bool checkIn, Guid? nightId = null,
        CancellationToken token = default)
        => _api.SendExpectingReasonAsync<ScanHostedEventPassRequest, HostedEventScanResult>(
               HttpMethod.Post,
               $"/api/organizations/{orgId}/events/{eventId}/bookings/door/scan",
               new ScanHostedEventPassRequest(code, checkIn, nightId), token);

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

    public Task<(MyHostedEventBookingRecord? StillStanding, string? Error)> WithdrawMyHostedEventBookingAsync(
        Guid eventId, string? reason, CancellationToken token = default)
    {
        // The reason travels as a query string — escaped, because "can't make it, my mother's ill
        // & the car's gone" is a perfectly ordinary reason and an ampersand in a raw query string
        // would silently hand the server half of it.
        var query = string.IsNullOrWhiteSpace(reason) ? "" : "?reason=" + Uri.EscapeDataString(reason.Trim());

        // Kept rather than discarded, because the answer's SHAPE is the answer. Withdrawing means
        // two different things on this door: a request is deleted and the server answers 204, and
        // a confirmed booking is not — the venue has catered against it, so the ask is recorded and
        // the booking comes back with CancellationRequestedUtc set. A helper that threw the body
        // away left a screen unable to tell "it is gone" from "you have asked them to release it",
        // which are opposite things to say to a guest.
        return _api.SendExpectingReasonAsync<object, MyHostedEventBookingRecord>(
            HttpMethod.Delete, MyBookingUrl(eventId) + query, new { }, token);
    }

    public Task<ItemResult<MyHostedEventPassRecord>> GetMyHostedEventPassAsync(
        Guid eventId, CancellationToken token = default)
        => _api.GetItemAsync<MyHostedEventPassRecord>(
               $"/api/public/hosted-events/{eventId}/my-booking/pass", token);

    public Task<ItemResult<HostedEventMenusRecord>> GetMyHostedEventMenusAsync(
        Guid eventId, CancellationToken token = default)
        => _api.GetItemAsync<HostedEventMenusRecord>(
               $"/api/public/hosted-events/{eventId}/menus", token);

    // ── what a party wears (item 235 phase 7) ────────────────────────────────

    private static string BandsUrl(Guid orgId, Guid eventId)
        => $"/api/organizations/{orgId}/events/{eventId}/bands";

    public Task<ItemResult<HostedEventBandsRecord>> GetEventBandsAsync(
        Guid orgId, Guid eventId, CancellationToken token = default)
        => _api.GetItemAsync<HostedEventBandsRecord>(BandsUrl(orgId, eventId), token);

    public Task<(HostedEventBandsRecord? Result, string? Error)> SetEventBandsAsync(
        Guid orgId, Guid eventId, SetHostedEventBandsRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<SetHostedEventBandsRequest, HostedEventBandsRecord>(
               HttpMethod.Put, BandsUrl(orgId, eventId), request, token);

    public Task<(HostedEventBandsRecord? Result, string? Error)> SetBookingBandAsync(
        Guid orgId, Guid eventId, Guid bookingId, Guid? bandId, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<SetHostedEventBookingBandRequest, HostedEventBandsRecord>(
               HttpMethod.Post,
               $"/api/organizations/{orgId}/events/{eventId}/bookings/{bookingId}/band",
               new SetHostedEventBookingBandRequest(bandId), token);

    // ── the door, on the night (item 235 phase 7) ────────────────────────────

    private static string DoorUrl(Guid orgId, Guid eventId)
        => $"/api/organizations/{orgId}/events/{eventId}/door";

    public Task<ItemResult<HostedEventDoorRecord>> GetEventDoorAsync(
        Guid orgId, Guid eventId, Guid? night = null, CancellationToken token = default)
        => _api.GetItemAsync<HostedEventDoorRecord>(
               DoorUrl(orgId, eventId) + (night is { } id ? $"?night={id}" : ""), token);

    public Task<(HostedEventDoorRecord? Result, string? Error)> DoorArriveAsync(
        Guid orgId, Guid eventId, HostedEventDoorMoveRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<HostedEventDoorMoveRequest, HostedEventDoorRecord>(
               HttpMethod.Post, $"{DoorUrl(orgId, eventId)}/arrive", request, token);

    public Task<(HostedEventDoorRecord? Result, string? Error)> DoorLeaveAsync(
        Guid orgId, Guid eventId, HostedEventDoorMoveRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<HostedEventDoorMoveRequest, HostedEventDoorRecord>(
               HttpMethod.Post, $"{DoorUrl(orgId, eventId)}/leave", request, token);

    public Task<(HostedEventDoorRecord? Result, string? Error)> DoorUndoAsync(
        Guid orgId, Guid eventId, HostedEventDoorMoveRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<HostedEventDoorMoveRequest, HostedEventDoorRecord>(
               HttpMethod.Post, $"{DoorUrl(orgId, eventId)}/undo", request, token);

    public Task<(HostedEventDoorRecord? Result, string? Error)> DoorWalkUpAsync(
        Guid orgId, Guid eventId, HostedEventWalkUpRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<HostedEventWalkUpRequest, HostedEventDoorRecord>(
               HttpMethod.Post, $"{DoorUrl(orgId, eventId)}/walk-up", request, token);

    public Task<(HostedEventDoorRecord? Result, string? Error)> DoorUndoWalkUpAsync(
        Guid orgId, Guid eventId, Guid walkUpId, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<object, HostedEventDoorRecord>(
               HttpMethod.Delete, $"{DoorUrl(orgId, eventId)}/walk-up/{walkUpId}", new { }, token);

    // ── who is helping (item 235 phase 7) ────────────────────────────────────

    private static string StaffUrl(Guid orgId, Guid eventId)
        => $"/api/organizations/{orgId}/events/{eventId}/staff";

    public Task<ItemResult<HostedEventStaffListRecord>> GetEventStaffAsync(
        Guid orgId, Guid eventId, CancellationToken token = default)
        => _api.GetItemAsync<HostedEventStaffListRecord>(StaffUrl(orgId, eventId), token);

    public Task<(HostedEventStaffListRecord? Result, string? Error)> SaveEventStaffAsync(
        Guid orgId, Guid eventId, SaveHostedEventStaffRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<SaveHostedEventStaffRequest, HostedEventStaffListRecord>(
               HttpMethod.Put, StaffUrl(orgId, eventId), request, token);

    public Task<(HostedEventStaffListRecord? Result, string? Error)> ResendEventStaffInviteAsync(
        Guid orgId, Guid eventId, Guid staffId, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<object, HostedEventStaffListRecord>(
               HttpMethod.Post, $"{StaffUrl(orgId, eventId)}/{staffId}/resend", new { }, token);

    public Task<(HostedEventStaffListRecord? Result, string? Error)> RemoveEventStaffAsync(
        Guid orgId, Guid eventId, Guid staffId, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<object, HostedEventStaffListRecord>(
               HttpMethod.Delete, $"{StaffUrl(orgId, eventId)}/{staffId}", new { }, token);

    public Task<ItemResult<HostedEventStaffInviteRecord>> GetStaffInviteAsync(
        string token, CancellationToken cancellationToken = default)
        => _api.GetItemAsync<HostedEventStaffInviteRecord>(
               $"/api/public/hosted-event-staff/{Uri.EscapeDataString(token)}", cancellationToken);

    public Task<(HostedEventStaffInviteRecord? Result, string? Error)> AcceptStaffInviteAsync(
        string token, CancellationToken cancellationToken = default)
        => _api.SendExpectingReasonAsync<object, HostedEventStaffInviteRecord>(
               HttpMethod.Post,
               $"/api/public/hosted-event-staff/{Uri.EscapeDataString(token)}/accept",
               new { }, cancellationToken);

    public async Task<(MyHostedEventBookingRecord? Held, HoldRefusedRecord? Refused, string? Error)> HoldHostedEventPlacesAsync(
        Guid eventId, HoldHostedEventPlacesRequest request, CancellationToken token = default)
    {
        var (held, error, refused) = await _api.SendExpectingConflictAsync<HoldHostedEventPlacesRequest, MyHostedEventBookingRecord, HoldRefusedRecord>(
            HttpMethod.Post, $"/api/public/hosted-events/{eventId}/holds", request, token);
        return (held, refused, error);
    }

    public Task<ItemResult<HostedEventCopyPreviewRecord>> GetHostedEventCopyPreviewAsync(
        Guid orgId, Guid eventId, CancellationToken token = default)
        => _api.GetItemAsync<HostedEventCopyPreviewRecord>($"/api/organizations/{orgId}/events/{eventId}/copy", token);

    public Task<(HostedEventCopyResultRecord? Result, string? Error)> CopyHostedEventAsync(
        Guid orgId, Guid eventId, CopyHostedEventRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<CopyHostedEventRequest, HostedEventCopyResultRecord>(
               HttpMethod.Post, $"/api/organizations/{orgId}/events/{eventId}/copy", request, token);

    public async Task<(byte[] Data, string FileName)?> DownloadHostedEventBookingsCsvAsync(
        Guid orgId, Guid eventId, CancellationToken token = default)
    {
        var result = await _api.GetBytesAsync(
            $"/api/organizations/{orgId}/events/{eventId}/bookings/export.csv", "bookings.csv", token);
        return result is { } r ? (r.Data, r.FileName) : null;
    }

    public Task<ItemResult<BookingContactRecord>> GetMyBookingContactAsync(CancellationToken token = default)
        => _api.GetItemAsync<BookingContactRecord>("/api/public/hosted-events/my-contact", token);

    public async Task<(HostedEventEmailPickPlacedRecord? Placed, HoldRefusedRecord? Refused, string? Error)> PickHostedEventPlacesByEmailAsync(
        Guid eventId, PickHostedEventPlacesByEmailRequest request, CancellationToken token = default)
    {
        var (placed, error, refused) = await _api.SendExpectingConflictAsync<PickHostedEventPlacesByEmailRequest, HostedEventEmailPickPlacedRecord, HoldRefusedRecord>(
            HttpMethod.Post, $"/api/public/hosted-events/{eventId}/email-picks", request, token);
        return (placed, refused, error);
    }

    public Task<ItemResult<HostedEventEmailPickRecord>> GetEmailPickAsync(string pickToken, CancellationToken token = default)
        => _api.GetAnonymousItemAsync<HostedEventEmailPickRecord>(
               $"/api/public/hosted-events/email-picks/{Uri.EscapeDataString(pickToken)}", token);

    public Task<(HostedEventEmailPickRecord? Result, string? Error)> ConfirmEmailPickAsync(
        string pickToken, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<object, HostedEventEmailPickRecord>(
               HttpMethod.Post, $"/api/public/hosted-events/email-picks/{Uri.EscapeDataString(pickToken)}/confirm",
               new { }, token);

    public Task<(HostedEventEmailPickRecord? Result, string? Error)> LetGoEmailPickAsync(
        string pickToken, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<object, HostedEventEmailPickRecord>(
               HttpMethod.Delete, $"/api/public/hosted-events/email-picks/{Uri.EscapeDataString(pickToken)}",
               new { }, token);

    public Task<(bool Sent, string? Error)> RequestHostedEventAttendanceAsync(
        Guid eventId, RequestEventAttendanceRequest request, CancellationToken token = default)
        => _api.PostAnonymousExpectingReasonAsync($"/api/public/event-attendance/{eventId}/request", request, token);

    public Task<ItemResult<PublicHostedEventPlanRecord>> GetPublicHostedEventPlanAsync(
        Guid eventId, CancellationToken token = default)
        => _api.GetItemAsync<PublicHostedEventPlanRecord>(
               $"/api/public/hosted-events/{eventId}/plan", token);

    public Task<(MyHostedEventBookingRecord? Result, string? Error)> EmailMyHostedEventPassAsync(
        Guid eventId, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<object, MyHostedEventBookingRecord>(
               HttpMethod.Post, $"/api/public/hosted-events/{eventId}/my-booking/pass/email",
               new { }, token);

    public Task<ItemResult<EventBookingAlertSettingsRecord>> GetEventBookingAlertSettingsAsync(
        CancellationToken token = default)
        => _api.GetItemAsync<EventBookingAlertSettingsRecord>("/api/me/event-booking-alerts", token);

    public Task<(EventBookingAlertSettingsRecord? Result, string? Error)> SetEventBookingAlertModeAsync(
        Guid orgId, SetEventBookingAlertModeRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<SetEventBookingAlertModeRequest, EventBookingAlertSettingsRecord>(
               HttpMethod.Put, $"/api/me/event-booking-alerts/{orgId}", request, token);
}
