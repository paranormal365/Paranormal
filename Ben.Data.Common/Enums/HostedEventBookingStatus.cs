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
/// <para><b>Only <see cref="Confirmed"/> holds a room-night</b> (DECISION 7). A request holds
/// nothing, so a queue of hopefuls cannot fill a house — but requests are shown to the venue as
/// "asked for", because over-asking is a fact a host needs to see rather than one the counting
/// quietly swallows.</para>
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
}
