namespace Ben.Data.Common.Enums;

/// <summary>
/// Where a booking stands between the guest asking and the venue agreeing.
/// </summary>
/// <remarks>
/// <para><b>The site never takes the money</b> (DECISION 1). <see cref="Confirmed"/> means the
/// venue and the guest have settled how payment happens, exactly as
/// <see cref="TourSeatStatus.Reserved"/> does on a walk, and nothing here should ever be read as a
/// payment record.</para>
///
/// <para><b>Held and Confirmed hold a unit-night; Requested does not</b> (item 235 phase 4,
/// superseding the original DECISION 7 that only Confirmed did). Which of the two a guest lands in
/// is the event's own choice: on an Ask event they request and hold nothing, so a queue of hopefuls
/// cannot fill a house and the overflow is a waiting list; on a Pick event they choose on the plan
/// and what they chose is held until the venue answers, so two people cannot spend an evening
/// choosing the same row. Requests are still shown to the venue as "asked for", because over-asking
/// is a fact a host needs to see rather than one the counting quietly swallows.</para>
///
/// <para><b>Why not <see cref="TourSeatStatus"/> itself.</b> That enum has three values and no way
/// to say a guest withdrew: a walk's sign-up is simply deleted. A booking cannot be, because the
/// room-nights it held, the guests it named and the dietary notes they gave are a record the venue
/// has already catered against — so a cancellation is a state, kept and dated, not a disappearance.
/// The word is different too: a room is <i>confirmed</i>, not <i>reserved</i>, and a host reading
/// their own screen should see their own vocabulary.</para>
///
/// <para>Append-only. The number is stored.</para>
/// </remarks>
public enum HostedEventBookingStatus
{
    /// <summary>Asked for. Holds no room and no place; the venue has not looked at it yet.</summary>
    Requested = 0,

    /// <summary>The venue agreed. The room-nights are held and the umbrella attendee row exists.</summary>
    Confirmed = 1,

    /// <summary>The venue said no. Said plainly, because a guest who is not coming must know.</summary>
    TurnedDown = 2,

    /// <summary>
    /// The guest withdrew, or the venue released it after confirming. Frees the room-nights and
    /// keeps the record of what was once catered for.
    /// </summary>
    Cancelled = 3,

    /// <summary>
    /// The guest picked these places on the plan and they are theirs until the venue answers.
    /// </summary>
    /// <remarks>
    /// <para><b>It counts against capacity exactly as Confirmed does</b>, which is the whole point:
    /// everybody else sees those squares as waiting on an answer and cannot take them. A hold that
    /// did not count would be a promise the site could not keep.</para>
    ///
    /// <para><b>Only ever on a Pick event.</b> Writing one on an Ask event would mean a guest had
    /// taken something the venue expected to allocate itself.</para>
    /// </remarks>
    Held = 4,

    /// <summary>
    /// A hold nobody answered in time. The places went back; the guest stays on the list.
    /// </summary>
    /// <remarks>
    /// Its own state rather than reverting to Requested, because the two are different facts and a
    /// guest reading their own booking deserves the true one: they chose seats, nobody answered,
    /// and the seats are gone. What they may still do about it is offer to pick again.
    /// </remarks>
    Expired = 5,
}
