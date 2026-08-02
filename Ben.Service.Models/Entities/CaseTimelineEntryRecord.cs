using Ben.Data.Common.Enums;

namespace Ben.Service.Models.Entities;

public record CaseTimelineEntryRecord
{
    public Guid Id { get; init; }
    public Guid CaseId { get; init; }
    public Guid AuthorAppUserId { get; init; }
    public string? AuthorDisplayName { get; init; }
    public CaseTimelineEntryType EntryType { get; init; }
    public DateTime? EventDateTime { get; init; }
    public string? Title { get; init; }
    public string? Body { get; init; }
    public bool IsPublic { get; init; }
    public IReadOnlyList<Guid> ExperienceTypeIds { get; init; } = [];
    public IReadOnlyList<CaseTimelineFileRecord> Files { get; init; } = [];
    public DateTime DateCreated { get; init; }
    public DateTime? DateUpdated { get; init; }
    public Guid CreatedByAppUserId { get; init; }
    public Guid? UpdatedByAppUserId { get; init; }
}
