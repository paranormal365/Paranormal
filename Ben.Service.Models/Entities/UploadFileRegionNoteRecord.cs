namespace Ben.Service.Models.Entities;

/// <summary>Read-only projection of a region note.</summary>
public record UploadFileRegionNoteRecord
{
    public Guid Id { get; init; }
    public Guid UploadFileId { get; init; }
    public double RegionStart { get; init; }
    public double RegionEnd { get; init; }
    public string? RegionLabel { get; init; }
    /// <summary>Null = whole-region note; value = absolute time (seconds) in the audio file.</summary>
    public double? TimeOffset { get; init; }
    public required string NoteHtml { get; init; }
    public bool IsPublic { get; init; }
    public DateTime DateCreated { get; init; }
    public DateTime? DateUpdated { get; init; }
    public Guid CreatedByAppUserId { get; init; }
    public Guid? UpdatedByAppUserId { get; init; }
}

// ── Requests ──────────────────────────────────────────────────────────────────

public record CreateRegionNoteRequest(
    double  RegionStart,
    double  RegionEnd,
    string? RegionLabel,
    double? TimeOffset,
    string  NoteHtml,
    bool    IsPublic);

public record UpdateRegionNoteRequest(
    double? TimeOffset,
    string  NoteHtml,
    bool    IsPublic);

// ── Audio clip ────────────────────────────────────────────────────────────────

/// <summary>Clips an existing upload-file's audio to the specified time bounds and saves as a new UploadFile.</summary>
public record ClipAudioRequest(
    double  Start,
    double  End,
    string? Label,
    bool    IsPublic,
    Guid    UploadFileTypeId);
