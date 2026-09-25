namespace Ben.Canvas.Editor.Services;

/// <summary>
/// Whether the person using the editor is signed in to whatever the host talks to.
/// </summary>
/// <remarks>
/// <para>The editor has no idea who is signed in and no business guessing. Hosts differ: the site
/// holds a token in its circuit, the standalone editor holds one in the browser, and a host with no
/// server at all has no answer to give.</para>
///
/// <para>Optional by design, and never registered by the library: it is resolved with
/// <c>GetService</c>. Without one the editor falls back to asking whether a server is configured -
/// the gap that once let the video editor offer Save to Server to somebody who was signed out.</para>
/// </remarks>
public interface ICanvasSignInState
{
    /// <summary>True when server-backed actions can be expected to work.</summary>
    bool IsSignedIn { get; }

    /// <summary>
    /// What to call the signed-in person, or null when signed out or not yet known. The API returns
    /// no display name, so hosts usually give the account's email address.
    /// </summary>
    string? DisplayName { get; }

    /// <summary>Raised when the person signs in or out, so the editor can open the case's board then.</summary>
    /// <remarks>A host that cannot tell leaves the default, which never raises.</remarks>
    event Action? Changed { add { } remove { } }
}
