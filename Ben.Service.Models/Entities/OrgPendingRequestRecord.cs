namespace Ben.Service.Models.Entities;

/// <summary>
/// Anonymized view of a client investigation request for an organization reviewing
/// pending applications. Exact address is withheld until the org accepts the case.
/// </summary>
public record OrgPendingRequestRecord
{
    public Guid ClientRequestId { get; init; }
    public DateTime DateApplied { get; init; }
    public DateTime DateSubmitted { get; init; }
    public string City { get; init; } = null!;
    public string State { get; init; } = null!;
    public string ZipCode { get; init; } = null!;
    public string? Description { get; init; }
    public decimal? Latitude { get; init; }
    public decimal? Longitude { get; init; }
}
