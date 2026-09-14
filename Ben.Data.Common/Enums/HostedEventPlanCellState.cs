namespace Ben.Data.Common.Enums;

/// <summary>
/// What one square of a plan is, on one night (item 235).
/// </summary>
/// <remarks>
/// <para><b>Here rather than in the website's component library</b>, because from phase 6 the
/// server answers this question too: a guest's picker is drawn from what the API says each square
/// is, and the organizer's board draws the same squares from its own read. Two enumerations with
/// the same six words in two assemblies is two chances to disagree about what "held" means, in
/// front of somebody trying to buy a seat.</para>
///
/// <para><b>Every state is drawn with a colour AND a glyph AND words in its label</b>, never
/// colour alone. About one man in twelve cannot separate the red from the green, and a seating
/// plan that tells him nothing is a seating plan that sells him somebody else's seat. The drawing
/// itself lives with the component; this is only the vocabulary.</para>
/// </remarks>
public enum HostedEventPlanCellState
{
    /// <summary>Nobody has it. The only state a guest may pick.</summary>
    Free = 0,

    /// <summary>This reader holds it, or has just chosen it.</summary>
    Mine = 1,

    /// <summary>Somebody else is holding it while the venue decides. Not pickable, and not sold.</summary>
    Pending = 2,

    /// <summary>The venue has confirmed somebody into it.</summary>
    Taken = 3,

    /// <summary>The venue has kept it back — a sound desk, a sightline, a room for staff.</summary>
    Blocked = 4,

    /// <summary>On the plan, but not on offer for this night.</summary>
    NotOffered = 5,
}
