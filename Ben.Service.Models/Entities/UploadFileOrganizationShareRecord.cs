using Ben.Data.Common.Enums;

namespace Ben.Service.Models.Entities;

public record UploadFileOrganizationShareRecord
{
    public Guid Id { get; init; }
    public Guid UploadFileId { get; init; }
    public Guid OrganizationId { get; init; }
    public Guid SharedByAppUserId { get; init; }
    public FileShareVisibility Visibility { get; init; }
    public bool IsActive { get; init; }
    public Guid? RemovedByAppUserId { get; init; }
    public DateTime? RemovalDate { get; init; }
    public DateTime DateCreated { get; init; }
    public DateTime? DateUpdated { get; init; }
    public Guid CreatedByAppUserId { get; init; }
    public Guid? UpdatedByAppUserId { get; init; }
}
