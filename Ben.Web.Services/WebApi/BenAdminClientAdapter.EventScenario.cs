using Ben.Service.Models.Entities;
using Ben.Service.Models.FieldLaunches;

namespace Ben.Web.Services.WebApi;

/// <summary>The event-scenario slice — see <see cref="IBenEventScenarioClient"/> for the contract.</summary>
public sealed partial class BenAdminClientAdapter
{
    private static string DoorTicketsUrl(Guid orgId, Guid eventId)
        => $"/api/organizations/{orgId}/events/{eventId}/door-tickets";

    private static string PublicDoorTicketUrl(string doorToken)
        => $"/api/public/door-tickets/{Uri.EscapeDataString(doorToken)}";

    private static string TourRoomUrl(Guid dateId)
        => $"/api/public/tour-dates/{dateId}/room";

    public Task<LoadResult<PublicHostedEventMapPin>> GetHostedEventMapPinsAsync(CancellationToken token = default)
        => _api.GetAnonymousListAsync<PublicHostedEventMapPin>("/api/public/hosted-events/map?maxResults=500", token);

    public Task<(HostedEventGuestInvitesRecord? Result, string? Error)> InviteEventGuestsAsync(
        Guid orgId, Guid eventId, InviteHostedEventGuestsRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<InviteHostedEventGuestsRequest, HostedEventGuestInvitesRecord>(
               HttpMethod.Post, $"/api/organizations/{orgId}/events/{eventId}/bookings/on-behalf/invite-many", request, token);

    public Task<(HostedEventAfterRecord? Result, string? Error)> SetGuestCameAsync(
        Guid orgId, Guid eventId, Guid bookingId, bool? came, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<SetHostedEventGuestCameRequest, HostedEventAfterRecord>(
               HttpMethod.Put, $"/api/organizations/{orgId}/events/{eventId}/after/guests/{bookingId}/came",
               new SetHostedEventGuestCameRequest(came), token);

    public Task<(HostedEventResultsSentRecord? Result, string? Error)> SendEventResultsAsync(
        Guid orgId, Guid eventId, SendHostedEventResultsRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<SendHostedEventResultsRequest, HostedEventResultsSentRecord>(
               HttpMethod.Post, $"/api/organizations/{orgId}/events/{eventId}/after/results", request, token);

    public Task<ItemResult<HostedEventDoorTicketsRecord>> GetDoorTicketsAsync(Guid orgId, Guid eventId, CancellationToken token = default)
        => _api.GetItemAsync<HostedEventDoorTicketsRecord>(DoorTicketsUrl(orgId, eventId), token);

    public Task<(HostedEventDoorTicketsRecord? Result, string? Error)> SetDoorTicketsAsync(
        Guid orgId, Guid eventId, bool on, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<SetHostedEventDoorTicketsRequest, HostedEventDoorTicketsRecord>(
               HttpMethod.Put, DoorTicketsUrl(orgId, eventId), new SetHostedEventDoorTicketsRequest(on), token);

    public Task<(HostedEventDoorTicketsRecord? Result, string? Error)> ConfirmDoorTicketAsync(
        Guid orgId, Guid eventId, Guid bookingId, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<object, HostedEventDoorTicketsRecord>(
               HttpMethod.Post, $"{DoorTicketsUrl(orgId, eventId)}/{bookingId}/confirm", new { }, token);

    public Task<ItemResult<PublicDoorTicketRecord>> GetDoorTicketAsync(string doorToken, CancellationToken token = default)
        => _api.GetItemAsync<PublicDoorTicketRecord>(PublicDoorTicketUrl(doorToken), token);

    public Task<(PublicDoorTicketRecord? Result, string? Error)> AskAtTheDoorAsync(
        string doorToken, AskAtTheDoorRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<AskAtTheDoorRequest, PublicDoorTicketRecord>(
               HttpMethod.Post, PublicDoorTicketUrl(doorToken), request, token);

    public Task<ItemResult<HostedEventClassesRecord>> GetClassAttendanceAsync(Guid orgId, Guid eventId, CancellationToken token = default)
        => _api.GetItemAsync<HostedEventClassesRecord>($"/api/organizations/{orgId}/events/{eventId}/sessions/attendance", token);

    public Task<(HostedEventSessionRosterRecord? Result, string? Error)> SetSessionAttendanceAsync(
        Guid orgId, Guid eventId, Guid sessionId, Guid signUpId, bool attended, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<SetHostedEventSessionAttendanceRequest, HostedEventSessionRosterRecord>(
               HttpMethod.Put, $"/api/organizations/{orgId}/events/{eventId}/sessions/{sessionId}/sign-ups/{signUpId}/attended",
               new SetHostedEventSessionAttendanceRequest(attended), token);

    /// <summary>The launch's refusal, which comes back as <c>{ "message": … }</c>.</summary>
    private sealed record LaunchRefusal(string? Message);

    public async Task<(LaunchOutcomeRecord? Result, string? Error)> StartHostedEventSessionsAsync(
        Guid hostedEventId, CancellationToken token = default)
    {
        var (result, conflict) = await _api.PostExpectingConflictAsync<LaunchRequest, LaunchOutcomeRecord, LaunchRefusal>(
            "/api/field-launches", new LaunchRequest("hosted-event", hostedEventId), token);
        return result is not null
            ? (result, null)
            : (null, conflict?.Message ?? "The sessions couldn't be started just now.");
    }

    public Task<ItemResult<TourRoomRecord>> GetTourRoomAsync(Guid dateId, CancellationToken token = default)
        => _api.GetItemAsync<TourRoomRecord>(TourRoomUrl(dateId), token);

    public Task<(TourRoomRecord? Result, string? Error)> PostTourRoomAsync(Guid dateId, string body, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<PostTourRoomMessageRequest, TourRoomRecord>(
               HttpMethod.Post, TourRoomUrl(dateId), new PostTourRoomMessageRequest(body), token);

    public Task<(TourRoomRecord? Result, string? Error)> RemoveTourRoomMessageAsync(Guid dateId, Guid messageId, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<object, TourRoomRecord>(
               HttpMethod.Delete, $"{TourRoomUrl(dateId)}/messages/{messageId}", null, token);
}
