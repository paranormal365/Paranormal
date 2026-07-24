using Ben.Data.Common.Enums;

namespace Ben.Service.Models.Entities;

public record ClientRequestRecord
{
    public Guid Id { get; init; }
    public Guid AppUserId { get; init; }
    public ClientRequestStatus Status { get; init; }
    public string StreetAddress1 { get; init; } = null!;
    public string? StreetAddress2 { get; init; }
    public string City { get; init; } = null!;
    public string State { get; init; } = null!;
    public string ZipCode { get; init; } = null!;
    public string Country { get; init; } = null!;
    public decimal? Latitude { get; init; }
    public decimal? Longitude { get; init; }
    public ClientGender Gender { get; init; }
    public int? BirthYear { get; init; }
    public string? Description { get; init; }
    public DateTime DateCreated { get; init; }
    public DateTime? DateUpdated { get; init; }
    public Guid CreatedByAppUserId { get; init; }
    public Guid? UpdatedByAppUserId { get; init; }
}
