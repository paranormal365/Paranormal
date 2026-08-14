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
/// <param name="Start">Clip start in seconds, including whatever lead-in the caller wants.</param>
/// <param name="End">Clip end in seconds.</param>
/// <param name="Label">Optional name for the clip; a descriptive one is generated when omitted.</param>
/// <param name="IsPublic">Whether the resulting file is publicly visible.</param>
/// <param name="UploadFileTypeId">File type to file the clip under.</param>
/// <param name="Normalize">
/// Raise the clip's peak to just under full scale. An EVP is usually far quieter than the
/// recording around it, so an un-normalized clip is often near-inaudible on anything but
/// headphones. Deliberately the only processing baked in: everything else stays reversible,
/// because <c>ParentFileId</c>/<c>RegionStart</c>/<c>RegionEnd</c> mean a clip can always be re-cut
/// from the original.
/// </param>
/// <param name="SourceMarkerId">
/// The EVP marker this clip was cut from, when there is one. Links the two so the marker can show
/// its clip and the clip can be traced back to the finding that justified it.
/// </param>
public record ClipAudioRequest(
    double  Start,
    double  End,
    string? Label,
    bool    IsPublic,
    Guid    UploadFileTypeId,
    bool    Normalize      = false,
    Guid?   SourceMarkerId = null);
