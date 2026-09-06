namespace Ben.Service.Models.Entities;

/// <summary>A file linked to a case's general Files/Evidence tab.</summary>
public record CaseFileRecord
{
    public Guid Id { get; init; }
    public Guid CaseId { get; init; }
    public Guid UploadFileId { get; init; }
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public long FileSize { get; init; }
    public string? Description { get; init; }
    public DateTime DateCreated { get; init; }
    public Guid CreatedByAppUserId { get; init; }

    /// <summary>
    /// How long the audio or video is, when that has been measured. Null when it has not.
    /// </summary>
    /// <remarks>
    /// The mixer draws each placed clip at its real length, and without this it drew every one of
    /// them the same width — a three-minute recording and a four-second one looked identical, so
    /// the grid could not represent the thing it held (2026-09-06 audio walk, finding K-length).
    /// Null is meaningful and is drawn as "length unknown" rather than guessed at.
    /// </remarks>
    public double? DurationSeconds { get; init; }
}

public record LinkCaseFileRequest(string? Description);
