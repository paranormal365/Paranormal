namespace Ben.Data.Common.Enums;

/// <summary>
/// What holding a duty lets somebody do <b>on that one visit</b> (item 160).
/// </summary>
/// <remarks>
/// <para><b>Duties still grant nothing standing.</b> That principle, set when duties shipped in
/// item 158, is not weakened here. A capability applies to the single investigation the duty was
/// assigned on and disappears with the assignment — the same shape as the visit lead's manage
/// right, which is delegated authority that expires with the visit rather than a rank.</para>
///
/// <para><b>Only capabilities with a door are listed.</b> Ben's worked example also describes
/// inviting members to a visit and rescheduling one; neither has a control anywhere in the
/// product yet, and a switch that reads well on a settings page and changes nothing is the
/// write-only feature this codebase keeps having to go back and finish. They go in when their
/// doors do — see item 160's note.</para>
/// </remarks>
[Flags]
public enum InvestigationDutyCapabilities
{
    /// <summary>Holding this duty confers nothing beyond the duty itself.</summary>
    None = 0,

    /// <summary>
    /// The person to call about this visit. Shown on the roster and the duty board so a group,
    /// and the client, can see who is answerable on the night.
    /// </summary>
    PointOfContact = 1,

    /// <summary>
    /// May hand out and take back the other duties on this visit, without being able to change
    /// anything else about it.
    /// </summary>
    MayAssignDuties = 2,
}
