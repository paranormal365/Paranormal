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

    /// <summary>Null for a visit with no client case, along with <see cref="CaseReference"/>.</summary>
    public Guid? CaseId { get; init; }

    /// <summary>Null when there is no case. Not "—" or "(none)": rendering is the UI's business.</summary>
    public string? CaseReference { get; init; }

    /// <summary>Always set — read from the investigation, not through the case.</summary>
    public Guid OrganizationId { get; init; }
    public required string OrganizationName { get; init; }
    public required string Title { get; init; }
    public DateTime ScheduledDateTime { get; init; }
    public DateTime? EndDateTime { get; init; }
    public InvestigationStatus Status { get; init; }
}
