namespace Ben.Web.Website.Services;

/// <summary>
/// Decides whether a request arrived on a name the site should move it off.
/// </summary>
/// <remarks>
/// <para><c>www.ishaunted.com</c> serves this same site rather than redirecting to it, so a person
/// can be on either name and nothing tells them which. That is not merely untidy. Sign in with
/// Apple posts its answer back to ONE registered return URL, on the bare name, and the state
/// cookie set on <c>www</c> is not sent to the bare name — so a sign-in begun on <c>www</c> fails
/// its state check and the screen can say nothing useful about why.</para>
///
/// <para>The rule is derived from the host that arrived rather than configured: strip a leading
/// <c>www.</c>, leave everything else alone. That is right in every environment and cannot misfire
/// on <c>localhost</c>, which no configured value could promise.</para>
///
/// <para><b><c>/.well-known/</c> is deliberately never redirected.</b> Domain verification — Apple's
/// today, a certificate authority's tomorrow — fetches that path on the exact host it is checking
/// and does not follow redirects. Redirecting it would make <c>www</c> permanently unverifiable,
/// and the portal reports that as a mismatch rather than as a redirect.</para>
/// </remarks>
public static class CanonicalHost
{
    private const string Prefix = "www.";

    /// <summary>The host this request belongs on, or <c>null</c> when it is already there.</summary>
    public static string? ApexFor(string? host, string? path)
    {
        if (string.IsNullOrWhiteSpace(host)) return null;
        if (IsVerificationPath(path)) return null;
        if (!host.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return null;

        var apex = host[Prefix.Length..];

        // "www." on its own is not a name, and redirecting to an empty host would send the
        // browser somewhere that cannot exist.
        return apex.Length == 0 ? null : apex;
    }

    private static bool IsVerificationPath(string? path)
        => path is not null
           && (path.Equals("/.well-known", StringComparison.OrdinalIgnoreCase)
               || path.StartsWith("/.well-known/", StringComparison.OrdinalIgnoreCase));
}
