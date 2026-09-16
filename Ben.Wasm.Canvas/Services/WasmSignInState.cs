using Ben.Canvas.Editor.Services;

namespace Ben.Wasm.Canvas.Services;

/// <summary>
/// Tells the editor whether this browser is signed in, and what to call the person.
/// </summary>
/// <remarks>
/// Without it the editor could only ask whether a server was configured, and would offer Save to case
/// to somebody who was signed out - a button that could only ever answer 401. The API returns no
/// display name, so the email /api/me returned is what is shown.
/// </remarks>
public sealed class WasmSignInState(TokenStore tokens, AccountInfoService account) : ICanvasSignInState
{
    /// <inheritdoc />
    public bool IsSignedIn => tokens.IsAuthenticated;

    /// <inheritdoc />
    public string? DisplayName => tokens.IsAuthenticated ? account.Email : null;

    /// <inheritdoc />
    public event Action? Changed
    {
        add => tokens.Changed += value;
        remove => tokens.Changed -= value;
    }
}
