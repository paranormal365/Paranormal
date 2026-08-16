namespace Ben.Video.Core.SidecarContracts;

/// <summary>
/// Response body for <c>GET /v1/health</c> — item #38 phase E. Deliberately carries no secrets
/// (never the pairing token) and no information about what the sidecar has cached or is doing;
/// it's the one endpoint reachable without a token, so it must be safe to expose to a bare,
/// unauthenticated GET.
/// </summary>
public sealed record HealthInfo(
    int ProtocolVersion,
    string AppVersion,
    string? FfmpegVersion,
    bool FfmpegIntegrityOk,
    bool RequiresPairing);
