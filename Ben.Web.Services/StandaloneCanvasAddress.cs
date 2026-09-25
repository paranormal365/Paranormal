namespace Ben.Web.Services;

/// <summary>
/// Where the standalone case canvas editor lives, which is not the same address everywhere.
/// </summary>
/// <remarks>
/// <para>The sibling of <see cref="StandaloneEditorAddress"/>, for the same reason. The canvas is a
/// published WebAssembly app that IIS mounts at <c>/editors/canvas/</c>; on a development machine
/// nothing serves that path, and a link typed as the literal would be a 404 on exactly the machines
/// where somebody is working on the canvas. The video editor's "Standalone editor" link was that
/// 404 for months (V-6 of the 2026-09-06 evaluation) before its address became a fact read from
/// the environment.</para>
///
/// <para>Configuration wins where it is set (<c>CanvasEditor:StandaloneUrl</c>) — that is how any
/// other layout says where its own mount is — then development gets the canvas dev server on 5125,
/// and everything else gets the production mount path. Consumers arrive with the Canvas tab on a
/// case (plan M8) and the help text.</para>
/// </remarks>
public static class StandaloneCanvasAddress
{
    /// <summary>Where IIS mounts the published canvas editor.</summary>
    public const string MountPath = "/editors/canvas/";

    /// <summary>The canvas host's WebAssembly dev server (<c>dotnet run --project Ben.Wasm.Canvas</c>).</summary>
    /// <remarks>
    /// Absolute, and on its own port, because in development the canvas is a separate host rather
    /// than a folder under this one. The API's development CORS list names this origin.
    /// </remarks>
    public const string DevelopmentUrl = "http://localhost:5125/";

    /// <summary>The address to link to, always ending in a slash.</summary>
    /// <param name="isDevelopment">Whether this host is running in the Development environment.</param>
    /// <param name="configured">
    /// <c>CanvasEditor:StandaloneUrl</c>, when a deployment sets it. Blank counts as unset.
    /// </param>
    /// <remarks>
    /// The trailing slash is load-bearing: a handoff appends <c>#handoff=…</c>, and
    /// <c>/editors/canvas#handoff=…</c> does not resolve to the app while <c>/editors/canvas/#…</c> does.
    /// </remarks>
    public static string For(bool isDevelopment, string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
            return configured.Trim().TrimEnd('/') + "/";

        return isDevelopment ? DevelopmentUrl : MountPath;
    }
}
