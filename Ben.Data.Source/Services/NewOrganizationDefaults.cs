using Ben.Data.Source.Context;

namespace Ben.Data.Source.Services;

/// <summary>
/// Everything a brand-new organization must be given before it is usable.
/// </summary>
/// <remarks>
/// <para><b>One door, because there is more than one caller and the list keeps growing.</b> The
/// admin creation endpoint and self-registration each carried their own copy of the same four
/// calls, in the same order, and a third caller (the personal organization behind a solo plan)
/// would have made three. Every addition to the list — roles, then member levels, then duties,
/// then event types — has had to be remembered at every copy, and the failure mode is silent: a
/// group created through the door somebody forgot simply has no roles, and nobody discovers it
/// until a permission check refuses somebody who should have passed.</para>
///
/// <para><b>Adds, does not save.</b> The caller owns the transaction, because creating an
/// organization is never only creating an organization: there is a membership to write, an audit
/// entry to raise, and in the solo case a subscription to attach. Saving here would commit half a
/// creation and leave the rest to a second round trip that can fail on its own.</para>
///
/// <para>Deliberately not conditional on the organization's kind or on whether it is personal. A
/// hidden one-person organization still needs roles and duties — it is a real organization that
/// happens to have one member, and giving it a reduced skeleton would mean every feature it
/// touches needs a second code path for the reduced case.</para>
/// </remarks>
public static class NewOrganizationDefaults
{
    /// <summary>
    /// Queues the default roles, member levels, duties and event types for a new organization.
    /// </summary>
    /// <param name="db">The context the organization itself was added to.</param>
    /// <param name="organizationId">The new organization.</param>
    /// <param name="createdByAppUserId">Who is creating it; recorded on every seeded row.</param>
    public static void AddAll(BenDataContext db, Guid organizationId, Guid createdByAppUserId)
    {
        OrgCalendarDefaults.AddDefaultEventTypes(db, organizationId, createdByAppUserId);
        OrgMemberLevelDefaults.AddDefaultLevels(db, organizationId, createdByAppUserId);
        OrgInvestigationDutyDefaults.AddDefaultDuties(db, organizationId, createdByAppUserId);
        OrgRoleDefaults.AddDefaultRoles(db, organizationId, createdByAppUserId);
    }
}
