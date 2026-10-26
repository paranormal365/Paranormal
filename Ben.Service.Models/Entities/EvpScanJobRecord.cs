using Ben.Data.Common.Enums;

namespace Ben.Service.Models.Entities;

/// <summary>A background EVP scan and how far it has got (phase 6, 10/26/2026).</summary>
/// <param name="Percent">0–100 over the whole job, weighted by each recording's length.</param>
/// <param name="Error">Why it failed, in a sentence, or null.</param>
public sealed record EvpScanJobRecord(
    Guid Id,
    EvpScanJobStatus Status,
    int Percent,
    string? Error,
    IReadOnlyList<EvpScanSegmentRecord> Segments,
    DateTime CreatedUtc,
    DateTime? StartedUtc,
    DateTime? FinishedUtc)
{
    /// <summary>Queued or running: the editor keeps watching.</summary>
    public bool IsLive => Status is EvpScanJobStatus.Queued or EvpScanJobStatus.Running;
}

/// <summary>One recording in a background scan.</summary>
/// <param name="SettledSeconds">Audio whose result can no longer change; drawn solid in the editor.</param>
/// <param name="TotalSeconds">The recording's length when known up front.</param>
/// <param name="CandidateCount">When done, how many candidates it proposed.</param>
public sealed record EvpScanSegmentRecord(
    Guid UploadFileId,
    EvpScanJobStatus Status,
    int Percent,
    double ProcessedSeconds,
    double SettledSeconds,
    double? TotalSeconds,
    int? CandidateCount,
    float? TopScore,
    string? Error);

/// <summary>Starts a background scan of one recording.</summary>
/// <param name="Options">The settings to scan with; null means the sensitivity's preset.</param>
public sealed record StartScanJobRequest(EvpSensitivity Sensitivity, EvpDetectionOptions? Options = null);
