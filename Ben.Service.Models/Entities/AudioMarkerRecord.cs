using Ben.Data.Common.Enums;

namespace Ben.Service.Models.Entities;

/// <summary>Read-only projection of an EVP marker on an audio file — a point or a span.</summary>
public record AudioMarkerRecord
{
    public Guid Id { get; init; }
    public Guid UploadFileId { get; init; }
    public double TimeSeconds { get; init; }

    /// <summary>End of the marked span, or null for a point marker.</summary>
    public double? EndSeconds { get; init; }

    public required string Label { get; init; }
    public EvpConfidenceLevel ConfidenceLevel { get; init; }
    public string? Note { get; init; }

    /// <summary>True when the detector proposed this rather than a person placing it.</summary>
    public bool IsAutoDetected { get; init; }

    /// <summary>The detector's 0–100 signal score, or null for a hand-placed marker.</summary>
    public float? DetectionScore { get; init; }

    /// <summary>Where this marker stands in review.</summary>
    public EvpReviewStatus ReviewStatus { get; init; }

    /// <summary>The clip cut from this marker, when one exists.</summary>
    public Guid? LinkedClipUploadFileId { get; init; }

    public DateTime DateCreated { get; init; }
    public DateTime? DateUpdated { get; init; }
    public Guid CreatedByAppUserId { get; init; }
    public Guid? UpdatedByAppUserId { get; init; }

    /// <summary>Convenience for callers deciding between a point and a region: a span needs a real end after the start.</summary>
    public bool IsSpan => EndSeconds is { } end && end > TimeSeconds;
}

// ── Requests ──────────────────────────────────────────────────────────────────

public record CreateAudioMarkerRequest(
    double             TimeSeconds,
    string             Label,
    EvpConfidenceLevel ConfidenceLevel,
    string?            Note,
    double?            EndSeconds = null);

public record UpdateAudioMarkerRequest(
    double             TimeSeconds,
    string             Label,
    EvpConfidenceLevel ConfidenceLevel,
    string?            Note,
    double?            EndSeconds = null);

/// <summary>One detector proposal, before anyone has looked at it.</summary>
/// <param name="StartSeconds">Start of the span.</param>
/// <param name="EndSeconds">End of the span. Candidates are always spans — a detector has no way to mean "this instant".</param>
/// <param name="Score">0–100 signal score from the detector.</param>
public record AudioCandidateRequest(
    double StartSeconds,
    double EndSeconds,
    float  Score);

/// <summary>
/// The full result of one scan, replacing whatever the previous scan proposed for this file.
/// </summary>
/// <remarks>
/// Sent as a batch rather than one POST per candidate: a scan produces up to a few hundred at once,
/// and the replace-then-insert has to be atomic or a failure mid-way leaves the file showing half
/// of two different scans.
/// </remarks>
/// <param name="Candidates">Everything the scan proposed, already deduped client-side against confirmed and dismissed markers.</param>
public record BulkCreateAudioCandidatesRequest(
    IReadOnlyList<AudioCandidateRequest> Candidates);

/// <summary>The outcome of a person reviewing a candidate.</summary>
/// <param name="ReviewStatus">Confirmed or Dismissed. Pending is not a decision and is rejected.</param>
/// <param name="Label">Label to give a confirmed marker. Ignored when dismissing.</param>
/// <param name="ConfidenceLevel">The reviewer's confidence. Ignored when dismissing.</param>
/// <param name="Note">Optional note.</param>
/// <param name="StartSeconds">Adjusted span start, when the reviewer moved the bounds.</param>
/// <param name="EndSeconds">Adjusted span end, when the reviewer moved the bounds.</param>
public record ReviewAudioMarkerRequest(
    EvpReviewStatus     ReviewStatus,
    string?             Label            = null,
    EvpConfidenceLevel? ConfidenceLevel  = null,
    string?             Note             = null,
    double?             StartSeconds     = null,
    double?             EndSeconds       = null);
