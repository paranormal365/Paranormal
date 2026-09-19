using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;

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
    /// <returns>
    /// The role the group should start new members on, or null when the defaults did not stage one.
    /// Callers holding the <see cref="Entities.Organization"/> itself should prefer the overload
    /// that takes it, which sets the setting for them.
    /// </returns>
    public static Entities.OrganizationRole? AddAll(
        BenDataContext db, Guid organizationId, Guid createdByAppUserId)
    {
        OrgCalendarDefaults.AddDefaultEventTypes(db, organizationId, createdByAppUserId);

        // The ladder and the duties are seeded together because the third thing — which title may
        // hold which duty (item 160) — is about both. A group that starts with an empty matrix is
        // a group whose matrix nobody ever fills in.
        var levels = OrgMemberLevelDefaults.AddDefaultLevels(db, organizationId, createdByAppUserId);
        var duties = OrgInvestigationDutyDefaults.AddDefaultDuties(db, organizationId, createdByAppUserId);
        OrgInvestigationDutyDefaults.AddDefaultEligibility(db, duties, levels, createdByAppUserId);

        var roles = OrgRoleDefaults.AddDefaultRoles(db, organizationId, createdByAppUserId);

        // W-M1: rank alone opens nothing below Administrator, so a group that starts nobody on a
        // functional role starts them unable to read the cases their own desk lists. New groups
        // therefore begin with a default; an owner can clear it or change it on the settings page.
        return roles.FirstOrDefault(r =>
            string.Equals(r.Name, OrgRoleDefaults.DefaultMemberRoleName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The same, plus the group's starting-member role — saved in two steps, deliberately.
    /// </summary>
    /// <remarks>
    /// <para><b>Why it saves twice.</b> An organization points at one of its own roles
    /// (<see cref="Entities.Organization.DefaultMemberRoleId"/>) and every role points back at its
    /// organization. Inserting both in one <c>SaveChanges</c> is a cycle EF cannot order, and it
    /// refuses the whole batch — which is how registering a group started failing the moment the
    /// pointer was set before the first save. So the group and its roles go in first, and the
    /// pointer follows.</para>
    ///
    /// <para>The two saves are not a transaction boundary the caller can lose: if the second one
    /// fails, the group exists with its roles and simply starts nobody on one, which is the same
    /// state every group had before this setting existed.</para>
    /// </remarks>
    public static async Task AddAllAsync(
        BenDataContext db, Entities.Organization organization, Guid createdByAppUserId,
        CancellationToken token = default)
    {
        var starting = AddAll(db, organization.Id, createdByAppUserId);
        await db.SaveChangesAsync(token);

        if (starting is null) return;
        organization.DefaultMemberRoleId = starting.Id;
        await db.SaveChangesAsync(token);
    }
}
