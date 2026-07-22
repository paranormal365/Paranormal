namespace Ben.Service.Security.Enums;

/// <summary>
/// Represents the organization-related database tables that have security controls.
/// </summary>
public enum OrganizationSecurityTable
{
    None = 0,
    Organization = 1,
    OrganizationAddress = 2,
    OrganizationEmail = 3,
    OrganizationPhone = 4,
    OrganizationLink = 5,
    OrganizationNote = 6,
    OrganizationPage = 7,
    OrganizationAddressType = 8,
    OrganizationEmailType = 9,
    OrganizationPhoneType = 10,
    OrganizationLinkType = 11,
    OrganizationNoteType = 12,
    User = 13,
    UserAddress = 14,
    UserEmail = 15,
    UserPhone = 16,
    UserLink = 17,
    UserNote = 18,
    UserMessage = 19,
    UserAddressType = 20,
    UserEmailType = 21,
    UserPhoneType = 22,
    UserLinkType = 23,
    UserNoteType = 24,
    UserMessageType = 25,
    CmsSection = 26,
    OrgMemberGroup = 27,
    /// <summary>Grants access to review and respond to user membership applications.</summary>
    MembershipRequests = 28,
    /// <summary>Grants access to manage organization-owned files.</summary>
    OrganizationFiles = 29,
}
