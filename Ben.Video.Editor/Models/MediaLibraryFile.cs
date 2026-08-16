namespace Ben.Video.Editor.Models;

/// <summary>
/// Lightweight DTO representing a file from the AverageBen media library.
/// Mirrors the fields of <c>UploadFileRecord</c> that the editor needs.
/// </summary>
public sealed record MediaLibraryFile
{
    public Guid   Id          { get; init; }
    public string FileName    { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long   FileSize    { get; init; }
    public string? Description { get; init; }
    public DateTime DateCreated { get; init; }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>True when the file is a video (content-type starts with "video/").</summary>
    public bool IsVideo => ContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the file is audio-only (content-type starts with "audio/").</summary>
    public bool IsAudio => ContentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase);

    /// <summary>Human-readable file size (e.g. "4.2 MB").</summary>
    public string FileSizeDisplay => FileSize switch
    {
        >= 1_073_741_824 => $"{FileSize / 1_073_741_824.0:F1} GB",
        >= 1_048_576     => $"{FileSize / 1_048_576.0:F1} MB",
        >= 1_024         => $"{FileSize / 1_024.0:F1} KB",
        _                => $"{FileSize} B",
    };
}
