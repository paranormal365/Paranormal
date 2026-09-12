using Ben.Service.Models.Entities;
using Ben.Web.Services.WebApi;

namespace Ben.Web.Services;

/// <summary>
/// Everything that happens to a hosted event after it is published — the plan, the bookings, the
/// menus, the passes and the door — and the guest's side of the same weekend (item 235).
/// </summary>
/// <remarks>
/// <para>Split from <see cref="IBenOrganizationClient"/>, which already owns the event itself (its
/// dates, its publishing, its cancellation), because none of the fifteen hosted route groups had a
/// website method at all when the plan of record was written on 2026-09-12. The organization slice
/// is where an event is created; this is where a weekend is run.</para>
///
/// <para><b>Three shapes, chosen by what the page has to say when it goes wrong.</b> Reads that
/// return one thing use <see cref="ItemResult{T}"/>, because the plan's rule is that a 403 must
/// never read as "not found" — a member refused the dietary sheet is not looking at an event with
/// no guests. Mutations return <c>(Result, Error)</c> and keep the server's sentence, because every
/// hosted refusal is written to be read aloud to the person in front of you. The one exception is
/// <see cref="SetEventLayoutAsync"/>, which can also come back with a structure: the units to ring.</para>
///
/// <para>Every route here is authenticated. Reading the board is open to any member; deciding,
/// the kitchen's sheet and the door take the deciding permission; the guest routes are the
/// signed-in person's own booking and nobody else's. The WebApi enforces all of it — these methods
/// only carry the answer back honestly.</para>
/// </remarks>
public interface IBenEventBookingClient
{
    // ── the plan: what this event allocates ──────────────────────────────────

    /// <summary>What this event allocates — rooms or seats — and what each of them holds.</summary>
    /// <remarks>Readable by any member: the booking board needs it to draw its grid.</remarks>
    Task<ItemResult<HostedEventLayoutRecord>> GetEventLayoutAsync(
        Guid orgId, Guid eventId, CancellationToken token = default);

    /// <summary>
    /// Replaces the whole plan, and comes back three ways.
    /// </summary>
    /// <remarks>
    /// <para>Saved: <c>Result</c>. Refused for a reason a sentence can carry — a room that is not
    /// this place's, a kind change on a plan with bookings — <c>Error</c>. Refused because dropping
    /// a unit would strand a confirmed party: <c>Conflict</c>, which carries the sentence AND the
    /// ids of the units still booked, so the designer rings them instead of leaving a venue with
    /// four hundred seats reading "C4 and C5 still have confirmed bookings" and hunting for row C.</para>
    /// </remarks>
    Task<(HostedEventLayoutRecord? Result, string? Error, LayoutRefusalRecord? Conflict)> SetEventLayoutAsync(
        Guid orgId, Guid eventId, SetHostedEventLayoutRequest request, CancellationToken token = default);

    // ── the board: every booking, and how full the house is ──────────────────

    /// <summary>The whole weekend: units offered, how full each is per night, and every booking.</summary>
    /// <remarks>
    /// Any member may read it — knowing how full the house is is not a billing question. Dietary
    /// notes are withheld from a member who cannot decide a booking, because they are health
    /// information about named guests; the record says whether they were included.
    /// </remarks>
    Task<ItemResult<HostedEventBookingBoardRecord>> GetEventBookingBoardAsync(
        Guid orgId, Guid eventId, CancellationToken token = default);

    /// <summary>Agrees to a booking and puts the party where they will actually sleep.</summary>
    /// <remarks>
    /// The units sent need not be what was asked for: moving a party of three out of a double and
    /// into the suite is the commonest thing a host does, and making them turn it down and ask
    /// again would be absurd.
    /// </remarks>
    Task<(HostedEventBookingRecord? Result, string? Error)> ConfirmEventBookingAsync(
        Guid orgId, Guid eventId, Guid bookingId, ConfirmHostedEventBookingRequest request,
        CancellationToken token = default);

    /// <summary>Says no. The guest is told, because a guest who is not coming must know.</summary>
    Task<(HostedEventBookingRecord? Result, string? Error)> TurnDownEventBookingAsync(
        Guid orgId, Guid eventId, Guid bookingId, string? decisionNote, CancellationToken token = default);

    /// <summary>
    /// Releases a booking, whether it was confirmed or still waiting.
    /// </summary>
    /// <remarks>
    /// Frees the unit-nights and removes the umbrella attendee row, so every count that already
    /// exists stops including them. The booking itself stays: the venue catered against it.
    /// </remarks>
    Task<(HostedEventBookingRecord? Result, string? Error)> CancelEventBookingAsync(
        Guid orgId, Guid eventId, Guid bookingId, string? decisionNote, CancellationToken token = default);

