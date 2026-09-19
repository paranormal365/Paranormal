namespace Ben.Data.Common.Enums;

/// <summary>
/// Whether the group behind a case-derived feed post wants its name on it (item 186 F7).
/// </summary>
/// <remarks>
/// <para><b>The default is Unclaimed, and Unclaimed shows nothing.</b> A post derived from a
/// group's case does not link back to the group until somebody with standing there says so —
/// the safe default for a group that has not looked yet. Declining leaves the post up, credited
/// to the person, with no group link: the group's name is theirs, not the poster's.</para>
///
/// <para>Append-only, like every enum here: the numbers are in the database.</para>
/// </remarks>
public enum OrgAttributionState
{
    /// <summary>Nobody at the group has decided. Renders exactly like Declined: no link.</summary>
    Unclaimed = 0,

    /// <summary>The group claims it: name + link render, and the post wears "Group verified".</summary>
    Claimed = 1,

    /// <summary>The group said no. The post stays, the person's credit stays, no group link.</summary>
    Declined = 2,
}
