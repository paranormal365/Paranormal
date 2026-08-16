namespace Ben.Video.Core.SidecarContracts;

/// <summary>
/// Response body for <c>GET /v1/status</c> — a fully-authenticated ping used by
/// <c>NativeSidecarService.PairAsync</c> to confirm a pasted pairing code actually works, and by
/// later reconnect checks. Unlike <see cref="HealthInfo"/>, this can safely describe the
/// sidecar's current load, since only a caller that already presented a valid token sees it.
/// </summary>
public sealed record StatusInfo(
    int ProtocolVersion,
    string AppVersion,
    string? FfmpegVersion,
    int ActiveJobCount,
    int MaxConcurrentJobs);