    /// <summary>Changes a booking: the party size, the units, the guests, the note.</summary>
    /// <remarks>
    /// Ben asked for editable reservations, and a real weekend needs them: somebody drops out on
    /// the Thursday, a party moves rooms, a name was spelled wrong. The server re-checks capacity,
    /// excluding the booking's own beds, so a move is never refused by the room it is leaving.
    /// </remarks>
    Task<(HostedEventBookingRecord? Result, string? Error)> EditEventBookingAsync(
        Guid orgId, Guid eventId, Guid bookingId, EditHostedEventBookingRequest request,
        CancellationToken token = default);

    /// <summary>
    /// Books somebody who asked by phone, by email or at the door — an existing account only.
    /// </summary>
    /// <remarks>
    /// Somebody with no account goes through <see cref="InviteEventGuestAsync"/> instead, because
    /// creating an account for a person who never asked for one is the guest door's job and there
    /// is exactly one of those.
    /// </remarks>
    Task<(HostedEventBookingRecord? Result, string? Error)> CreateEventBookingOnBehalfAsync(
        Guid orgId, Guid eventId, CreateHostedEventBookingOnBehalfRequest request,
        CancellationToken token = default);

    /// <summary>
    /// Emails an invitation to somebody with no account. Nothing is held until they click.
    /// </summary>
    /// <remarks>
    /// The answer says an email was sent, not that a booking exists — a host who thinks they have
    /// held a room for a phone caller will sell it twice. It answers the same way whether or not
    /// that address has an account, exactly as the walk-up invitation does.
    /// </remarks>
    Task<(HostedEventGuestInviteRecord? Result, string? Error)> InviteEventGuestAsync(
        Guid orgId, Guid eventId, InviteHostedEventGuestRequest request, CancellationToken token = default);

    // ── the kitchen ──────────────────────────────────────────────────────────

    /// <summary>
    /// What the kitchen has to cook differently — confirmed parties by default.
    /// </summary>
    /// <remarks>
    /// <paramref name="includeRequests"/> folds in the parties still waiting, for a host ordering
    /// ahead of a weekend that has not been decided yet; the record says which it is, so a cook
    /// cannot read a provisional number as a settled one. Takes the deciding permission, not
    /// membership: everything in it is health information about named individuals, which is why
    /// this is an <see cref="ItemResult{T}"/> and a refusal must not look like an empty sheet.
    /// </remarks>
    Task<ItemResult<HostedEventDietaryRecord>> GetEventDietaryAsync(
        Guid orgId, Guid eventId, bool includeRequests, CancellationToken token = default);

    /// <summary>Every sitting of this event, in the order they are served.</summary>
    Task<ItemResult<HostedEventMenusRecord>> GetEventMenusAsync(
        Guid orgId, Guid eventId, CancellationToken token = default);

    /// <summary>Replaces the whole set of menus, exactly as the plan replaces the units.</summary>
    /// <remarks>
    /// One card a host fills in and saves; a half-saved service with the pudding missing is worse
    /// than no menu at all. A sitting must belong to a night of this event.
    /// </remarks>
    Task<(HostedEventMenusRecord? Result, string? Error)> SetEventMenusAsync(
        Guid orgId, Guid eventId, SetHostedEventMenusRequest request, CancellationToken token = default);

    // ── passes and the door ──────────────────────────────────────────────────

    /// <summary>Gives a confirmed booking a pass, or hands back the one it already has.</summary>
    /// <remarks>
    /// Confirming already issues one, so this is for the host who cannot see a pass and wants one
    /// — deliberately the same call, not a second kind of pass. Two live codes for one party is two
    /// codes at a door, one of which is the wrong one.
    /// </remarks>
    Task<(HostedEventPassRecord? Result, string? Error)> IssueEventPassAsync(
        Guid orgId, Guid eventId, Guid bookingId, CancellationToken token = default);

    /// <summary>Withdraws a pass, in words the door can read out.</summary>
    Task<(HostedEventPassRecord? Result, string? Error)> RevokeEventPassAsync(
        Guid orgId, Guid eventId, Guid bookingId, string reason, CancellationToken token = default);

    /// <summary>Withdraws the current pass and issues a fresh one in its place.</summary>
    /// <remarks>
    /// For the guest who lost the letter. The new pass remembers the one it replaced, so "what
    /// happened to the code I was sent" always has an answer. The reason may be left blank; the
    /// server supplies "The venue issued a replacement pass."
    /// </remarks>
    Task<(HostedEventPassRecord? Result, string? Error)> ReissueEventPassAsync(
        Guid orgId, Guid eventId, Guid bookingId, string? reason, CancellationToken token = default);

