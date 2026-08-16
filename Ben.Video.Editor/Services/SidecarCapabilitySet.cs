using Ben.Video.Core.SidecarContracts;

namespace Ben.Video.Editor.Services;

/// <summary>
/// What a connected sidecar can do, as the browser understands it — item #70 phase 158. Pure and
/// immutable so the "which features may I use against this sidecar" decision is testable without
/// any HTTP, JS interop, or connection state.
///
/// <para>The important case is <see cref="Legacy"/>: a sidecar from before phase 158 has no
/// <c>/v1/capabilities</c> endpoint at all and answers 404. That is NOT an error — it's a
/// perfectly good v2 sidecar that can still render segments, so it maps to exactly the one
/// capability it has always had. Every later phase gates its new work behind
/// <see cref="Has"/>, so an old sidecar keeps doing what it always did while a new one lights up
/// the extra paths.</para>
/// </summary>
public sealed class SidecarCapabilitySet
{
    private readonly HashSet<string> _capabilities;

    private SidecarCapabilitySet(int protocolVersion, Guid? instanceId, IEnumerable<string> capabilities)
    {
        ProtocolVersion = protocolVersion;
        InstanceId      = instanceId;
        _capabilities   = new HashSet<string>(capabilities, StringComparer.Ordinal);
    }

    public int ProtocolVersion { get; }

    /// <summary>Identity of the sidecar process. Null for a legacy sidecar (it can't report one),
    /// which phase 160 treats as "never trust retained-segment ids from this connection".</summary>
    public Guid? InstanceId { get; }

    public IReadOnlyCollection<string> Capabilities => _capabilities;

    public bool Has(string capability) => _capabilities.Contains(capability);

    /// <summary>A pre-158 sidecar: no capabilities endpoint, but segment rendering has worked
    /// since phase 123, so that's exactly what it's credited with.</summary>
    public static SidecarCapabilitySet Legacy { get; } =
        new(protocolVersion: 2, instanceId: null, [SidecarCapabilities.Segment]);

    /// <summary>Nothing usable — not connected, or the capabilities call failed in a way that
    /// isn't a clean 404 (which would be <see cref="Legacy"/>).</summary>
    public static SidecarCapabilitySet None { get; } =
        new(protocolVersion: 0, instanceId: null, []);

    /// <summary>Maps a parsed <see cref="CapabilitiesInfo"/> response into the browser's view of
    /// it. A null response (parse failure, empty body) degrades to <see cref="Legacy"/> rather
    /// than <see cref="None"/> — the sidecar demonstrably answered health and the token check to
    /// get this far, so segment rendering is known-good regardless of what happened here.</summary>
    public static SidecarCapabilitySet FromResponse(CapabilitiesInfo? info) =>
        info is null
            ? Legacy
            : new SidecarCapabilitySet(info.ProtocolVersion, info.InstanceId, info.Capabilities ?? []);
}
