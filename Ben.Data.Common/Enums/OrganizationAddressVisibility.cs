namespace Ben.Data.Common.Enums;

/// <summary>Controls which audience can access an OrganizationAddress.</summary>
public enum OrganizationAddressVisibility
{
    /// <summary>Visible to everyone including anonymous visitors.</summary>
    Public = 0,
    /// <summary>Visible to active org members only.</summary>
    MembersOnly = 1,
    /// <summary>Visible to a specific named list of members (OrganizationAddressMemberAccess).</summary>
    SpecificMembers = 2,
    /// <summary>Visible to org Owner and Administrators only.</summary>
    Private = 3
}
