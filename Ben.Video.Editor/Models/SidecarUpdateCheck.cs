namespace Ben.Video.Editor.Models;

/// <summary>
/// Whether the sidecar on this machine is older than the one the site is handing out.
/// </summary>
/// <remarks>
/// <para>There is no auto-updater and no update feed: the sidecar is a desktop app somebody
/// installs once from a .dmg or .exe, and nothing has ever told them a newer one exists. An install
/// from before 2026-09-19 watches the whole filesystem and pins a CPU core for as long as it is up,
/// and would go on doing that for ever. The editor already learns the installed version from
/// <c>/v1/health</c> and already knows where the download lives, so comparing the two is the whole
/// of a usable answer without signing, a feed, or anything running in the background.</para>
///
/// <para><b>Silence unless certain.</b> Every uncertain case answers "no". A version that will not
/// parse, a host that publishes none, a build NEWER than the published one — which is every
/// developer running from source — must not be nagged. A false "you are out of date" is worse than
/// saying nothing: it sends somebody to reinstall what they already have, and it teaches them to
/// ignore the notice that will one day be true.</para>
/// </remarks>
public static class SidecarUpdateCheck
{
    /// <summary>
    /// True only when both versions are understood and the installed one is genuinely older.
    /// </summary>
    /// <param name="installedVersion">What <c>/v1/health</c> reported, e.g. "1.0.0.0".</param>
    /// <param name="publishedVersion">What this host says it is handing out. Null = don't ask.</param>
    public static bool IsOutOfDate(string? installedVersion, string? publishedVersion)
    {
        if (!Version.TryParse(installedVersion?.Trim(), out var installed)) return false;
        if (!Version.TryParse(publishedVersion?.Trim(), out var published)) return false;

        return installed < published;
    }
}
