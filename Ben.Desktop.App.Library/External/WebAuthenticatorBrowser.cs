using Ben.Data.WebApi.Client.External;

namespace Ben.Desktop.App.Library.External;

/// <summary>
/// Opens a provider's sign-in page using the operating system's own authentication session.
/// </summary>
/// <remarks>
/// <para>On Mac Catalyst this is <c>ASWebAuthenticationSession</c>: a real Safari view that shares
/// the browser's existing sign-in state, keeps the password out of this process entirely, and
/// hands the redirect back without any listener. An embedded web view would do none of those
/// things, and several identity providers refuse one outright for exactly that reason.</para>
///
/// <para><b>Windows is not verified.</b> MAUI's WebAuthenticator on Windows wants the app to be
/// packaged so the operating system can route the callback back to it, and this app is currently
/// unpackaged. Anyone building the Windows head should expect to either package it or replace this
/// with a loopback redirect there. It is written rather than stubbed so the shape is obvious, not
/// because it has been seen to work.</para>
/// </remarks>
public sealed class WebAuthenticatorBrowser : IInteractiveBrowser
{
    public async Task<Uri?> AuthenticateAsync(
        Uri authorizeUrl, string callbackScheme, CancellationToken token = default)
    {
        try
        {
            var result = await WebAuthenticator.Default.AuthenticateAsync(new WebAuthenticatorOptions
            {
                Url = authorizeUrl,
                CallbackUrl = new Uri($"{callbackScheme}://auth"),

                // Ask for a private session so a previous account is not silently reused. Somebody
                // signing in on a shared machine should be asked who they are, not handed whoever
                // was here last.
                PrefersEphemeralWebBrowserSession = true,
            });

            return Rebuild(callbackScheme, result);
        }
        catch (TaskCanceledException)
        {
            // They closed the window. A decision, not a failure.
            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (FeatureNotSupportedException ex)
        {
            throw new InteractiveBrowserException(
                "This build can't open a sign-in window. Sign in with an email address and password instead.", ex);
        }
        catch (Exception ex)
        {
            throw new InteractiveBrowserException("The sign-in window couldn't be opened.", ex);
        }
    }

    /// <summary>
    /// Puts the parsed result back together as a URI.
    /// </summary>
    /// <remarks>
    /// WebAuthenticator hands back a dictionary of parsed values rather than the redirect itself,
    /// but everything downstream — including the state check that stops a forged redirect being
    /// accepted — reads a URI. Rebuilding here keeps that one code path rather than growing a
    /// second one that only this platform uses and only this platform can test.
    /// </remarks>
    internal static Uri Rebuild(string callbackScheme, WebAuthenticatorResult result)
    {
        var query = System.Web.HttpUtility.ParseQueryString(string.Empty);
        foreach (var pair in result.Properties)
            query[pair.Key] = pair.Value;

        return new Uri($"{callbackScheme}://auth?{query}");
    }
}
