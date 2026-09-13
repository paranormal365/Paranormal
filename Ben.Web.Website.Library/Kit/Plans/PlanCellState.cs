using Ben.Data.Common.Enums;

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

/// <summary>How a state is drawn and said, in one place so every screen agrees.</summary>
/// <remarks>
/// <para>Named here rather than in the markup because the board, the public page and the guest's
/// picker all draw the same squares, and three copies of "which glyph means taken" is three
/// chances to disagree in front of somebody standing at a door.</para>
///
/// <para>The states themselves are <see cref="HostedEventPlanCellState"/>, which moved to
/// <c>Ben.Data.Common</c> in phase 6 when the server started answering what a square is for the
/// guest's picker. How one is DRAWN is the website's business and stays here.</para>
/// </remarks>
public static class PlanCellStates
{
    /// <summary>The sprite symbol drawn inside the square.</summary>
    public static string Glyph(HostedEventPlanCellState state) => state switch
    {
        HostedEventPlanCellState.Mine       => "check",
        HostedEventPlanCellState.Pending    => "clock",
        HostedEventPlanCellState.Taken      => "x",
        HostedEventPlanCellState.Blocked    => "slash",
        HostedEventPlanCellState.NotOffered => "minus",
        _                        => "",
    };

    /// <summary>What a screen reader says, and what the legend prints.</summary>
    public static string Word(HostedEventPlanCellState state) => state switch
    {
        HostedEventPlanCellState.Mine       => "yours",
        HostedEventPlanCellState.Pending    => "held by somebody else",
        HostedEventPlanCellState.Taken      => "taken",
        HostedEventPlanCellState.Blocked    => "kept back",
        HostedEventPlanCellState.NotOffered => "not on offer",
        _                        => "free",
    };

    /// <summary>Whether a guest may choose it.</summary>
    public static bool IsPickable(HostedEventPlanCellState state)
        => state is HostedEventPlanCellState.Free or HostedEventPlanCellState.Mine;
}
