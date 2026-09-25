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
    /// <summary>Who can see this entry — org only, the client too, or public.</summary>
    public CaseTimelineVisibility Visibility { get; init; }

    /// <summary>The investigation this entry was recorded during, or null if it wasn't.</summary>
    public Guid? InvestigationId { get; init; }
    public IReadOnlyList<Guid> ExperienceTypeIds { get; init; } = [];
    public IReadOnlyList<CaseTimelineFileRecord> Files { get; init; } = [];
    public DateTime DateCreated { get; init; }
    public DateTime? DateUpdated { get; init; }
    public Guid CreatedByAppUserId { get; init; }
    public Guid? UpdatedByAppUserId { get; init; }

    /// <summary>
    /// True for a row the timeline shows but does not own.
    /// </summary>
    /// <remarks>
    /// Nothing sets it today. Research pages did, from 2026-09-14 until they were retired on 2026-09-16, and the
    /// next thing that wants to put a row on the timeline it does not own will want it again — so the flag stays,
    /// and the rows that carry it stay uneditable here.
    /// </remarks>
    public bool IsReadOnly { get; init; }
}
