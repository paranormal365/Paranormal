namespace Ben.Service.Models.Admin;

public record OrganizationPhoneAdminRecord
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public Guid OrganizationPhoneTypeId { get; init; }
    public bool IsValidated { get; init; }
    public required string PhoneNumber { get; init; }
    public DateTime? DateValidated { get; init; }
    public string? PhoneCountry { get; init; }
    public bool IsPrimary { get; init; }
    public bool IsPublic { get; init; }
    public bool IsCellular { get; init; }
    public DateTime DateCreated { get; init; }
    public DateTime? DateUpdated { get; init; }
    public Guid CreatedByAppUserId { get; init; }
    public Guid? UpdatedByAppUserId { get; init; }
}
