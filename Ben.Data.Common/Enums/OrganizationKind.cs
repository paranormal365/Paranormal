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
}
