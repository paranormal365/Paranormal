using Ben.Data.Common.Enums;

namespace Ben.Service.Models.Entities;

public record UploadFilePermissionRequestRecord
{
    public Guid Id { get; init; }
    public Guid UploadFileId { get; init; }
    public Guid? OrganizationId { get; init; }
    public Guid RequestedByAppUserId { get; init; }
    public FilePermissionType PermissionType { get; init; }
    public FilePermissionRequestStatus RequestStatus { get; init; }
    public string? RequestNotes { get; init; }
    public string? ReviewNotes { get; init; }
    public Guid? ReviewedByAppUserId { get; init; }
    public DateTime? DateReviewed { get; init; }
    public DateTime DateCreated { get; init; }
    public DateTime? DateUpdated { get; init; }
    public Guid CreatedByAppUserId { get; init; }
    public Guid? UpdatedByAppUserId { get; init; }
}
