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

    Task<(List<VenueProfileRecord>? Result, string? Error)> SaveVenueProfileAsync(
        Guid orgId, SaveVenueProfileRequest request, CancellationToken token = default);

    // ── anybody ──────────────────────────────────────────────────────────────

    Task<ItemResult<PublicVenueRecord>> GetPublicVenueAsync(string orgUrlName, Guid placeId, CancellationToken token = default);

    /// <summary>Who runs a place as its venue. Empty when nobody has proved it.</summary>
    Task<ItemResult<PlaceVenueRecord>> GetPlaceVenueAsync(Guid placeId, CancellationToken token = default);
}
