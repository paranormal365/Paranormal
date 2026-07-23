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
/// A parallel enum exists at <c>Ben.Service.Security.Enums.OrganizationSecurityTable</c>
/// with different integer values; a cast mapping is applied inside
/// <c>OrganizationSecurityService</c>.
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
}