namespace Ben.Service.Models.Admin;

/// <summary>Admin metadata record — FileData is excluded to keep responses lightweight.</summary>
public record UploadFileAdminRecord
{
    public Guid Id { get; init; }
    public Guid UploadFileTypeId { get; init; }
    public Guid AppUserId { get; init; }
    public required string FileName { get; init; }
    public required string StoredFileName { get; init; }
    public required string ContentType { get; init; }
    public long FileSize { get; init; }
    public string? Description { get; init; }
    public bool IsPublic { get; init; }
    public int SortOrder { get; init; }
    public DateTime DateCreated { get; init; }
    public DateTime? DateUpdated { get; init; }
    public Guid CreatedByAppUserId { get; init; }
    public Guid? UpdatedByAppUserId { get; init; }

    /// <summary>Set when this file was created by clipping a parent audio file.</summary>
    public Guid?   ParentFileId { get; init; }
    public double? RegionStart  { get; init; }
    public double? RegionEnd    { get; init; }
}
