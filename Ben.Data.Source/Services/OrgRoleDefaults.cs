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
    /// The seven starting roles: name, description, and (table, actions) grants.
    /// </summary>
    public static readonly IReadOnlyList<(string Name, string Description,
        IReadOnlyList<(OrganizationSecurityTable Table, OrganizationSecurityAction Actions)> Grants)> Defaults =
    [
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
            [.. PermissionAreas.Map.Keys.Select(t => (t, OrganizationSecurityAction.Read))]),

        ("Secretary Role", "Keeps the group running: the calendar, membership applications, and the group's own details.",
            [(OrganizationSecurityTable.OrgCalendar, Crud),
             (OrganizationSecurityTable.MembershipRequests, ReadUpdate),
             (OrganizationSecurityTable.Organization, ReadUpdate)]),
    ];

    /// <summary>Stages the seven roles with their grants; the caller's SaveChangesAsync commits them.</summary>
    public static void AddDefaultRoles(BenDataContext db, Guid organizationId, Guid createdByAppUserId)
    {
        var now = DateTime.UtcNow;
        var sort = 0;
        foreach (var (name, description, grants) in Defaults)
        {
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
