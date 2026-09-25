namespace Ben.Video.Core.SidecarContracts;

/// <summary>
/// The sidecar version this source tree builds, and therefore the one a site deployed from it is
/// handing out.
/// </summary>
/// <remarks>
/// <para>Two things have to agree for the editor's "a newer sidecar is available" notice to be
/// true rather than merely plausible: the version the SIDECAR reports from <c>/v1/health</c>, which
/// is its assembly version, and the version the HOST advertises as current. They are set in
/// different kinds of file — an MSBuild property and a C# constant — so a guard test compares them
/// (<c>SidecarReleaseVersionTests</c>). Bumping one and forgetting the other would either nag
/// everybody for ever or nobody at all, and both failures are silent.</para>
///
/// <para><b>Bump this when you publish a new build, not when you change the code.</b> The notice
/// sends people to the downloads page, so it must not claim a version the page is not yet serving.
/// The deploy runbook carries the order: build the artifacts, upload them, then deploy the site
/// that advertises them.</para>
///
/// <para>History: 1.1.0 is the first build worth telling anybody about. 1.0.0 resolved its content
/// root to "/" under launchd and put a recursive file watch over the entire filesystem, pinning a
/// CPU core for as long as it ran and growing to 2.9 GB; it also ran from login until shutdown
/// whether or not anyone opened the editor. Nothing would ever have told those installs, because
/// until now nothing compared versions at all.</para>
///
/// <para>1.1.3: on Windows it no longer opens a console window, which stopped the sidecar when
/// somebody closed it; a new install shows a small window with its pairing code instead. WebM clips
/// stopped reading as 0 seconds long on every platform.</para>
/// </remarks>
public static class SidecarRelease
{
    /// <summary>Kept identical to &lt;Version&gt; in Ben.Video.Sidecar.csproj.</summary>
    public const string Version = "1.1.3";
}
