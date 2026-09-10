using Ben.Data.WebApi.Client.External;

namespace Ben.Desktop.App.UI;

/// <summary>
/// Where the Microsoft sign-in settings come from.
/// </summary>
/// <remarks>
/// <para>None of these are secrets. A desktop app is a public client: it cannot keep a client
/// secret, which is exactly why the sign-in uses a proof key instead of one. The client id and
/// scope below are the same public identifiers the website already carries in its own settings.</para>
///
/// <para>Left as constants rather than read from a file because a MAUI app has no appsettings to
/// read at run time, and because changing them means changing what is registered in the Entra
/// portal too — they are not independently tunable.</para>
/// </remarks>
public static class EntraSettings
{
    /// <summary>Matches the registration the website already uses.</summary>
    private const string ClientId = "3e37e6d7-13ea-4b94-b271-618267256d8b";

    /// <summary>
    /// The API's own scope, not a Graph one.
    /// </summary>
    /// <remarks>
    /// The API accepts an audience of <c>api://{clientId}</c> or the bare client id. Asking for a
    /// Graph scope instead returns a token that looks perfectly valid and is refused by our API
    /// with a 401 that names nothing.
    /// </remarks>
    private const string Scope = "api://3e37e6d7-13ea-4b94-b271-618267256d8b/access_as_user";

    /// <summary>
    /// Must also be registered in the Entra portal under "Mobile and desktop applications", with
    /// public client flows enabled, and must match the URL scheme in the platform manifest.
    /// </summary>
    private const string RedirectUri = "msauth.com.ishaunted.desktop://auth";

    public static EntraOptions FromConfiguration() => new(ClientId, Scope, RedirectUri);
}
