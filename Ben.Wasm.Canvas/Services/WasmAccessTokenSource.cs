using Ben.Canvas.Editor.Services;

namespace Ben.Wasm.Canvas.Services;

/// <summary>
/// Hands the editor the session's token for the requests the browser makes itself (pictures and uploads), the
/// same way <see cref="BearerTokenHandler"/> does for HttpClient: refreshed first when it has expired.
/// </summary>
public sealed class WasmAccessTokenSource(TokenStore tokens, AuthService auth) : ICanvasAccessTokenSource
{
    public async Task<string?> GetAccessTokenAsync(bool forceRefresh = false, CancellationToken ct = default)
    {
        if (forceRefresh || await tokens.IsAccessTokenExpiredAsync())
            await auth.TryRefreshAsync(ct);
        return await tokens.GetAccessTokenAsync();
    }
}
