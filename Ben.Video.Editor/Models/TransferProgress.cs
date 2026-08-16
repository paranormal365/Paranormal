namespace Ben.Video.Editor.Models;

/// <summary>
/// Snapshot of an in-progress HTTP upload or download transfer.
/// Reported via <see cref="IProgress{T}"/> callbacks from <c>ProjectService</c>.
/// </summary>
public sealed record TransferProgress
{
    /// <summary>Bytes transferred so far.</summary>
    public long Bytes { get; init; }

    /// <summary>
    /// Total expected bytes, or <c>-1</c> when the server did not supply
    /// a <c>Content-Length</c> header (download) or the payload size is unknown.
    /// </summary>
    public long TotalBytes { get; init; }

    /// <summary>
    /// 0–100 percentage, or <c>-1</c> when <see cref="TotalBytes"/> is unknown.
    /// </summary>
    public int Percent => TotalBytes > 0
        ? (int)Math.Clamp(Bytes * 100L / TotalBytes, 0, 100)
        : -1;

    /// <summary>Human-readable bytes transferred (e.g. "1.4 MB").</summary>
    public string FormattedBytes => FormatBytes(Bytes);

    /// <summary>Human-readable total size, or "?" when unknown.</summary>
    public string FormattedTotal => TotalBytes > 0 ? FormatBytes(TotalBytes) : "?";

    // ── helpers ─────────────────────────────────────────────────────────────

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
        >= 1_048_576     => $"{bytes / 1_048_576.0:F1} MB",
        >= 1_024         => $"{bytes / 1_024.0:F1} KB",
        _                => $"{bytes} B"
    };
}
