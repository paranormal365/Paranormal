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
    /// The research page this row stands for, when it is one (2026-09-14): a published research page with a date appears on
    /// the timeline where it happened. Such a row is changed on its page, not here.
    /// </summary>
    public Guid? ResearchEntryId { get; init; }

    /// <summary>True for a row the timeline shows but does not own — a research page's.</summary>
    public bool IsReadOnly { get; init; }
}
