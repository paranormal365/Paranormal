namespace Ben.Video.Editor.Services;

/// <summary>
/// Optional hook for telling the server that this browser paired with a native sidecar.
/// </summary>
/// <remarks>
/// <para>An interface rather than a call, because the editor is deliberately host-agnostic: it does
/// not know the site's address and has no way to authenticate to it. Each host implements this with
/// whatever it already uses to reach its own API — the circuit's token under Blazor Server, the
/// browser's bearer token under WebAssembly — and the editor just says when.</para>
///
/// <para>Registration is optional. The standalone editor, or a host that would rather not report,
/// simply doesn't register one and nothing happens.</para>
/// </remarks>
public interface ISidecarPairingReporter
{
    /// <summary>
    /// Called once after a pairing succeeds. Implementations must not throw: the pairing has
    /// already happened, and failing to record it is not a reason to tell the user it failed.
    /// </summary>
    Task ReportPairedAsync(Guid installId, string? version, string? platform, CancellationToken ct = default);
}
