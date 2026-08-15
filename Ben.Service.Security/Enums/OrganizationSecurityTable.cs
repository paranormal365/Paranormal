namespace Ben.Service.Security.Enums;

/// <summary>
/// Represents the organization-related database tables that have security controls.
/// </summary>
/// <remarks>
/// <para><b>Every value here must equal the value of the same-named member in
/// <c>Ben.Data.Common.Enums.OrganizationSecurityTable</c>.</b> <c>OrganizationSecurityService</c>
/// converts between the two with a plain numeric cast — <c>(DataCommonTable)table</c> — so the
/// numbers, not the names, are what actually decide which table a permission check reads.</para>
///
/// <para>They did not match. Of the thirty values this enum had, twenty-six resolved to a
/// different table than their name: <c>OrganizationFiles</c> (29) landed on
/// <c>MembershipRequests</c>, <c>CmsSection</c> (26) on <c>UserPhoneType</c>, and so on down the
/// list — every value from 3 upward was off by one or more places. A grant to review membership
/// applications would have satisfied a check for managing organization files.</para>
///
/// <para>It never fired, because <c>OrganizationSecurityAuthorizeAttribute</c> is registered in DI
/// and applied to no controller or action; nothing in the running app reaches the cast. The values
/// below are now aligned so that it is correct if anything ever does, and
/// <c>OrganizationSecurityTableParityTests</c> fails the build if the two drift again.</para>
///
/// <para>Renumbering was safe precisely because the mapping was unused: the persisted column
/// (<c>OrganizationAccessGrant.TableName</c>) is the <c>Ben.Data.Common</c> type, so no stored row
/// carries a number from this enum.</para>
/// </remarks>
public enum OrganizationSecurityTable
{
    /// <summary>No table. Never matches a grant — <c>Ben.Data.Common</c> has no zero value.</summary>
    None = 0,

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

    /// <summary>
    /// Named <c>AppUser</c> in <c>Ben.Data.Common</c>. Kept as <c>User</c> here for source
    /// compatibility; the parity test knows about this one alias and checks the number matches.
    /// </summary>
    User = 13,

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

    /// <summary>Grants access to manage specific-member access lists on org addresses.</summary>
    OrganizationAddressMemberAccess = 31,

    /// <summary>Grants access to configure address searchability and proximity search settings.</summary>
    OrganizationAddressSearch = 32,

    /// <summary>Grants access to manage org-level settings.</summary>
    OrganizationSettings = 33,

    /// <summary>Grants access to schedule and edit the organization's investigations.</summary>
    Investigation = 34,
}
