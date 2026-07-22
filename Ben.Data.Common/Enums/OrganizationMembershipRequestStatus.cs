namespace Ben.Data.Common.Enums;

/// <summary>
/// Represents the lifecycle state of an <see cref="OrganizationMembershipRequest"/>.
/// </summary>
public enum OrganizationMembershipRequestStatus
{
    /// <summary>The application has been submitted and is awaiting a response.</summary>
    Pending = 0,

    /// <summary>The application was accepted; the user has been added to the organization.</summary>
    Accepted = 1,

    /// <summary>The application was denied; the user remains outside the organization.</summary>
    Denied = 2,

    /// <summary>The applicant withdrew their own request before a response was given.</summary>
    Withdrawn = 3,
}
