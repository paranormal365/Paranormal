using Ben.Service.Models.Entities;

namespace Ben.Web.Services.WebApi;

/// <summary>The venue slice — see <see cref="IBenVenueClient"/> for the contract.</summary>
public sealed partial class BenAdminClientAdapter
{
    private static string EventVenueUrl(Guid orgId, Guid eventId)
        => $"/api/organizations/{orgId}/events/{eventId}/venue";

    private static string VenueProfilesUrl(Guid orgId)
        => $"/api/organizations/{orgId}/venue-profiles";

    public Task<ItemResult<EventVenueRecord>> GetEventVenueAsync(Guid orgId, Guid eventId, CancellationToken token = default)
        => _api.GetItemAsync<EventVenueRecord>(EventVenueUrl(orgId, eventId), token);

    public Task<(EventVenueRecord? Result, string? Error)> AskTheVenueAsync(
        Guid orgId, Guid eventId, AskTheVenueRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<AskTheVenueRequest, EventVenueRecord>(
               HttpMethod.Post, $"{EventVenueUrl(orgId, eventId)}/ask", request, token);

    public Task<(EventVenueRecord? Result, string? Error)> WithdrawVenueQuestionAsync(
        Guid orgId, Guid eventId, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<object, EventVenueRecord>(
               HttpMethod.Post, $"{EventVenueUrl(orgId, eventId)}/withdraw", new { }, token);

    public Task<LoadResult<PlaceRoomRecord>> GetOfferableRoomsAsync(Guid orgId, Guid eventId, CancellationToken token = default)
        => _api.GetListAsync<PlaceRoomRecord>($"/api/organizations/{orgId}/events/{eventId}/layout/rooms", token);

    public Task<ItemResult<VenueRequestListRecord>> GetVenueRequestsAsync(Guid orgId, CancellationToken token = default)
        => _api.GetItemAsync<VenueRequestListRecord>($"/api/organizations/{orgId}/venue-requests", token);

    public Task<(VenueRequestListRecord? Result, string? Error)> ApproveVenueRequestAsync(
        Guid orgId, Guid requestId, ApproveVenueRequestRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<ApproveVenueRequestRequest, VenueRequestListRecord>(
               HttpMethod.Post, $"/api/organizations/{orgId}/venue-requests/{requestId}/approve", request, token);

    public Task<(VenueRequestListRecord? Result, string? Error)> DeclineVenueRequestAsync(
        Guid orgId, Guid requestId, VenueReasonRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<VenueReasonRequest, VenueRequestListRecord>(
               HttpMethod.Post, $"/api/organizations/{orgId}/venue-requests/{requestId}/decline", request, token);

    public Task<(VenueRequestListRecord? Result, string? Error)> RevokeVenueGrantAsync(
        Guid orgId, Guid grantId, VenueReasonRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<VenueReasonRequest, VenueRequestListRecord>(
               HttpMethod.Post, $"/api/organizations/{orgId}/venue-grants/{grantId}/revoke", request, token);

    public Task<LoadResult<VenueProfileRecord>> GetVenueProfilesAsync(Guid orgId, CancellationToken token = default)
        => _api.GetListAsync<VenueProfileRecord>(VenueProfilesUrl(orgId), token);

    public Task<(List<VenueProfileRecord>? Result, string? Error)> SaveVenueProfileAsync(
        Guid orgId, SaveVenueProfileRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<SaveVenueProfileRequest, List<VenueProfileRecord>>(
               HttpMethod.Put, VenueProfilesUrl(orgId), request, token);

    public Task<ItemResult<PublicVenueRecord>> GetPublicVenueAsync(string orgUrlName, Guid placeId, CancellationToken token = default)
        => _api.GetItemAsync<PublicVenueRecord>(
               $"/api/public/venues/{Uri.EscapeDataString(orgUrlName)}/{placeId}", token);

    public Task<ItemResult<PlaceVenueRecord>> GetPlaceVenueAsync(Guid placeId, CancellationToken token = default)
        => _api.GetItemAsync<PlaceVenueRecord>($"/api/public/places/{placeId}/venue", token);
}
