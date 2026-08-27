using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;

namespace Ben.Data.Source.Services;

/// <summary>
/// The permission roles every new organization starts with (item 156 Phase C).
/// </summary>
/// <remarks>
/// <para>Same pattern as <see cref="OrgCalendarDefaults"/> / <see cref="OrgMemberLevelDefaults"/> /
/// <see cref="OrgInvestigationDutyDefaults"/>: per-org, stamped at every creation door on the
/// same SaveChanges, fully the owner's to edit or delete afterwards. Every name carries the
/// <b>"… Role" suffix</b> (Ben, 2026-08-23): the member-title ladder legitimately uses
/// overlapping words, and "Case Manager" the title-adjacent word must never be confusable with
/// "Case Manager Role" the permission set.</para>
///
/// <para>Grants are ADDITIVE starting points (decision D5) — a group that wants a narrower or
/// wider Case Manager Role edits it like any other role. Some grants do real work today
/// (Phase B's doors); the rest become decisive as later phases land.</para>
/// </remarks>
public static class OrgRoleDefaults
{
    private const OrganizationSecurityAction Crud =
        OrganizationSecurityAction.Create | OrganizationSecurityAction.Read
        | OrganizationSecurityAction.Update | OrganizationSecurityAction.Delete;

    private const OrganizationSecurityAction ReadUpdate =
        OrganizationSecurityAction.Read | OrganizationSecurityAction.Update;

    /// <summary>
    /// The eight starting roles: name, description, and (table, actions) grants.
    /// </summary>
    public static readonly IReadOnlyList<(string Name, string Description,
        IReadOnlyList<(OrganizationSecurityTable Table, OrganizationSecurityAction Actions)> Grants)> Defaults =
    [
        // The ordinary member's role, and the one every real group needs first. It was missing
        // from the original seven because the grandfathering seeder used to hand an equivalent to
        // every member automatically; when Ben ended that (IH-03 step 4, 2026-08-26) the role
        // became something an owner assigns deliberately — and a fresh group had nothing to
        // assign. The revoke migration kept the old role where it existed; this provides it where
        // it never did. The description deliberately differs from the revoked seeder's exact
        // sentence, which that migration matches on.
        ("Investigator Role", "Reads the group's cases and investigations. Assign it to the members who should see them.",
            [(OrganizationSecurityTable.Case, OrganizationSecurityAction.Read),
             (OrganizationSecurityTable.Investigation, OrganizationSecurityAction.Read)]),

        ("Case Manager Role", "Runs cases end to end: the case record, and the investigations under it.",
            [(OrganizationSecurityTable.Case, Crud), (OrganizationSecurityTable.Investigation, Crud)]),

        ("Equipment Manager Role", "Owns the group's gear: the equipment list and its checkouts.",
            [(OrganizationSecurityTable.Equipment, Crud), (OrganizationSecurityTable.EquipmentCheckout, Crud)]),

        ("CMS Manager Role", "Builds and maintains the group's public pages.",
            [(OrganizationSecurityTable.OrganizationPage, Crud), (OrganizationSecurityTable.CmsSection, Crud)]),

        ("Client Manager Role", "Handles what clients send in — accepting, declining, corresponding — and can read the cases that result.",
            [(OrganizationSecurityTable.ClientRequest, Crud), (OrganizationSecurityTable.Case, OrganizationSecurityAction.Read)]),

        ("Content Manager Role", "Curates the group's files and keeps the public pages' content fresh.",
            [(OrganizationSecurityTable.OrganizationFiles, Crud), (OrganizationSecurityTable.CmsSection, ReadUpdate)]),

        ("Historian Role", "Reads everything, changes nothing: the group's memory.",
            // Mapped tables minus the ungated ones (item 170): a grant nothing consults would
            // sit in the role invisibly and be silently dropped on the editor's next save.
            [.. PermissionAreas.Map.Keys
                .Where(t => !PermissionAreas.UngatedTables.Contains(t))
                .Select(t => (t, OrganizationSecurityAction.Read))]),

        ("Secretary Role", "Keeps the group running: the calendar, membership applications, and the group's own details.",
            [(OrganizationSecurityTable.OrgCalendar, Crud),
             (OrganizationSecurityTable.MembershipRequests, ReadUpdate),
             (OrganizationSecurityTable.Organization, ReadUpdate)]),
    ];

    /// <summary>Stages the seven roles with their grants; the caller's SaveChangesAsync commits them.</summary>
    public static void AddDefaultRoles(BenDataContext db, Guid organizationId, Guid createdByAppUserId)
        => AddDefaultRoles(db, organizationId, createdByAppUserId, existingRoleNames: []);

    /// <summary>
    /// Adds the defaults this group does not already have, leaving the ones it does alone.
    /// </summary>
    /// <param name="existingRoleNames">
    /// The role names already on the group. Anything listed here is skipped.
    /// </param>
    /// <remarks>
    /// <para><b>For repairing a group that was created half-finished</b>, which the plain overload
    /// cannot do: it adds all eight unconditionally, so calling it on a group that has some would
    /// duplicate them.</para>
    ///
    /// <para><b>Deliberately NOT what the backfill seeder uses.</b> That one skips any group with
    /// ANY role, and should: a group that deleted a default meant it, and having it reappear at
    /// the next restart would be the site overruling them. This overload is for callers that OWN
    /// the group and know it was never finished — the development seeders repairing their own demo
    /// groups, where nobody has made any such decision (found 2026-08-27: a demo group created
    /// before the creation-time fix kept exactly one role forever, because the seeder would not
    /// recreate it and the backfill would not touch it).</para>
    /// </remarks>
    public static void AddDefaultRoles(
        BenDataContext db, Guid organizationId, Guid createdByAppUserId,
        IReadOnlyCollection<string> existingRoleNames)
    {
        var now = DateTime.UtcNow;
        var sort = 0;
        foreach (var (name, description, grants) in Defaults)
        {
            // Sort order still advances for a skipped role, so a repaired group orders its roles
            // the same way a freshly created one does.
            if (existingRoleNames.Contains(name, StringComparer.OrdinalIgnoreCase)) { sort++; continue; }

            var role = new OrganizationRole
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                Name = name,
                Description = description,
                IsActive = true,
                SortOrder = ++sort,
                DateCreated = now,
                CreatedByAppUserId = createdByAppUserId,
            };
            db.OrganizationRoles.Add(role);

            foreach (var (table, actions) in grants)
            {
                db.OrganizationRolePermissions.Add(new OrganizationRolePermission
                {
                    Id = Guid.NewGuid(),
                    OrganizationRoleId = role.Id,
                    TableName = table,
                    Actions = actions,
                    DateCreated = now,
                    CreatedByAppUserId = createdByAppUserId,
                });
            }
        }
    }
}
