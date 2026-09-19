using Microsoft.JSInterop;
using System.Text.Json;

namespace Ben.Wasm.Video.Services;

/// <summary>
/// Holds the WebApi bearer tokens for this browser session.
/// </summary>
/// <remarks>
/// <para>This is the WASM counterpart of the server host's <c>IWebApiTokenStore</c>: there, the
/// circuit keeps tokens in server memory and the browser never sees them. Here the browser <i>is</i>
/// the caller, so the token has to live with it.</para>
///
/// <para>Tokens live in memory first and are mirrored to <c>sessionStorage</c> so a reload —
/// something the editor does survive by design (project restore, OPFS cache) — does not silently
/// sign the user out. sessionStorage rather than localStorage on purpose: per-tab, gone when the
/// tab closes, never shared across sites. The honest trade: anything readable by page script is
/// readable by injected script. The editor already enforces a strict no-eval CSP posture
/// (post-#70 audit) which is the real mitigation; revisit storage if that posture ever weakens.</para>
/// </remarks>
public sealed class TokenStore
{
    private const string StorageKey = "bwv-auth";

    private readonly IJSRuntime _js;
    private Snapshot? _current;
    private bool _loaded;

    public TokenStore(IJSRuntime js) => _js = js;

    public event Action? Changed;

    public bool IsAuthenticated => _current is not null && !IsExpired(_current);

    /// <summary>The current access token, or null when signed out or expired past refresh.</summary>
    public async Task<string?> GetAccessTokenAsync()
    {
        await EnsureLoadedAsync();
        return _current?.AccessToken;
    }

    /// <summary>Whether the token this store holds has already expired.</summary>
    /// <remarks>
    /// The store knew and nothing asked. Every call after expiry therefore went out carrying a
    /// token the sender already knew was dead, collected a 401, refreshed and went again — two
    /// round trips where one would do, on a connection that may be somebody's phone
    /// (2026-09-05 audit, wasm-15).
    /// </remarks>
    public async Task<bool> IsAccessTokenExpiredAsync()
    {
        await EnsureLoadedAsync();
        return _current is not null && IsExpired(_current);
    }

    public async Task<string?> GetRefreshTokenAsync()
    {
        await EnsureLoadedAsync();
        return _current?.RefreshToken;
    }

    public async Task SetAsync(string accessToken, string refreshToken, int expiresInSeconds)
    {
        _current = new Snapshot(
            accessToken,
            refreshToken,
            // A minute of slack so a token is refreshed slightly early rather than used slightly late.
            DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, expiresInSeconds - 60)));
        _loaded = true;
        await _js.InvokeVoidAsync("sessionStorage.setItem", StorageKey, JsonSerializer.Serialize(_current));
        Changed?.Invoke();
    }

    public async Task ClearAsync()
    {
        _current = null;
        _loaded = true;
        await _js.InvokeVoidAsync("sessionStorage.removeItem", StorageKey);
        Changed?.Invoke();
    }

    private async Task EnsureLoadedAsync()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            var raw = await _js.InvokeAsync<string?>("sessionStorage.getItem", StorageKey);
            if (!string.IsNullOrEmpty(raw))
                _current = JsonSerializer.Deserialize<Snapshot>(raw);
        }
        catch
        {
            // Unreadable storage (private mode quirks, corrupted value) just means signed out.
            _current = null;
        }
    }

    private static bool IsExpired(Snapshot s) => DateTimeOffset.UtcNow >= s.ExpiresAtUtc;

    private sealed record Snapshot(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAtUtc);
}
