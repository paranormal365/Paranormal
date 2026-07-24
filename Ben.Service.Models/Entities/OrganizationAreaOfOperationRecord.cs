namespace Ben.Service.Models.Entities;

/// <summary>
/// Full area of operation record — includes private coordinates.
/// Only returned to org admins and SuperAdmin, never to public endpoints.
/// </summary>
public record OrganizationAreaOfOperationRecord
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public decimal RadiusMiles { get; init; }

    /// <summary>Private — never expose on public search endpoints.</summary>
    public decimal CenterLatitude { get; init; }

    /// <summary>Private — never expose on public search endpoints.</summary>
    public decimal CenterLongitude { get; init; }

    public string? DisplayLabel { get; init; }
    public DateTime DateCreated { get; init; }
    public DateTime? DateUpdated { get; init; }
    public Guid CreatedByAppUserId { get; init; }
    public Guid? UpdatedByAppUserId { get; init; }
}
