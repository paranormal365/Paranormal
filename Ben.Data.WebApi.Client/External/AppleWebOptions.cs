namespace Ben.Data.WebApi.Client.External;

/// <summary>
/// What a WEBSITE needs to sign somebody in with Apple. Not what an app needs.
/// </summary>
/// <param name="ServicesId">
/// Apple's identifier for a website, configured against the App ID, and NOT a bundle id.
/// </param>
/// <param name="RedirectUri">
/// Where Apple posts the answer. Must be <c>https</c> and registered against the Services ID.
/// </param>
/// <remarks>
/// <para><b>The Services ID becomes the token's audience</b>, so it also has to appear in the
/// server's <c>Apple:ClientIds</c>. Without that, a perfectly valid token is refused with a 401
/// that names nothing. The app bundle ids already in that list will not do.</para>
///
/// <para><b>Apple refuses a plain-http or localhost redirect</b>, which means this flow cannot be
/// exercised on a development machine the way the rest of the site can. Everything either side of
/// the redirect is written to be testable without it, because otherwise none of it would be tested
/// at all.</para>
/// </remarks>
public sealed record AppleWebOptions(string ServicesId, string RedirectUri)
{
    public static readonly Uri AuthorizeEndpoint = new("https://appleid.apple.com/auth/authorize");

    /// <summary>Whether to offer the button. An unconfigured site should show nothing.</summary>
    /// <remarks>
    /// A button that cannot work reads as a broken feature rather than an absent one, and this ships
    /// unconfigured — the Services ID does not exist until somebody creates it in Apple's portal.
    /// </remarks>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ServicesId)
        && !string.IsNullOrWhiteSpace(RedirectUri)
        && Uri.TryCreate(RedirectUri, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps;
}
