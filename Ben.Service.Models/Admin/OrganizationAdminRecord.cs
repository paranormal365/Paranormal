namespace Ben.Service.Models.Admin;

public record OrganizationAdminRecord
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public required string UrlName { get; init; }
    /// <summary>When true, registered users can submit membership applications.</summary>
    public bool IsAcceptingApplications { get; init; }

    /// <summary>
    /// True when this group has chosen not to appear in search, browse or nearby results.
    /// </summary>
    /// <remarks>
    /// Carried so the settings form can round-trip it. A form that loaded only the fields it was
    /// written for silently reset the ones it did not know about — the reason every optional flag
    /// on the update request means "leave as-is" rather than false.
    /// </remarks>
    public bool IsUnlisted { get; init; }
    /// <summary>When true, the public can submit investigation requests.</summary>
    public bool IsAcceptingClients { get; init; }
    /// <summary>When true, the org considers requests from outside their operating area.</summary>
    public bool AcceptsClientsOutsideRange { get; init; }
    /// <summary>
    /// When true, members who have personally opted in may have their private photo shown to this
    /// org's clients. Both keys are required — see PrivatePhotoConsent.
    /// </summary>
    public bool AllowMemberPrivatePhotosToClients { get; init; }
    /// <summary>What this group primarily is (2026-08-24). Decides the defaults a NEW group
    /// starts with; afterwards it is a label, never a gate.</summary>
    public Ben.Data.Common.Enums.OrganizationKind Kind { get; init; }
    /// <summary>It runs public walking tours, whatever kind it primarily is.</summary>
    public bool RunsPublicTours { get; init; }
    public string? PublicPhone { get; init; }
    public string? PublicEmail { get; init; }
    public string? PublicWebsite { get; init; }
    public DateTime DateCreated { get; init; }
    public DateTime? DateUpdated { get; init; }
    public Guid CreatedByAppUserId { get; init; }
    public Guid? UpdatedByAppUserId { get; init; }
}
