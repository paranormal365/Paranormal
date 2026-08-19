namespace Ben.Data.Common.Enums;

/// <summary>
/// Identifies a logical database table for which per-user access grants can be
/// configured within an organization via <c>OrganizationAccessGrant</c>.
/// </summary>
/// <remarks>
/// Each value corresponds to a table in the BenDb database.  When an active
/// <c>OrganizationAccessGrant</c> row exists for a user/table/<see cref="OrganizationSecurityAction"/>
/// combination, that operation is permitted for the user within that organization.
/// <para>
/// This is now the only such enum. A parallel copy used to live in the <c>Ben.Service.Security</c>
/// project, converted to this one by a plain numeric cast — which meant the <i>numbers</i>, not the
/// names, decided which table a permission check actually read. They drifted: twenty-six of thirty
/// values resolved to the wrong table (<c>OrganizationFiles</c> landed on <c>MembershipRequests</c>)
/// and nothing said so, because the attribute driving that path was applied to nothing. Both the
/// duplicate enum and its parity test were removed with that project, so the hazard is gone at the
/// root rather than guarded against.
/// </para>
/// </remarks>
public enum OrganizationSecurityTable
{
    Organization = 1,
    OrganizationAddress = 2,
    OrganizationAddressType = 3,
    OrganizationEmail = 4,
    OrganizationEmailType = 5,
    OrganizationLink = 6,
    OrganizationLinkType = 7,
    OrganizationNote = 8,
    OrganizationNoteType = 9,
    OrganizationPage = 10,
    OrganizationPhone = 11,
    OrganizationPhoneType = 12,
    AppUser = 13,
    UserAddress = 14,
    UserAddressType = 15,
    UserEmail = 16,
    UserEmailType = 17,
    UserLink = 18,
    UserLinkType = 19,
    UserMessage = 20,
    UserMessageTo = 21,
    UserMessageType = 22,
    UserNote = 23,
    UserNoteType = 24,
    UserPhone = 25,
    UserPhoneType = 26,
    CmsSection = 27,
    OrgMemberGroup = 28,
    /// <summary>Grants access to review and respond to user membership applications.</summary>
    MembershipRequests = 29,
    /// <summary>Grants access to manage organization-owned files.</summary>
    OrganizationFiles = 30,
    /// <summary>Grants access to manage specific-member access lists on org addresses (SpecificMembers visibility).</summary>
    OrganizationAddressMemberAccess = 31,
    /// <summary>Grants access to configure address searchability and proximity search settings.</summary>
    OrganizationAddressSearch = 32,
    /// <summary>Grants access to manage org-level settings (ShowAddressMap, ShowAddressDirections, etc.).</summary>
    OrganizationSettings = 33,

    /// <summary>
    /// Grants access to schedule and edit the organization's investigations.
    /// </summary>
    /// <remarks>
    /// One of several ways to earn the right to edit an investigation — see
    /// <c>InvestigationAccess.CanManageAsync</c>, which also recognises the creator, the case
    /// manager, the visit's own lead, and org owners and administrators. This value is the
    /// delegable one: it is what an organization grants to someone whose job is scheduling.
    /// </remarks>
    Investigation = 34,

    /// <summary>
    /// Grants access to manage the organization's own equipment — adding and editing gear, its
    /// photos, its service and defect log, and who is currently holding a piece.
    /// </summary>
    /// <remarks>
    /// Covers the group's <i>property</i> only. Gear that members have shared with the group from
    /// their own lists is readable by any active member without this permission, because the
    /// sharing is the owner's own decision and not the group's to gate. Reading the group's own
    /// equipment is likewise open to members; this value governs changing it, and seeing serial
    /// numbers.
    /// </remarks>
    Equipment = 35,

    /// <summary>
    /// Grants access to review equipment checkouts for the organization — approving or denying
    /// requests, handing gear over, and receiving it back. The delegable "Equipment Management"
    /// right item #55 asks for.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Equipment"/> so a group can hand someone the loans desk without
    /// also handing them the catalog: running checkouts and deciding what the group owns are
    /// different jobs. Rendered as a sub-permission of <see cref="Equipment"/> in the role editor.
    /// Applies to the group's own gear; a loan of a member's personal item is always approved by
    /// its owner, never by this permission.
    /// </remarks>
    EquipmentCheckout = 36,
}