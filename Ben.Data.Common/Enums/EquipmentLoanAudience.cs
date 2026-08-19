namespace Ben.Data.Common.Enums;

/// <summary>
/// Who the owner is willing to lend a piece of equipment to. Combinable — any mix of the three
/// routes below.
/// </summary>
/// <remarks>
/// <para>Deliberately separate from who can <i>see</i> the item. Sharing gear with a group so they
/// know it exists is not the same as offering it: an item can be listed publicly and never be
/// borrowable, or be lendable to a trusted group while staying off the public catalog entirely.</para>
///
/// <para><c>[Flags]</c> rather than a widening scale, because the routes differ along two axes at
/// once, not one:</para>
/// <list type="bullet">
/// <item><description><b>Attribution</b> — <see cref="SharedGroups"/> is a loan taken out <i>for</i> a
/// group, and records which one, typically against an investigation. The other two are personal
/// loans with no borrowing group, which is why a checkout's organization is nullable.</description></item>
/// <item><description><b>Reach</b> — <see cref="GroupMembers"/> is limited to people the owner
/// actually shares a group with; <see cref="IndividualUsers"/> is anyone with an account.</description></item>
/// </list>
///
/// <para>So "lend to my groups, and to people in them, but not to strangers" is
/// <c>SharedGroups | GroupMembers</c>, and each combination means something an owner would
/// genuinely want.</para>
///
/// <para>Defaults to <see cref="NotLoanable"/>: lending is the higher-consequence choice, so it is
/// opted into rather than out of. Follows the same per-domain-flags shape as
/// <see cref="FilePermissionType"/> and <see cref="CmsPageAction"/>.</para>
/// </remarks>
[Flags]
public enum EquipmentLoanAudience
{
    /// <summary>Not available to borrow. The item may still be visible to groups or publicly.</summary>
    NotLoanable = 0,

    /// <summary>
    /// A group this item is shared with may borrow it for the group's own use. The loan records
    /// which group it was borrowed for, and can be tied to a specific investigation.
    /// </summary>
    SharedGroups = 1,

    /// <summary>
    /// Someone the owner shares a group with may borrow it personally — a fellow member, borrowing
    /// as themselves rather than on the group's behalf. No borrowing group is recorded.
    /// </summary>
    GroupMembers = 2,

    /// <summary>
    /// Any signed-in user may request it personally, and the owner approves or denies. The widest
    /// reach; no borrowing group is recorded.
    /// </summary>
    IndividualUsers = 4,
}
