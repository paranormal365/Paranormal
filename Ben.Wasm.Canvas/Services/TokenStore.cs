using Microsoft.JSInterop;
using System.Text.Json;

namespace Ben.Wasm.Canvas.Services;

// Copied from Ben.Wasm.Video/Services/TokenStore.cs. Differences: the storage key, and it is
// registered as a singleton (see Program.cs) so the HttpClientFactory handler and the app share one
// instance.

/// <summary>
/// Holds the Web API bearer tokens for this browser session.
/// </summary>
/// <remarks>
/// <para>The WebAssembly counterpart of the site's circuit-held token store: there the server keeps
/// tokens and the browser never sees them; here the browser <i>is</i> the caller, so the token has
/// to live with it.</para>
///
/// <para>Tokens live in memory first and are mirrored to <c>sessionStorage</c> so a reload does not
/// silently sign the person out. sessionStorage rather than localStorage on purpose: per tab, gone
/// when the tab closes. The key differs from the video editor's (<c>bwv-auth</c>) so the two editors
/// never overwrite each other's session in one tab.</para>
/// </remarks>
public sealed class TokenStore
{
    private const string StorageKey = "bwc-auth";

    private readonly IJSRuntime _js;
    private Snapshot? _current;
    private bool _loaded;

    public TokenStore(IJSRuntime js) => _js = js;

    /// <summary>Raised when the person signs in, refreshes or signs out.</summary>
    public event Action? Changed;

    /// <summary>True while an unexpired token is held.</summary>
    public bool IsAuthenticated => _current is not null && !IsExpired(_current);

    /// <summary>The current access token, or null when signed out.</summary>
    public async Task<string?> GetAccessTokenAsync()
    {
        await EnsureLoadedAsync();
        return _current?.AccessToken;
    }

    /// <summary>Whether the token this store holds has already expired.</summary>
    /// <remarks>
    /// Asked before sending, so a call after expiry refreshes first instead of collecting a 401 and
    /// going again - two round trips where one would do, on a connection that may be a phone.
    /// </remarks>
    public async Task<bool> IsAccessTokenExpiredAsync()
    {
        await EnsureLoadedAsync();
        return _current is not null && IsExpired(_current);
    }

    /// <summary>The refresh token, or null when signed out.</summary>
    public async Task<string?> GetRefreshTokenAsync()
    {
        await EnsureLoadedAsync();
        return _current?.RefreshToken;
    }

    /// <summary>Stores new tokens.</summary>
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

    /// <summary>Forgets the tokens.</summary>
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
            // Unreadable storage (private mode quirks, a corrupted value) just means signed out.
            _current = null;
        }

        // Anything drawn before this first read (the sign-in chip, the editor deciding whether to open the case's
        // board) showed "signed out"; tell it the answer is in.
        if (_current is not null) Changed?.Invoke();
    }

    private static bool IsExpired(Snapshot s) => DateTimeOffset.UtcNow >= s.ExpiresAtUtc;

    private sealed record Snapshot(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAtUtc);
}
