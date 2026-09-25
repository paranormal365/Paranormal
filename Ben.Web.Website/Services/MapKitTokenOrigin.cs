namespace Ben.Web.Website.Services;

/// <summary>
/// Which origin the MapKit token endpoint signs for: the request's own, or an allow-listed
/// development origin asked for with <c>?origin=</c>.
/// </summary>
/// <remarks>
/// <para><b>Why an override exists.</b> A MapKit JS token names one origin and Apple refuses it on any
/// other. In production the canvas editor at <c>/editors/canvas/</c> shares the site's origin and
/// never uses this. In development the canvas runs on <c>http://localhost:5125</c> and the website on
/// 5078, so a token for the request's origin draws no tiles on the canvas and a developer sees only
/// the address card (canvas plan M6-12).</para>
///
/// <para><b>Why an allow list.</b> A token endpoint that signed for whatever <c>?origin=</c> it was
/// sent would let any website on the internet draw Apple Maps on our key and our quota.
/// <c>Maps:AllowedTokenOrigins</c> is empty in production, so there every override is refused; the
/// untracked development settings name the local canvas only.</para>
///
/// <para><b>Exact origins only.</b> The requested value must parse as an absolute http or https URL
/// with nothing after the authority — no path, query or fragment — and must match an allow-listed
/// entry ignoring case and a trailing slash. Anything else answers null, and the endpoint answers 404.</para>
/// </remarks>
public static class MapKitTokenOrigin
{
    /// <summary>
    /// The origin to sign for, or null when <paramref name="requestedOrigin"/> is set and not allowed.
    /// </summary>
    /// <param name="requestOrigin">The request's own <c>scheme://host</c>.</param>
    /// <param name="requestedOrigin">The <c>?origin=</c> value; blank means "the request's own".</param>
    /// <param name="allowed"><c>Maps:AllowedTokenOrigins</c>.</param>
    public static string? Resolve(string requestOrigin, string? requestedOrigin, IReadOnlyCollection<string> allowed)
    {
        if (string.IsNullOrWhiteSpace(requestedOrigin)) return requestOrigin;

        var candidate = Canonical(requestedOrigin);
        if (candidate is null) return null;

        foreach (var entry in allowed ?? [])
        {
            if (Canonical(entry) is { } allowedOrigin
                && string.Equals(allowedOrigin, candidate, StringComparison.OrdinalIgnoreCase))
                return allowedOrigin;
        }
        return null;
    }

    /// <summary><c>scheme://host[:port]</c> for an origin-shaped value, or null.</summary>
    private static string? Canonical(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim().TrimEnd('/');
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme is not ("http" or "https")) return null;
        if (uri.PathAndQuery != "/" || !string.IsNullOrEmpty(uri.Fragment) || !string.IsNullOrEmpty(uri.UserInfo)) return null;
        return uri.GetLeftPart(UriPartial.Authority);
    }
}
