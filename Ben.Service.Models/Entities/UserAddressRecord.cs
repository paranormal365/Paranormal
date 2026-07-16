namespace Ben.Service.Models.Entities;

public record UserAddressRecord
{
    public Guid Id { get; init; }
    public Guid UserAddressTypeId { get; init; }
    public Guid AppUserId { get; init; }
    public required string StreetAddress1 { get; init; }
    public string? StreetAddress2 { get; init; }
    public required string City { get; init; }
    public required string State { get; init; }
    public required string ZipCode { get; init; }
    public required string Country { get; init; }
    public bool IsPublic { get; init; }
    public int SortOrder { get; init; }
    public decimal? Longitude { get; init; }
    public decimal? Latitude { get; init; }
    public DateTime DateCreated { get; init; }
    public DateTime? DateUpdated { get; init; }
    public Guid CreatedByAppUserId { get; init; }
    public Guid? UpdatedByAppUserId { get; init; }
}
