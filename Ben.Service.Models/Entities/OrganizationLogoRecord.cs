namespace Ben.Service.Models.Entities;

public record OrganizationLogoRecord
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public Guid UploadFileId { get; init; }
    public string? AltText { get; init; }
    public bool IsActive { get; init; }
    public int SortOrder { get; init; }
    public DateTime DateCreated { get; init; }
    public DateTime? DateUpdated { get; init; }
    public Guid CreatedByAppUserId { get; init; }
    public Guid? UpdatedByAppUserId { get; init; }
}
