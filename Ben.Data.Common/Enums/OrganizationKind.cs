namespace Ben.Data.Common.Enums;

/// <summary>
/// What an organization primarily IS — not what it investigates (item 78's deferred subject
/// taxonomy is a different axis), but what it does.
/// </summary>
/// <remarks>
/// <para><b>Why a kind at all.</b> An investigation group and a ghost walking tour want
/// opposite defaults. A group's headquarters is frequently somebody's home, so its address
/// starts hidden and unsearchable; a tour's meeting point is the entire product, and a tour
/// nobody can find is worthless. Rather than make every new tour operator hunt through
/// settings to undo privacy they never wanted, the kind chosen at creation sets the
/// defaults — and everything stays individually adjustable afterwards.</para>
///
/// <para><b>The kind is a starting point and a label, never a gate.</b> Nothing is withheld
/// from either kind: a tour company may take investigation requests, and an investigation
/// group may run public tours (see <c>Organization.RunsPublicTours</c>, which is how a group
/// says so and gets found under tours without changing what it primarily is).</para>
///
/// <para>Append-only, like every enum here: the numbers are in the database, and
/// <see cref="InvestigationGroup"/> is 0 because every organization that existed before this
/// was one.</para>
/// </remarks>
public enum OrganizationKind
{
    /// <summary>A paranormal investigation group — the default, and what every pre-existing
    /// organization is.</summary>
    InvestigationGroup = 0,

    /// <summary>A ghost walking tour operator: public tours on a schedule, public by nature.</summary>
    GhostWalkingTour = 1,

    /// <summary>
    /// Sells tickets to paranormal events at venues it does not own — a night in a decommissioned
    /// prison, a lock-in at a museum.
    /// </summary>
    /// <remarks>
    /// Closer to a tour than to an investigation group, and distinct from both: the product is a
    /// ticketed event on a date, the staff are guides rather than investigators, and the meeting
    /// point is the thing customers need most. It shared the tour's defaults before this existed,
    /// which cost nothing functionally — the kind is a label and a starting point, never a gate —
    /// and read wrong everywhere the kind is shown (Ben, 2026-08-27: "I like those org types to be
    /// available").
    /// </remarks>
    PublicEventProvider = 2,

    /// <summary>
    /// A property whose reported haunting IS the attraction: a hotel, an inn, a dormitory.
    /// </summary>
    /// <remarks>
    /// <para>The owner runs the place rather than visiting it, so the usual privacy default is
    /// backwards for them — the address is the product, and the reports are the marketing. Item
    /// 197 has the fuller shape (rooms, offerings, standing investigations), all of it still to
    /// design; this is the kind it will hang from, added now so a property owner is not made to
    /// call themselves an investigation group in the meantime.</para>
    /// </remarks>
    HauntedProperty = 3,
}
