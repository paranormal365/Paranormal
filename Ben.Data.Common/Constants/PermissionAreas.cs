using Ben.Data.Common.Enums;

namespace Ben.Data.Common.Constants;

/// <summary>
/// The total map from organization-scoped permission tables to their tier areas (item 156).
/// </summary>
/// <remarks>
/// <para><b>Total, and guarded.</b> Every org-scoped <see cref="OrganizationSecurityTable"/>
/// value appears exactly once; a value missing here would be a permission no tier could ever
/// include, which is invisible rather than broken — the same failure class the role editor's
/// coverage guard exists for. <c>PermissionAreaMapGuardTests</c> keeps this list and the enum in
/// lockstep.</para>
///
/// <para><b>The exclusions are declared, not implied.</b> User-scoped values (13–26) belong to
/// no organization and no area; <c>AppUser</c>=13 additionally is referenced nowhere in the
/// codebase (recorded under item 83). They live in <see cref="UserScopedTables"/> so the guard
/// can assert that everything is either mapped or deliberately excluded — nothing in between.</para>
/// </remarks>
public static class PermissionAreas
{
    /// <summary>Values that are user-scoped, not organization-scoped: no area, by design.</summary>
    public static readonly IReadOnlySet<OrganizationSecurityTable> UserScopedTables =
        new HashSet<OrganizationSecurityTable>
        {
            OrganizationSecurityTable.AppUser,
            OrganizationSecurityTable.UserAddress,
            OrganizationSecurityTable.UserAddressType,
            OrganizationSecurityTable.UserEmail,
            OrganizationSecurityTable.UserEmailType,
            OrganizationSecurityTable.UserLink,
            OrganizationSecurityTable.UserLinkType,
            OrganizationSecurityTable.UserMessage,
            OrganizationSecurityTable.UserMessageTo,
            OrganizationSecurityTable.UserMessageType,
            OrganizationSecurityTable.UserNote,
            OrganizationSecurityTable.UserNoteType,
            OrganizationSecurityTable.UserPhone,
            OrganizationSecurityTable.UserPhoneType,
        };

    private static readonly IReadOnlyDictionary<OrganizationSecurityTable, OrganizationPermissionArea> _map =
        new Dictionary<OrganizationSecurityTable, OrganizationPermissionArea>
        {
            // ── Organization profile ─────────────────────────────────────────
            [OrganizationSecurityTable.Organization]                    = OrganizationPermissionArea.OrganizationProfile,
            [OrganizationSecurityTable.OrganizationSettings]            = OrganizationPermissionArea.OrganizationProfile,
            [OrganizationSecurityTable.OrganizationAddress]             = OrganizationPermissionArea.OrganizationProfile,
            [OrganizationSecurityTable.OrganizationAddressType]         = OrganizationPermissionArea.OrganizationProfile,
            [OrganizationSecurityTable.OrganizationAddressMemberAccess] = OrganizationPermissionArea.OrganizationProfile,
            [OrganizationSecurityTable.OrganizationAddressSearch]       = OrganizationPermissionArea.OrganizationProfile,
            [OrganizationSecurityTable.OrganizationEmail]               = OrganizationPermissionArea.OrganizationProfile,
            [OrganizationSecurityTable.OrganizationEmailType]           = OrganizationPermissionArea.OrganizationProfile,
            [OrganizationSecurityTable.OrganizationPhone]               = OrganizationPermissionArea.OrganizationProfile,
            [OrganizationSecurityTable.OrganizationPhoneType]           = OrganizationPermissionArea.OrganizationProfile,
            [OrganizationSecurityTable.OrganizationLink]                = OrganizationPermissionArea.OrganizationProfile,
            [OrganizationSecurityTable.OrganizationLinkType]            = OrganizationPermissionArea.OrganizationProfile,
            [OrganizationSecurityTable.OrganizationNote]                = OrganizationPermissionArea.OrganizationProfile,
            [OrganizationSecurityTable.OrganizationNoteType]            = OrganizationPermissionArea.OrganizationProfile,
            [OrganizationSecurityTable.OrgMemberGroup]                  = OrganizationPermissionArea.OrganizationProfile,

            // ── Membership ───────────────────────────────────────────────────
            [OrganizationSecurityTable.MembershipRequests]              = OrganizationPermissionArea.Membership,

            // ── Cases / Investigations / Clients / Calendar ──────────────────
            [OrganizationSecurityTable.Case]                            = OrganizationPermissionArea.Cases,
            [OrganizationSecurityTable.Investigation]                   = OrganizationPermissionArea.Investigations,
            [OrganizationSecurityTable.ClientRequest]                   = OrganizationPermissionArea.Clients,
            [OrganizationSecurityTable.OrgCalendar]                     = OrganizationPermissionArea.Calendar,

            // ── Equipment / Public pages / Files ─────────────────────────────
            [OrganizationSecurityTable.Equipment]                       = OrganizationPermissionArea.Equipment,
            [OrganizationSecurityTable.EquipmentCheckout]               = OrganizationPermissionArea.Equipment,
            [OrganizationSecurityTable.OrganizationPage]                = OrganizationPermissionArea.PublicPages,
            [OrganizationSecurityTable.CmsSection]                      = OrganizationPermissionArea.PublicPages,
            [OrganizationSecurityTable.OrganizationFiles]               = OrganizationPermissionArea.Files,
        };

    /// <summary>The area a table belongs to, or null for user-scoped values.</summary>
    public static OrganizationPermissionArea? AreaFor(OrganizationSecurityTable table)
        => _map.TryGetValue(table, out var area) ? area : null;

    /// <summary>Every mapped table, for guards and grouping.</summary>
    public static IReadOnlyDictionary<OrganizationSecurityTable, OrganizationPermissionArea> Map => _map;
}
