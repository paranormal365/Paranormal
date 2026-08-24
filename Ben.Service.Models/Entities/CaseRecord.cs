using Ben.Data.Common.Enums;

namespace Ben.Service.Models.Entities;

public record CaseRecord
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public Guid? ClientRequestId { get; init; }
    public Guid? CaseManagerAppUserId { get; init; }
    public string? CaseManagerDisplayName { get; init; }
    public CaseStatus Status { get; init; }
    public required string Title { get; init; }
    public int CaseYear { get; init; }
    public int OrgCaseNumber { get; init; }

    /// <summary>Human-readable reference, e.g. "#2026-042".</summary>
    public string CaseReference => $"#{CaseYear}-{OrgCaseNumber:D3}";

    /// <summary>Full display label, e.g. "#2026-042 — Smith, Nashville TN".</summary>
    public string DisplayLabel => $"{CaseReference} — {Title}";
    public string? Description { get; init; }
    public string StreetAddress1 { get; init; } = null!;
    public string? StreetAddress2 { get; init; }
    public string City { get; init; } = null!;
    public string State { get; init; } = null!;
    public string ZipCode { get; init; } = null!;
    public string Country { get; init; } = null!;
    public decimal? Latitude { get; init; }
    public decimal? Longitude { get; init; }
    public string? PublicPseudonym { get; init; }
    public bool IsPublic { get; init; }

    /// <summary>Item 184: private-lane work — public prose substitutes names, publication is plan-gated.</summary>
    public bool IsPrivateEngagement { get; init; }
    public DateTime DateCaseOpened { get; init; }
    public DateTime? DateCaseClosed { get; init; }
    public DateTime DateCreated { get; init; }
    public DateTime? DateUpdated { get; init; }
    public Guid CreatedByAppUserId { get; init; }
    public Guid? UpdatedByAppUserId { get; init; }
}


/// <summary>One place a client's real name appears in prose somebody typed (item 182).</summary>
public sealed record ClientNameOccurrence(
    string Where, string Field, string Matched, Guid EntityId, string Kind);

/// <summary>What applying a case's privacy protections after the fact did — and did not do.</summary>
public sealed record CasePrivacyRetrofitResult(
    bool MadePrivate,
    bool LocationGeneralized,
    int FilesStripped,
    int FilesAlreadyClean,
    int FilesUnstrippable,
    IReadOnlyList<ClientNameOccurrence> NameOccurrences,
    bool WasEverPublic);
