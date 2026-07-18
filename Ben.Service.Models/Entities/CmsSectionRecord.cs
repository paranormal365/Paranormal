using Ben.Data.Common.Enums;

namespace Ben.Service.Models.Entities;

public record CmsSectionRecord
{
    public Guid Id { get; init; }
    public Guid OrganizationPageId { get; init; }
    public CmsSectionType SectionType { get; init; }
    public string? Title { get; init; }
    public string ContentJson { get; init; } = "{}";
    public int SortOrder { get; init; }
    public bool IsActive { get; init; }
    public DateTime DateCreated { get; init; }
    public DateTime? DateUpdated { get; init; }
    public Guid CreatedByAppUserId { get; init; }
    public Guid? UpdatedByAppUserId { get; init; }
}
