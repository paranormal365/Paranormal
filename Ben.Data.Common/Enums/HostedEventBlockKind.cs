namespace Ben.Data.Common.Enums;

/// <summary>
/// Why a room or a seat is not on offer (item 235 phase 4).
/// </summary>
/// <remarks>
/// <para>Two reasons rather than one, because a guest's plan should say different things about
/// them. A blocked seat is one nobody may have — behind a pillar, out of order, kept for the crew.
/// A house-held room is one the venue has taken for itself, and telling a guest it is "unavailable"
/// when the owner's family is in it is the kind of small untruth that turns into a phone call.</para>
///
/// <para>The venue's own note is never shown either way. What reaches a guest is the difference
/// between "not on offer" and "the venue is using this one".</para>
///
/// <para><b>Append only.</b> The numbers are stored.</para>
/// </remarks>
public enum HostedEventBlockKind
{
    /// <summary>Not on offer to anybody. Out of order, behind a pillar, kept for the crew.</summary>
    Blocked = 0,

    /// <summary>The venue is using it themselves.</summary>
    HouseHeld = 1,
}
