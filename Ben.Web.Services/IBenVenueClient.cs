using Ben.Service.Models.Entities;
using Ben.Web.Services.WebApi;

namespace Ben.Web.Services;

/// <summary>
/// Venues on this site: one group asking another for its building, the answer, taking it back, and
/// the venue's own page (item 235 phase 9).
/// </summary>
/// <remarks>
/// The same three shapes as the booking slice, for the same reasons: single reads are
/// <see cref="ItemResult{T}"/> so a refusal never reads as "nobody runs this place", and writes keep
/// the server's sentence, because every refusal here is something a person has to act on — "you
/// asked on 10/01 and they haven't answered yet" is the whole point of the screen.
/// </remarks>
public interface IBenVenueClient
{
    // ── the organizer, about one event ───────────────────────────────────────

    /// <summary>Whether this event's place has a venue on the site, and what it has said.</summary>
    Task<ItemResult<EventVenueRecord>> GetEventVenueAsync(Guid orgId, Guid eventId, CancellationToken token = default);

    /// <summary>Asks the venue whether this event may be held there, for its exact nights.</summary>
    Task<(EventVenueRecord? Result, string? Error)> AskTheVenueAsync(
        Guid orgId, Guid eventId, AskTheVenueRequest request, CancellationToken token = default);

    /// <summary>Takes back a question the venue has not answered.</summary>
    Task<(EventVenueRecord? Result, string? Error)> WithdrawVenueQuestionAsync(
        Guid orgId, Guid eventId, CancellationToken token = default);

    /// <summary>The rooms this event's plan may place: the group's own, and the venue's when lent.</summary>
    Task<LoadResult<PlaceRoomRecord>> GetOfferableRoomsAsync(Guid orgId, Guid eventId, CancellationToken token = default);

    // ── the venue ────────────────────────────────────────────────────────────

    /// <summary>Everything waiting on this venue, what it has answered, and what it has lent.</summary>
    Task<ItemResult<VenueRequestListRecord>> GetVenueRequestsAsync(Guid orgId, CancellationToken token = default);

    Task<(VenueRequestListRecord? Result, string? Error)> ApproveVenueRequestAsync(
        Guid orgId, Guid requestId, ApproveVenueRequestRequest request, CancellationToken token = default);

    Task<(VenueRequestListRecord? Result, string? Error)> DeclineVenueRequestAsync(
        Guid orgId, Guid requestId, VenueReasonRequest request, CancellationToken token = default);

    /// <summary>Takes back a yes. Every event still to happen under it stops.</summary>
    Task<(VenueRequestListRecord? Result, string? Error)> RevokeVenueGrantAsync(
        Guid orgId, Guid grantId, VenueReasonRequest request, CancellationToken token = default);

    /// <summary>The places this group describes as its venue.</summary>
    Task<LoadResult<VenueProfileRecord>> GetVenueProfilesAsync(Guid orgId, CancellationToken token = default);

    /// <summary>A venue's photo library: its own pictures and organizers' offers (item 235 phase 12).</summary>
    Task<LoadResult<VenuePhotoRecord>> GetVenuePhotosAsync(Guid orgId, Guid profileId, CancellationToken token = default);

    Task<(List<VenuePhotoRecord>? Result, string? Error)> AddVenuePhotoAsync(
        Guid orgId, Guid profileId, MultipartFormDataContent content, CancellationToken token = default);

    Task<(List<VenuePhotoRecord>? Result, string? Error)> UpdateVenuePhotoAsync(
        Guid orgId, Guid profileId, Guid photoId, UpdateVenuePhotoRequest request, CancellationToken token = default);

    Task<(List<VenuePhotoRecord>? Result, string? Error)> AcceptVenuePhotoAsync(
        Guid orgId, Guid profileId, Guid photoId, CancellationToken token = default);

    Task<(List<VenuePhotoRecord>? Result, string? Error)> DeleteVenuePhotoAsync(
        Guid orgId, Guid profileId, Guid photoId, CancellationToken token = default);

    Task<(List<VenueProfileRecord>? Result, string? Error)> SaveVenueProfileAsync(
        Guid orgId, SaveVenueProfileRequest request, CancellationToken token = default);

    // ── anybody ──────────────────────────────────────────────────────────────

    Task<ItemResult<PublicVenueRecord>> GetPublicVenueAsync(string orgUrlName, Guid placeId, CancellationToken token = default);

    /// <summary>Who runs a place as its venue. Empty when nobody has proved it.</summary>
    Task<ItemResult<PlaceVenueRecord>> GetPlaceVenueAsync(Guid placeId, CancellationToken token = default);

    // ── a place's contact details ────────────────────────────────────────────

    /// <summary>The public details, plus the viewer's own groups' private ones when signed in.</summary>
    Task<ItemResult<PlaceContactListRecord>> GetPlaceContactsAsync(Guid placeId, bool signedIn, CancellationToken token = default);

    Task<(PlaceContactListRecord? Result, string? Error)> AddPlaceContactAsync(
        Guid placeId, AddPlaceContactRequest request, CancellationToken token = default);

    Task<(PlaceContactListRecord? Result, string? Error)> RemovePlaceContactAsync(
        Guid placeId, Guid contactId, CancellationToken token = default);

    /// <summary>The confirmed venue vouches for a public detail somebody else added.</summary>
    Task<(PlaceContactListRecord? Result, string? Error)> ConfirmPlaceContactAsync(
        Guid placeId, Guid contactId, CancellationToken token = default);

    // ── claiming a place ─────────────────────────────────────────────────────

    Task<ItemResult<VenueClaimStartRecord>> GetVenueClaimStartAsync(Guid orgId, Guid placeId, CancellationToken token = default);

    Task<(VenueClaimRecord? Result, string? Error)> StartVenueClaimAsync(
        Guid orgId, StartVenueClaimRequest request, CancellationToken token = default);

    Task<(VenueClaimRecord? Result, string? Error)> SubmitVenueClaimCodeAsync(
        Guid orgId, Guid claimId, VenueClaimCodeRequest request, CancellationToken token = default);

    Task<(VenueClaimRecord? Result, string? Error)> ResendVenueClaimCodeAsync(
        Guid orgId, Guid claimId, CancellationToken token = default);

    Task<(VenueClaimRecord? Result, string? Error)> WithdrawVenueClaimAsync(
        Guid orgId, Guid claimId, CancellationToken token = default);

    /// <summary>A claim, for the claimant, a group that may object to it, or a reviewer.</summary>
    Task<ItemResult<VenueClaimRecord>> GetVenueClaimAsync(Guid claimId, CancellationToken token = default);

    Task<(VenueClaimRecord? Result, string? Error)> ObjectToVenueClaimAsync(
        Guid claimId, ObjectToVenueClaimRequest request, CancellationToken token = default);

    // ── the reviewer ─────────────────────────────────────────────────────────

    Task<ItemResult<AdminVenueClaimListRecord>> GetAdminVenueClaimsAsync(CancellationToken token = default);

    Task<(AdminVenueClaimListRecord? Result, string? Error)> ApproveVenueClaimAsync(
        Guid claimId, DecideVenueClaimRequest request, CancellationToken token = default);

    Task<(AdminVenueClaimListRecord? Result, string? Error)> RefuseVenueClaimAsync(
        Guid claimId, DecideVenueClaimRequest request, CancellationToken token = default);

    /// <summary>Undoes a confirmation that turned out to be wrong.</summary>
    Task<(AdminVenueClaimListRecord? Result, string? Error)> UnconfirmVenueAsync(
        Guid profileId, DecideVenueClaimRequest request, CancellationToken token = default);
}
