namespace Ben.Web.Website.Library.Kit.Plans;

/// <summary>What a plan is being used for, which decides what a square does when it is touched.</summary>
public enum PlanMode
{
    /// <summary>The venue arranging its own plan. Squares select; empty squares are drop targets.</summary>
    Edit = 0,

    /// <summary>Nobody is choosing anything — the board, or a public page showing how full it is.</summary>
    Read = 1,

    /// <summary>A guest choosing what to hold. Only free squares answer.</summary>
    Pick = 2,
}

/// <summary>
/// What one square is on one night.
/// </summary>
/// <remarks>
/// <para>Every state is drawn with a colour AND a glyph AND words in its label, never colour
/// alone. About one man in twelve cannot separate the red from the green, and a seating plan that
/// tells him nothing is a seating plan that sells him somebody else's seat.</para>
/// </remarks>
public enum PlanCellState
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

/// <summary>How a state is drawn and said, in one place so every screen agrees.</summary>
/// <remarks>
/// Named here rather than in the markup because the board, the public page and the guest's picker
/// all draw the same squares, and three copies of "which glyph means taken" is three chances to
/// disagree in front of somebody standing at a door.
/// </remarks>
public static class PlanCellStates
{
    /// <summary>The sprite symbol drawn inside the square.</summary>
    public static string Glyph(PlanCellState state) => state switch
    {
        PlanCellState.Mine       => "check",
        PlanCellState.Pending    => "clock",
        PlanCellState.Taken      => "x",
        PlanCellState.Blocked    => "slash",
        PlanCellState.NotOffered => "minus",
        _                        => "",
    };

    /// <summary>What a screen reader says, and what the legend prints.</summary>
    public static string Word(PlanCellState state) => state switch
    {
        PlanCellState.Mine       => "yours",
        PlanCellState.Pending    => "held by somebody else",
        PlanCellState.Taken      => "taken",
        PlanCellState.Blocked    => "kept back",
        PlanCellState.NotOffered => "not on offer",
        _                        => "free",
    };

    /// <summary>Whether a guest may choose it.</summary>
    public static bool IsPickable(PlanCellState state)
        => state is PlanCellState.Free or PlanCellState.Mine;
}
