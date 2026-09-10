namespace Ben.Data.WebApi.Client.External;

/// <summary>
/// What this client needs to know to sign somebody in with a Microsoft work or school account.
/// </summary>
/// <param name="Authority">
/// The Entra endpoint. <c>common</c> accepts any tenant plus personal accounts, and matches what
/// the API validates: its Entra scheme is configured against
/// <c>login.microsoftonline.com/common/v2.0</c>.
/// </param>
/// <param name="Scope">
/// The API scope, not a Graph scope. It must be the one the API's audience check accepts, or the
/// token comes back looking perfectly valid and is refused with a 401 that names nothing.
/// </param>
/// <param name="RedirectUri">
/// Where the browser hands the code back. Must ALSO be registered on the app registration in the
/// Entra portal, under a "Mobile and desktop applications" platform, with public client flows
/// enabled — an unregistered redirect is refused by Entra before the person even sees a password
/// box.
/// </param>
public sealed record EntraOptions(
    string ClientId,
    string Scope,
    string RedirectUri,
    string Authority = "https://login.microsoftonline.com/common/v2.0")
{
    /// <summary>Whether enough is configured to attempt a sign-in at all.</summary>
    /// <remarks>
    /// The API only enables its Entra scheme when its own <c>AzureAd:ClientId</c> is set, so a
    /// client with nothing configured should hide the button rather than offer one that cannot
    /// work. A door that leads nowhere is worse than no door.
    /// </remarks>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(Scope)
        && !string.IsNullOrWhiteSpace(RedirectUri);

    public Uri AuthorizeEndpoint => new($"{Authority.TrimEnd('/')}/authorize");
    public Uri TokenEndpoint => new($"{Authority.TrimEnd('/')}/token");
}
