using Ben.Video.Editor.Services;

namespace Ben.Wasm.Video.Services;

/// <summary>
/// Tells the editor whether this browser is signed in.
/// </summary>
/// <remarks>
/// Without it the editor could only ask whether a server was configured, and offered Save to
/// Server to somebody who was signed out — a button that could only ever answer 401 (2026-09-05
/// audit, F13's other half).
/// </remarks>
public sealed class WasmSignInState(TokenStore tokens) : IEditorSignInState
{
    public bool IsSignedIn => tokens.IsAuthenticated;
}
