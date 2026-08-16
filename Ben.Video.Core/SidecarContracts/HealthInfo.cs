namespace Ben.Video.Core.SidecarContracts;

/// <summary>
/// Response body for <c>GET /v1/health</c> — item #38 phase E. Deliberately carries no secrets
/// (never the pairing token) and no information about what the sidecar has cached or is doing;
/// it's the one endpoint reachable without a token, so it must be safe to expose to a bare,
/// unauthenticated GET.
/// </summary>
/// <remarks>
/// <see cref="InstallId"/> and <see cref="Platform"/> are optional and default to null, so an
/// editor talking to an older sidecar simply gets nothing rather than failing to deserialize.
/// Neither is a secret: the id is a locally-generated value that means nothing until a signed-in
/// browser reports it, and the platform is already obvious from the download.
/// </remarks>
public sealed record HealthInfo(
    int ProtocolVersion,
    string AppVersion,
    string? FfmpegVersion,
    bool FfmpegIntegrityOk,
    bool RequiresPairing,
    Guid? InstallId = null,
    string? Platform = null);