    /// <summary>Sends the guest their pass again, to the address on the booking.</summary>
    /// <remarks>
    /// Phase 1 of the plan of record: the pass is already emailed at confirmation, but a guest who
    /// deleted the mail and a host who cannot forward it needed a button. Nothing about the pass
    /// changes — the same code, sent again — which is why this is not a reissue.
    /// </remarks>
    Task<(HostedEventPassRecord? Result, string? Error)> EmailEventPassAsync(
        Guid orgId, Guid eventId, Guid bookingId, CancellationToken token = default);

    /// <summary>
    /// Scans a code at the door and says who has just walked in.
    /// </summary>
    /// <remarks>
    /// Every refusal is a sentence somebody can say aloud to the person in front of them; a second
    /// scan is not a refusal. <paramref name="checkIn"/> false looks without admitting anybody,
    /// which is what a host testing a code before the doors open wants.
    /// </remarks>
    Task<(HostedEventScanResult? Result, string? Error)> ScanEventPassAsync(
        Guid orgId, Guid eventId, string code, bool checkIn, CancellationToken token = default);

    // ── the guest's own weekend ──────────────────────────────────────────────

    /// <summary>Every hosted event this person has a booking at, coming up or recently past.</summary>
    Task<LoadResult<MyHostedEventBookingRecord>> GetMyHostedEventBookingsAsync(CancellationToken token = default);

    /// <summary>This person's booking at one event.</summary>
    /// <remarks>
    /// The server answers 404 when they have none, which arrives here as <c>Failed</c> with no
    /// reason — the same as an unreachable API. A page that wants to offer "ask for a place" on
    /// that state should read the event's public record first, which says whether bookings are
    /// open, rather than inferring it from this failing.
    /// </remarks>
    Task<ItemResult<MyHostedEventBookingRecord>> GetMyHostedEventBookingAsync(
        Guid eventId, CancellationToken token = default);

    /// <summary>
    /// Asks the venue for a place. Records a request and nothing more.
    /// </summary>
    /// <remarks>
    /// The units named are a preference — the venue puts them where it can when it confirms — so
    /// this is deliberately not checked against capacity: refusing a request because a room is
    /// full would close the waiting list the host wants.
    /// </remarks>
    Task<(MyHostedEventBookingRecord? Result, string? Error)> RequestHostedEventBookingAsync(
        Guid eventId, RequestHostedEventBookingRequest request, CancellationToken token = default);

    /// <summary>Changes the guest's own booking.</summary>
    /// <remarks>
    /// A change to a confirmed booking sends it back to the venue as a request, because the thing
    /// the venue agreed to is not the thing being asked for any more — the answer's status says so,
    /// and the page must show it rather than "saved".
    /// </remarks>
    Task<(MyHostedEventBookingRecord? Result, string? Error)> UpdateMyHostedEventBookingAsync(
        Guid eventId, EditHostedEventBookingRequest request, CancellationToken token = default);

    /// <summary>Says the guest has read what the venue decided. Clears their bell.</summary>
    /// <remarks>Any answer, not only a yes: a refusal that could never be acknowledged would sit on somebody's bell for ever.</remarks>
    Task<(MyHostedEventBookingRecord? Result, string? Error)> AcknowledgeMyHostedEventBookingAsync(
        Guid eventId, CancellationToken token = default);

    /// <summary>
    /// Takes a request back, or asks the venue to release a confirmed booking.
    /// </summary>
    /// <remarks>
    /// A request is simply withdrawn. A confirmed booking is not: the venue has catered, staffed
    /// and possibly turned somebody else away against it, so the server records the ask and the
    /// host releases it from their own screen. Either way <c>Withdrawn</c> is true — the page reads
    /// the booking again to learn which happened. A booking already turned down answers 409 with a
    /// sentence, which arrives as <c>Error</c>.
    /// </remarks>
    Task<(bool Withdrawn, string? Error)> WithdrawMyHostedEventBookingAsync(
        Guid eventId, string? reason, CancellationToken token = default);

    /// <summary>The guest's own pass, with enough words on it to get in without a scanner.</summary>
    /// <remarks>
    /// A revoked pass comes back as itself (phase 1 fixed the door that said "not issued yet"), so
    /// the page can say what happened to it. A guest whose place is not confirmed is told what to
    /// wait for, as a sentence, rather than being shown an empty box.
    /// </remarks>
    Task<ItemResult<MyHostedEventPassRecord>> GetMyHostedEventPassAsync(
        Guid eventId, CancellationToken token = default);

    /// <summary>What is being served, for a guest whose place the venue has agreed to.</summary>
    /// <remarks>
    /// Confirmed guests, not the world: a venue that has not sold the weekend yet may not want its
    /// catering costed by the hotel down the road. A guest still waiting is told the venue
    /// publishes it once the place is agreed — a refusal with a sentence, not an empty menu.
    /// </remarks>
    Task<ItemResult<HostedEventMenusRecord>> GetMyHostedEventMenusAsync(
        Guid eventId, CancellationToken token = default);
}
