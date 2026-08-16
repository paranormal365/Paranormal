namespace Ben.Video.Core.SidecarContracts;

/// <summary>
/// Response body for <c>GET /v1/capabilities</c> — item #70 phase 158.
///
/// <para><b>Why this is a separate endpoint rather than fields on <see cref="HealthInfo"/>:</b>
/// <see cref="SidecarJsonOptions.Default"/> uses <c>JsonUnmappedMemberHandling.Disallow</c>, which
/// cuts BOTH ways. Adding a field to <see cref="HealthInfo"/> would make an older cached browser
/// build fail to parse <c>/v1/health</c> at all — and that parse failure is swallowed by
/// <c>NativeSidecarService.ProbeAsync</c>'s per-port <c>catch</c>, so the user would see "no
/// sidecar found" and silently lose even the v2 rendering that used to work. A brand-new endpoint
/// is additive by construction: an old client never calls it, and a new client treats 404 as
/// "legacy sidecar, segment rendering only".</para>
///
/// <para><see cref="InstanceId"/> is a fresh GUID per sidecar process. Phase 160's client-side
/// remote-segment index keys off it: if the id changes between probes, the sidecar restarted and
/// every previously-retained segment id it handed out is stale, so the whole index is dropped
/// rather than pointing at segments that no longer exist.</para>
/// </summary>
public sealed record CapabilitiesInfo(
    int ProtocolVersion,
    Guid InstanceId,
    IReadOnlyList<string> Capabilities);

/// <summary>
/// The capability strings a sidecar can advertise. Shared constants rather than loose literals so
/// the server's advertisement and the client's <c>HasCapability</c> checks can never drift apart
/// in spelling — they compile against the same names.
/// </summary>
public static class SidecarCapabilities
{
    /// <summary>Per-clip segment rendering — <c>POST /v1/jobs/segment</c>. Every sidecar since
    /// phase 123 has this; it's the implied capability of a legacy v2 sidecar that has no
    /// <c>/v1/capabilities</c> endpoint at all.</summary>
    public const string Segment = "segment";

    /// <summary>Media probing via ffprobe — advertised only when a verified ffprobe binary is
    /// actually present (phase 159 consumes this).</summary>
    public const string Probe = "probe";

    /// <summary>Thumbnail-strip extraction (phase 159). Gated on the same verified ffprobe as
    /// <see cref="Probe"/> — the job itself runs ffmpeg, but the client only ever asks for
    /// thumbnails on a path where it also probes, so they're advertised together.</summary>
    public const string Thumbnails = "thumbnails";

    /// <summary>Retained segments + stream-copy concat of them (phase 160).</summary>
    public const string Concat = "concat";

    /// <summary>Whole-export concat + audio mix assembly (phase 162).</summary>
    public const string ExportAssemble = "export-assemble";
}
