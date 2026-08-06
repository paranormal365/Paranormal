using Ben.Data.Common.Enums;

namespace Ben.Service.Models.Entities;

/// <summary>A case summary for the SuperAdmin cross-org "All Cases" view (backlog item #2).</summary>
public record AdminCaseSummaryRecord
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public required string OrganizationName { get; init; }
    public required string Title { get; init; }
    public int CaseYear { get; init; }
    public int OrgCaseNumber { get; init; }

    /// <summary>Human-readable reference, e.g. "#2026-042".</summary>
    public string CaseReference => $"#{CaseYear}-{OrgCaseNumber:D3}";
    public CaseStatus Status { get; init; }
    public string City { get; init; } = null!;
    public string State { get; init; } = null!;
    public DateTime DateCaseOpened { get; init; }
    public DateTime? DateCaseClosed { get; init; }
}

/// <summary>An investigation summary for the SuperAdmin cross-org "All Investigations" view (backlog item #2).</summary>
public record AdminInvestigationSummaryRecord
{
    public Guid Id { get; init; }
    public Guid CaseId { get; init; }
    public required string CaseReference { get; init; }
    public Guid OrganizationId { get; init; }
    public required string OrganizationName { get; init; }
    public required string Title { get; init; }
    public DateTime ScheduledDateTime { get; init; }
    public DateTime? EndDateTime { get; init; }
    public InvestigationStatus Status { get; init; }
}
