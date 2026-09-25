using System.Globalization;

namespace Ben.Web.Website.Services;

/// <summary>What a map picture request asks for, once it has passed the checks.</summary>
public sealed record MapKitSnapshotAsk(double Latitude, double Longitude, int Zoom, int Width, int Height, string ColorScheme);

/// <summary>
/// Reads and checks a request to <c>/auth/mapkit-snapshot</c> (canvas plan R35).
/// </summary>
/// <remarks>
/// <para><b>Only our own pages.</b> A signed snapshot spends our Apple quota (25,000 a day), so the picture is
/// signed only for a request whose <c>Referer</c> is this site or an allow-listed development origin
/// (<c>Maps:AllowedTokenOrigins</c>, the same list the token endpoint uses). Browsers send the page's origin as
/// the referrer for an image under the site's <c>strict-origin-when-cross-origin</c> policy.</para>
///
/// <para><b>Apple's limits, checked here.</b> Coordinates on the globe, zoom 3 to 20, sizes 50 to 640, and a light
/// or dark scheme; anything else is refused before anything is signed.</para>
/// </remarks>
public static class MapKitSnapshotRequest
{
    /// <summary>The checked request, or null when it should be answered 404.</summary>
    /// <param name="requestOrigin">The request's own <c>scheme://host</c>.</param>
    /// <param name="referer">The request's <c>Referer</c> header.</param>
    /// <param name="allowed"><c>Maps:AllowedTokenOrigins</c>.</param>
    public static MapKitSnapshotAsk? Read(
        string? lat, string? lng, string? z, string? w, string? h, string? scheme,
        string requestOrigin, string? referer, IReadOnlyCollection<string> allowed)
    {
        if (!FromUs(requestOrigin, referer, allowed)) return null;

        if (!double.TryParse(lat, NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude) || latitude is < -90 or > 90) return null;
        if (!double.TryParse(lng, NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude) || longitude is < -180 or > 180) return null;
        if (!int.TryParse(z, NumberStyles.Integer, CultureInfo.InvariantCulture, out var zoom) || zoom is < 3 or > 20) return null;
        if (!int.TryParse(w, NumberStyles.Integer, CultureInfo.InvariantCulture, out var width) || width is < 50 or > 640) return null;
        if (!int.TryParse(h, NumberStyles.Integer, CultureInfo.InvariantCulture, out var height) || height is < 50 or > 640) return null;
        if (scheme is not ("light" or "dark")) return null;

        return new MapKitSnapshotAsk(latitude, longitude, zoom, width, height, scheme);
    }

    private static bool FromUs(string requestOrigin, string? referer, IReadOnlyCollection<string> allowed)
    {
        if (string.IsNullOrWhiteSpace(referer) || !Uri.TryCreate(referer, UriKind.Absolute, out var uri)) return false;
        var refererOrigin = uri.GetLeftPart(UriPartial.Authority);
        if (string.Equals(refererOrigin, requestOrigin, StringComparison.OrdinalIgnoreCase)) return true;
        return MapKitTokenOrigin.Resolve(requestOrigin, refererOrigin, allowed) is not null;
    }
}
