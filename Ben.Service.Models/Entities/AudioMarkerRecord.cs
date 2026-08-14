using Ben.Data.Common.Enums;

namespace Ben.Service.Models.Entities;

/// <summary>Read-only projection of an EVP marker anchored to a point in an audio file.</summary>
public record AudioMarkerRecord
{
    public Guid Id { get; init; }
    public Guid UploadFileId { get; init; }
    public double TimeSeconds { get; init; }
    public required string Label { get; init; }
    public EvpConfidenceLevel ConfidenceLevel { get; init; }
    public string? Note { get; init; }
    public DateTime DateCreated { get; init; }
    public DateTime? DateUpdated { get; init; }
    public Guid CreatedByAppUserId { get; init; }
    public Guid? UpdatedByAppUserId { get; init; }
}

// ── Requests ──────────────────────────────────────────────────────────────────

public record CreateAudioMarkerRequest(
    double             TimeSeconds,
    string             Label,
    EvpConfidenceLevel ConfidenceLevel,
    string?            Note);

public record UpdateAudioMarkerRequest(
    double             TimeSeconds,
    string             Label,
    EvpConfidenceLevel ConfidenceLevel,
    string?            Note);
