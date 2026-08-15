namespace Ben.Service.Models.Admin;

public record OrganizationAdminRecord
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public required string UrlName { get; init; }
    /// <summary>When true, registered users can submit membership applications.</summary>
    public bool IsAcceptingApplications { get; init; }
    /// <summary>When true, the public can submit investigation requests.</summary>
    public bool IsAcceptingClients { get; init; }
    /// <summary>When true, the org considers requests from outside their operating area.</summary>
    public bool AcceptsClientsOutsideRange { get; init; }
    /// <summary>
    /// When true, members who have personally opted in may have their private photo shown to this
    /// org's clients. Both keys are required — see PrivatePhotoConsent.
    /// </summary>
    public bool AllowMemberPrivatePhotosToClients { get; init; }
    public string? PublicPhone { get; init; }
    public string? PublicEmail { get; init; }
    public string? PublicWebsite { get; init; }
    public DateTime DateCreated { get; init; }
    public DateTime? DateUpdated { get; init; }
    public Guid CreatedByAppUserId { get; init; }
    public Guid? UpdatedByAppUserId { get; init; }
}
