using Ben.Canvas.Core.Options;

namespace Ben.Canvas.Editor.Extensions;

/// <summary>
/// The one place that says what a full canvas editor is, so every host is the same editor.
/// </summary>
/// <remarks>
/// Split in two, as the video editor's host defaults are: editing has nothing to do with whether a
/// server is reachable, so <see cref="ApplyEditingDefaults"/> is applied unconditionally and
/// <see cref="ApplyServerIntegration"/> only turns server features on when there is an API.
/// </remarks>
public static class CanvasEditorHostDefaults
{
    /// <summary>
    /// Turns on everything a person can do on their own machine.
    /// </summary>
    /// <remarks>M1 sets the block and editing defaults here.</remarks>
    public static void ApplyEditingDefaults(CanvasEditorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
    }

    /// <summary>
    /// Points the editor at a Web API and turns on saving, link previews and publishing.
    /// </summary>
    /// <param name="options">The options being configured.</param>
    /// <param name="apiBaseUrl">The API's base address. Null or blank leaves a local-only editor, which is valid.</param>
    /// <param name="mapTokenUrl">Where MapKit tokens are issued; blank means no maps.</param>
    /// <param name="siteBaseUrl">The site's address, for links back into it.</param>
    public static void ApplyServerIntegration(
        CanvasEditorOptions options, string? apiBaseUrl, string? mapTokenUrl = null, string? siteBaseUrl = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(apiBaseUrl)) return;

        options.ApiBaseUrl = apiBaseUrl.Trim().TrimEnd('/');
        options.ServerSave = true;
        options.LinkUnfurl = true;
        options.Publish = true;
        options.MapTokenUrl = string.IsNullOrWhiteSpace(mapTokenUrl) ? null : mapTokenUrl.Trim();
        options.MapSnapshotUrl = SnapshotUrlBeside(options.MapTokenUrl);
        options.SiteBaseUrl = string.IsNullOrWhiteSpace(siteBaseUrl) ? null : siteBaseUrl.Trim().TrimEnd('/');
    }

    /// <summary>
    /// The website signs map pictures beside its map tokens, so <c>…/auth/mapkit-token?origin=…</c> gives
    /// <c>…/auth/mapkit-snapshot</c>. Any other token address has no known picture address.
    /// </summary>
    public static string? SnapshotUrlBeside(string? mapTokenUrl)
    {
        if (string.IsNullOrWhiteSpace(mapTokenUrl)) return null;
        var path = mapTokenUrl.Split('?', 2)[0].TrimEnd('/');
        const string token = "/auth/mapkit-token";
        return path.EndsWith(token, StringComparison.OrdinalIgnoreCase) ? path[..^token.Length] + "/auth/mapkit-snapshot" : null;
    }
}
