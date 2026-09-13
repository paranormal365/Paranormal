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

    private static string VenuePhotosUrl(Guid orgId, Guid profileId) => $"{VenueProfilesUrl(orgId)}/{profileId}/photos";

    public Task<LoadResult<VenuePhotoRecord>> GetVenuePhotosAsync(Guid orgId, Guid profileId, CancellationToken token = default)
        => _api.GetListAsync<VenuePhotoRecord>(VenuePhotosUrl(orgId, profileId), token);

    public Task<(List<VenuePhotoRecord>? Result, string? Error)> AddVenuePhotoAsync(
        Guid orgId, Guid profileId, MultipartFormDataContent content, CancellationToken token = default)
        => _api.PostMultipartExpectingReasonAsync<List<VenuePhotoRecord>>(VenuePhotosUrl(orgId, profileId), content, token);

    public Task<(List<VenuePhotoRecord>? Result, string? Error)> UpdateVenuePhotoAsync(
        Guid orgId, Guid profileId, Guid photoId, UpdateVenuePhotoRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<UpdateVenuePhotoRequest, List<VenuePhotoRecord>>(
               HttpMethod.Put, $"{VenuePhotosUrl(orgId, profileId)}/{photoId}", request, token);

    public Task<(List<VenuePhotoRecord>? Result, string? Error)> AcceptVenuePhotoAsync(
        Guid orgId, Guid profileId, Guid photoId, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<object, List<VenuePhotoRecord>>(
               HttpMethod.Post, $"{VenuePhotosUrl(orgId, profileId)}/{photoId}/accept", new { }, token);

    public Task<(List<VenuePhotoRecord>? Result, string? Error)> DeleteVenuePhotoAsync(
        Guid orgId, Guid profileId, Guid photoId, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<object, List<VenuePhotoRecord>>(
               HttpMethod.Delete, $"{VenuePhotosUrl(orgId, profileId)}/{photoId}", new { }, token);

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

    private static string ContactsUrl(Guid placeId) => $"/api/places/{placeId}/contacts";

    private static string ClaimsUrl(Guid orgId) => $"/api/organizations/{orgId}/venue-claims";

    public Task<ItemResult<PlaceContactListRecord>> GetPlaceContactsAsync(Guid placeId, bool signedIn, CancellationToken token = default)
        => signedIn
            ? _api.GetItemAsync<PlaceContactListRecord>(ContactsUrl(placeId), token)
            : _api.GetAnonymousItemAsync<PlaceContactListRecord>($"/api/public/places/{placeId}/contacts", token);

    public Task<(PlaceContactListRecord? Result, string? Error)> AddPlaceContactAsync(
        Guid placeId, AddPlaceContactRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<AddPlaceContactRequest, PlaceContactListRecord>(
               HttpMethod.Post, ContactsUrl(placeId), request, token);

    public Task<(PlaceContactListRecord? Result, string? Error)> RemovePlaceContactAsync(
        Guid placeId, Guid contactId, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<object, PlaceContactListRecord>(
               HttpMethod.Delete, $"{ContactsUrl(placeId)}/{contactId}", new { }, token);

    public Task<(PlaceContactListRecord? Result, string? Error)> ConfirmPlaceContactAsync(
        Guid placeId, Guid contactId, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<object, PlaceContactListRecord>(
               HttpMethod.Post, $"{ContactsUrl(placeId)}/{contactId}/confirm", new { }, token);

    public Task<ItemResult<VenueClaimStartRecord>> GetVenueClaimStartAsync(Guid orgId, Guid placeId, CancellationToken token = default)
        => _api.GetItemAsync<VenueClaimStartRecord>($"{ClaimsUrl(orgId)}/start?place={placeId}", token);

    public Task<(VenueClaimRecord? Result, string? Error)> StartVenueClaimAsync(
        Guid orgId, StartVenueClaimRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<StartVenueClaimRequest, VenueClaimRecord>(
               HttpMethod.Post, ClaimsUrl(orgId), request, token);

    public Task<(VenueClaimRecord? Result, string? Error)> SubmitVenueClaimCodeAsync(
        Guid orgId, Guid claimId, VenueClaimCodeRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<VenueClaimCodeRequest, VenueClaimRecord>(
               HttpMethod.Post, $"{ClaimsUrl(orgId)}/{claimId}/code", request, token);

    public Task<(VenueClaimRecord? Result, string? Error)> ResendVenueClaimCodeAsync(
        Guid orgId, Guid claimId, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<object, VenueClaimRecord>(
               HttpMethod.Post, $"{ClaimsUrl(orgId)}/{claimId}/resend", new { }, token);

    public Task<(VenueClaimRecord? Result, string? Error)> WithdrawVenueClaimAsync(
        Guid orgId, Guid claimId, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<object, VenueClaimRecord>(
               HttpMethod.Post, $"{ClaimsUrl(orgId)}/{claimId}/withdraw", new { }, token);

    public Task<ItemResult<VenueClaimRecord>> GetVenueClaimAsync(Guid claimId, CancellationToken token = default)
        => _api.GetItemAsync<VenueClaimRecord>($"/api/venue-claims/{claimId}", token);

    public Task<(VenueClaimRecord? Result, string? Error)> ObjectToVenueClaimAsync(
        Guid claimId, ObjectToVenueClaimRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<ObjectToVenueClaimRequest, VenueClaimRecord>(
               HttpMethod.Post, $"/api/venue-claims/{claimId}/object", request, token);

    public Task<ItemResult<AdminVenueClaimListRecord>> GetAdminVenueClaimsAsync(CancellationToken token = default)
        => _api.GetItemAsync<AdminVenueClaimListRecord>("/api/admin/venue-claims", token);

    public Task<(AdminVenueClaimListRecord? Result, string? Error)> ApproveVenueClaimAsync(
        Guid claimId, DecideVenueClaimRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<DecideVenueClaimRequest, AdminVenueClaimListRecord>(
               HttpMethod.Post, $"/api/admin/venue-claims/{claimId}/approve", request, token);

    public Task<(AdminVenueClaimListRecord? Result, string? Error)> RefuseVenueClaimAsync(
        Guid claimId, DecideVenueClaimRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<DecideVenueClaimRequest, AdminVenueClaimListRecord>(
               HttpMethod.Post, $"/api/admin/venue-claims/{claimId}/refuse", request, token);

    public Task<(AdminVenueClaimListRecord? Result, string? Error)> UnconfirmVenueAsync(
        Guid profileId, DecideVenueClaimRequest request, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<DecideVenueClaimRequest, AdminVenueClaimListRecord>(
               HttpMethod.Post, $"/api/admin/venue-profiles/{profileId}/unconfirm", request, token);
}
