using Ben.Data.Common.Enums;
using Ben.Service.Models.Entities;

namespace Ben.Service.Models.FieldSessions;

/// <summary>
/// Everything a page needs to offer an EVP scan of a field session (phase 9, 10/27/2026): whether
/// this person may, whose copy they would work on, the recordings, and any scan already running.
/// </summary>
/// <param name="MayScan">True when the person may scan and there is something to scan.</param>
/// <param name="Refusal">Why not, as a fixed key (<see cref="ScanRefusal"/>), when not.</param>
/// <param name="Message">Why not, as a sentence.</param>
/// <param name="Scopes">The copies they may make: their own, or a group's. The first is the default.</param>
/// <param name="Clips">The session's sound recordings, with what the chosen copy holds.</param>
/// <param name="LiveJob">Their scan of this session that is still waiting or running.</param>
/// <param name="DecoderAvailable">Whether this server can read the phone's .m4a recordings.</param>
public sealed record FieldSessionAnalysisView(
    bool MayScan,
    ScanRefusal Refusal,
    string? Message,
    IReadOnlyList<AnalysisScopeRecord> Scopes,
    IReadOnlyList<AnalysisClipRecord> Clips,
    EvpScanJobRecord? LiveJob,
    bool DecoderAvailable);

/// <param name="Label">"Your own copy", "Hollow Creek Paranormal's copy".</param>
public sealed record AnalysisScopeRecord(AnalysisScope Scope, Guid? OrganizationId, string Label);

/// <summary>One sound recording in the session.</summary>
/// <param name="FileId">The recording within the session.</param>
/// <param name="CopyUploadFileId">The chosen copy, once one has been made.</param>
/// <param name="PendingCount">Candidates on the copy waiting for a decision.</param>
/// <param name="ConfirmedCount">Candidates and markers kept on the copy.</param>
public sealed record AnalysisClipRecord(
    Guid FileId,
    string RelativePath,
    long FileSize,
    Guid? CopyUploadFileId,
    int PendingCount,
    int ConfirmedCount,
    DateTime? LastScanUtc);

/// <summary>Which copy to make or use; omitted, the first the person may have.</summary>
public sealed record AnalysisCopyRequest(AnalysisScope? Scope = null, Guid? OrganizationId = null);

/// <summary>A scanner's copy of one session recording, ready to open in the editor.</summary>
public sealed record AnalysisCopyRecord(
    Guid CopyUploadFileId,
    string FileName,
    string ContentType,
    long FileSize,
    string ScopeLabel,
    string RelativePath,
    DateTime SessionStartedAt,
    string? SessionLabel);

/// <summary>Scan a session's recordings: all of them, or those named.</summary>
public sealed record StartSessionScanRequest(
    EvpSensitivity Sensitivity,
    EvpDetectionOptions? Options = null,
    AnalysisScope? Scope = null,
    Guid? OrganizationId = null,
    IReadOnlyList<Guid>? ClipFileIds = null);

/// <summary>An EVP somebody saved from one of this session's recordings.</summary>
/// <param name="SourceClipPath">The session recording it was cut from.</param>
/// <param name="RegionStart">Where in that recording, in seconds.</param>
public sealed record SessionEvpRecord(
    Guid FileId,
    string FileName,
    string ContentType,
    string? SourceClipPath,
    double? RegionStart,
    double? RegionEnd,
    string MadeBy,
    DateTime DateCreated);
