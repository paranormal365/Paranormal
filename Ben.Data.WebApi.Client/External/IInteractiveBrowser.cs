namespace Ben.Data.WebApi.Client.External;

/// <summary>
/// Opens a sign-in page and waits for the provider to redirect back.
/// </summary>
/// <remarks>
/// <para>The one part of an external sign-in that cannot be pure: every platform hands the answer
/// back differently. Keeping it behind this interface means the handshake either side of it — the
/// proof key, the authorize URL, the state check, the code exchange — is ordinary testable code
/// rather than something only a person with a browser can exercise.</para>
///
/// <para>An implementation must use the system's own authentication session where it has one
/// (<c>ASWebAuthenticationSession</c>, and its equivalents), not an embedded web view. An embedded
/// view sees the password being typed, does not share the browser's sign-in state, and is refused
/// outright by some identity providers for exactly those reasons.</para>
/// </remarks>
public interface IInteractiveBrowser
{
    /// <summary>
    /// Shows <paramref name="authorizeUrl"/> and returns the redirect the provider finished with.
    /// </summary>
    /// <param name="callbackScheme">
    /// The scheme the platform should watch for, matching the redirect URI registered with the
    /// provider.
    /// </param>
    /// <returns>The redirect URI, or null when the person closed the window.</returns>
    /// <exception cref="InteractiveBrowserException">The browser could not be opened at all.</exception>
    Task<Uri?> AuthenticateAsync(Uri authorizeUrl, string callbackScheme, CancellationToken token = default);
}

/// <summary>The browser could not be shown, which is different from a sign-in being refused.</summary>
public sealed class InteractiveBrowserException : Exception
{
    public InteractiveBrowserException(string message, Exception? inner = null) : base(message, inner) { }
}
