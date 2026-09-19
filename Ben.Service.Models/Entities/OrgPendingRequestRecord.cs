using Ben.Data.Common.Enums;

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
    public ClientOrgRequestStatus Status { get; init; } = ClientOrgRequestStatus.Pending;

    /// <summary>
    /// Whether the client can be reached yet. A request made from the signed-out wizard belongs
    /// to an account that has not confirmed its email, so no mail reaches its owner and they
    /// cannot sign in to read messages — the group should know why a reply may be slow.
    /// </summary>
    public bool ClientEmailConfirmed { get; init; } = true;
}
