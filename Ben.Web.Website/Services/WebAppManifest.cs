using System.Text.Json.Serialization;
using Ben.Data.Common;

namespace Ben.Web.Website.Services;

/// <summary>
/// The web app manifest — what a browser needs to install the site to a home screen (item 209).
/// </summary>
/// <remarks>
/// <para><b>Why a manifest when there is a native app.</b> The native app is iPhone and iPad only.
/// Everybody else — Android, Windows, a Mac — gets nothing installable at all without this, and
/// "add to home screen" on those platforms produces a bookmark that opens in a browser tab with
/// its address bar, rather than something that looks like an app. The manifest is what turns the
/// second into the first, and it costs one file.</para>
///
/// <para><b>It does not compete with the app on iOS.</b> Safari on iOS reads the manifest for
/// home-screen installs, but a phone with the app installed follows the universal link to the app
/// first — see <see cref="AppleAppSiteAssociation"/>. The two answer different questions: which
/// app opens a link, and what happens when somebody deliberately installs the site.</para>
///
/// <para><b>Name and colours are not literals.</b> <see cref="SiteIdentity"/> exists because the
/// domain is not settled, and a name baked into a manifest is exactly the kind of thing that gets
/// missed on a rename — it is read by an installer, not by a page, so nobody sees it go stale. The
/// background matches the Night theme's <c>--bs-body-bg</c>; a mismatch shows as a flash of the
/// wrong colour on every launch.</para>
/// </remarks>
public static class WebAppManifest
{
    /// <summary>The Night theme's body background. Kept in step with night.min.css by a test.</summary>
    public const string BackgroundColor = "#212529";

    /// <summary>What the browser paints its own chrome with while the site is open.</summary>
    public const string ThemeColor = "#212529";

    public static ManifestDocument For(SiteIdentity site) =>
        new(
            Name: site.Name,
            // Home screens truncate at roughly a dozen characters, so the short name drops the
            // domain suffix rather than being cut mid-word by the launcher.
            ShortName: ShortNameFor(site.Name),
            Description: site.Tagline,
            // Root, not the feed: somebody who installed the site should land where a visitor
            // lands, and the home page decides for itself whether that is a desk or a front door.
            StartUrl: "/",
            Scope: "/",
            // standalone, not fullscreen: this is a site with navigation, and fullscreen takes the
            // status bar with it — no clock and no battery on a phone somebody is holding in a
            // dark building for three hours.
            Display: "standalone",
            Orientation: "any",
            BackgroundColor: BackgroundColor,
            ThemeColor: ThemeColor,
            Icons:
            [
                // "any" and "maskable" are declared on the same files deliberately. Android crops
                // a non-maskable icon into its shape and can clip the ghost's head; declaring
                // maskable tells the launcher the art already tolerates a safe zone.
                new ManifestIcon("/icon-192.png", "192x192", "image/png", "any maskable"),
                new ManifestIcon("/icon-512.png", "512x512", "image/png", "any maskable"),
                new ManifestIcon("/apple-touch-icon.png", "180x180", "image/png", "any"),
            ]);

    /// <summary>
    /// The launcher-sized name.
    /// </summary>
    /// <remarks>
    /// <para>Drops a trailing domain suffix — "IsHaunted.com" becomes "IsHaunted" — because a home
    /// screen shows about twelve characters and ".com" is the least useful of them.</para>
    ///
    /// <para><b>The test is whether the whole name looks like a domain, not how long the suffix
    /// is.</b> A length cap was the first attempt and it fails on every modern top-level domain:
    /// ".paranormal" and ".investigations" both exist, and a site named on one of them would keep
    /// a suffix while "IsHaunted.com" lost its. Whitespace is the reliable signal — a domain has
    /// none, and "Paranormal Investigations Ltd." has both a trailing dot and spaces.</para>
    ///
    /// <para>Never trimmed by length: a launcher truncates a long name with an ellipsis, and
    /// cutting it here would instead produce a name that is wrong rather than merely clipped.</para>
    /// </remarks>
    public static string ShortNameFor(string name)
    {
        // Anything with a space is a name somebody wrote, not an address.
        if (name.Any(char.IsWhiteSpace)) return name;

        var lastDot = name.LastIndexOf('.');
        if (lastDot <= 0) return name;

        var suffix = name[(lastDot + 1)..];
        // A trailing dot leaves nothing to call a suffix; a suffix with digits in it is a version
        // number or a build tag rather than a domain.
        return suffix.Length > 0 && suffix.All(char.IsLetter) ? name[..lastDot] : name;
    }

    // ── the document's shape, as the W3C defines it ───────────────────────────

    public sealed record ManifestDocument(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("short_name")] string ShortName,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("start_url")] string StartUrl,
        [property: JsonPropertyName("scope")] string Scope,
        [property: JsonPropertyName("display")] string Display,
        [property: JsonPropertyName("orientation")] string Orientation,
        [property: JsonPropertyName("background_color")] string BackgroundColor,
        [property: JsonPropertyName("theme_color")] string ThemeColor,
        [property: JsonPropertyName("icons")] IReadOnlyList<ManifestIcon> Icons);

    public sealed record ManifestIcon(
        [property: JsonPropertyName("src")] string Src,
        [property: JsonPropertyName("sizes")] string Sizes,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("purpose")] string Purpose);
}
