namespace Ben.Video.Core.SidecarContracts;

/// <summary>
/// Wire-level constants shared by both sides of the sidecar protocol — the browser
/// (<c>NativeSidecarService</c>, Ben.Video.Editor) and the sidecar itself
/// (<c>SecurityMiddleware</c>, Ben.Video.Sidecar). Living here rather than duplicated as string
/// literals on each side means the header name and default ports can never silently drift apart.
/// </summary>
public static class SidecarProtocol
{
    /// <summary>Custom header carrying the pairing token — see DESIGN-item38-long-form-memory.md
    /// §5.4. Deliberately a custom header (not e.g. a query string or cookie): it forces every
    /// cross-origin browser request to go through a CORS preflight, which is the mechanism that
    /// lets the sidecar reject a page it never agreed to talk to before any real request lands.</summary>
    public const string TokenHeaderName = "X-BenVideo-Sidecar-Token";

    /// <summary>First port the sidecar tries to bind, and the first port the browser's probe
    /// tries — both sides must agree on this range or discovery never succeeds.</summary>
    public const int DefaultPort = 43117;

    /// <summary>How many additional ports beyond <see cref="DefaultPort"/> are tried in sequence.</summary>
    public const int DefaultPortScanRange = 4;
}
