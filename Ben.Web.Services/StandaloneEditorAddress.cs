namespace Ben.Web.Services;

/// <summary>
/// Where the standalone video editor lives, which is not the same address everywhere.
/// </summary>
/// <remarks>
/// <para><b>V-6 of the 2026-09-06 evaluation.</b> "Standalone editor" on <c>/my-videos</c> links to
/// <c>/editors/video/</c>, a path that exists only because IIS mounts the published WebAssembly
/// app there. On a development machine nothing serves it and the link is a 404 — so the one route
/// into the editor that the site offers is broken for everybody working on the site, and the
/// evaluation had to drive the phase-12 handoff by hand against <c>:5180</c> instead.</para>
///
/// <para>The address is a fact about the environment, so it is read from one rather than typed
/// into a page. Configuration wins where it is set — that is how UAT or any other layout says
/// where its own mount is — and otherwise development gets the WebAssembly dev server and
/// everything else gets the mount path that production really has.</para>
///
/// <para>The alternative the plan offered was hiding the button when the mount is absent. That
/// trades a broken link for a missing feature and tells nobody anything, and it would have hidden
/// the editor on exactly the machines where somebody is trying to work on it.</para>
/// </remarks>
public static class StandaloneEditorAddress
{
    /// <summary>Where IIS mounts the published editor.</summary>
    public const string MountPath = "/editors/video/";

    /// <summary>
    /// The WebAssembly dev server, which is what `scripts/run-e2e.sh` and `dotnet run` start.
    /// </summary>
    /// <remarks>
    /// Absolute, and on a different port, because in development the editor is a separate host
    /// rather than a folder under this one. The site's CORS list already names this origin.
    /// </remarks>
    public const string DevelopmentUrl = "http://localhost:5180/";

    /// <summary>The address to link to, always ending in a slash.</summary>
    /// <param name="isDevelopment">Whether this host is running in the Development environment.</param>
    /// <param name="configured">
    /// <c>VideoEditor:StandaloneUrl</c>, when a deployment sets it. Blank counts as unset — an
    /// empty configuration key is somebody who has not decided, not somebody who chose "".
    /// </param>
    public static string For(bool isDevelopment, string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
            return configured.TrimEnd('/') + "/";

        return isDevelopment ? DevelopmentUrl : MountPath;
    }
}
