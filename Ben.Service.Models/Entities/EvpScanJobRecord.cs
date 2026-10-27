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

/// <summary>Scan several recordings in one background job (phase 10).</summary>
public sealed record StartBatchScanRequest(
    IReadOnlyList<Guid> FileIds, EvpSensitivity Sensitivity, EvpDetectionOptions? Options = null);

/// <summary>What happened to each recording asked for: in the job, or why not.</summary>
/// <param name="Job">The job every accepted recording is in; null when none was.</param>
public sealed record BatchScanResult(EvpScanJobRecord? Job, IReadOnlyList<BatchScanItem> Items);

/// <param name="Accepted">True when the recording is in the job.</param>
/// <param name="Refusal">Why it is not, in a sentence.</param>
public sealed record BatchScanItem(Guid FileId, string? FileName, bool Accepted, string? Refusal);

/// <summary>One of the person's scans for the Scans page: the job, which settings, and each recording by name.</summary>
/// <param name="Settings">"Low", "Medium", "High", or "Custom".</param>
/// <param name="FieldSessionUploadId">Set when the scan was of a field session's recordings.</param>
public sealed record EvpScanSummaryRecord(
    EvpScanJobRecord Job,
    string Settings,
    Guid? FieldSessionUploadId,
    IReadOnlyList<EvpScanSummaryFile> Files);

/// <param name="SessionId">For a copy of a session recording, the session it came from (the editor opens it there).</param>
public sealed record EvpScanSummaryFile(Guid FileId, string FileName, Guid? SessionId);
