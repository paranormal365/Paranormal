namespace Ben.Video.Editor.Services;

/// <summary>
/// Whether the person using the editor is signed in to whatever the host talks to.
/// </summary>
/// <remarks>
/// <para>The editor has no idea who is signed in and no business guessing. Hosts differ: the site
/// holds a token in the circuit, the standalone editor holds one in the browser, and a host with
/// no server at all has no answer to give.</para>
///
/// <para>Optional by design. Without one the editor falls back to asking whether a server is
/// configured, which is what it did before — a reasonable answer for a host that has no accounts,
/// and the wrong one for a host that does. That gap is why the standalone editor offered Save to
/// Server to somebody who was signed out (2026-09-05 audit, F13's other half).</para>
/// </remarks>
public interface IEditorSignInState
{
    /// <summary>True when server-backed actions can be expected to work.</summary>
    bool IsSignedIn { get; }
}
