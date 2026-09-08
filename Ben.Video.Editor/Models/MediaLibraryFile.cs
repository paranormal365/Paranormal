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

    /// <summary>Who this file belongs to — a person, or a group when one was handed it.</summary>
    /// <remarks>
    /// V-3 of the 2026-09-06 evaluation: the Server tab listed seven identical
    /// <c>test-audio.mp3</c> rows, reachable through a shared group, with nothing to tell them
    /// apart. Null when the host does not supply it, and the card simply omits the line.
    /// </remarks>
    public string? OwnerDisplayName { get; init; }

    /// <summary>The case this file is attached to, as its reference, or null.</summary>
    public string? CaseReference { get; init; }

    /// <summary>
    /// The owner and the case on one line, or null when neither is known.
    /// </summary>
    /// <remarks>
    /// One place builds it so the two hosts' cards cannot come out differently.
    /// </remarks>
    public string? Provenance => (OwnerDisplayName, CaseReference) switch
    {
        (null,     null)  => null,
        (null,     var c) => c,
        (var o,    null)  => o,
        var (o, c)        => $"{o} · {c}",
    };

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
