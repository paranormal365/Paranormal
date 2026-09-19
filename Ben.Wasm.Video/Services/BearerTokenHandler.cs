using System.Net;
using System.Net.Http.Headers;

namespace Ben.Wasm.Video.Services;

/// <summary>
/// Attaches the session's bearer token to outgoing WebApi calls, refreshing once on 401.
/// </summary>
/// <remarks>
/// The WASM counterpart of the server host's <c>BenMediaLibraryProvider</c> token forwarding.
/// Attached (in Program.cs) to the editor's MediaLibrary and ProjectPersistence named clients —
/// deliberately NOT to AssetCatalog, whose read endpoints are anonymous by design, and NOT to
/// AuthService's client, which must be able to call /refresh with an expired access token.
/// </remarks>
public sealed class BearerTokenHandler : DelegatingHandler
{
    /// <summary>
    /// The largest body this will hold a second copy of in order to retry, in bytes.
    /// </summary>
    /// <remarks>
    /// Eight megabytes: comfortably above every JSON body these clients send, and far below a
    /// finished render. Above it a 401 is returned rather than the tab being asked for a second
    /// copy of something that may be hundreds of megabytes.
    /// </remarks>
    public const long MaximumRetryableBody = 8L * 1024 * 1024;

    private readonly TokenStore _tokens;
    private readonly AuthService _auth;

    public BearerTokenHandler(TokenStore tokens, AuthService auth)
    {
        _tokens = tokens;
        _auth = auth;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        // Refresh before sending, not after being told. The store already knew the token had
        // expired, so every call after expiry went out with a dead one, collected a 401, refreshed
        // and went again — two round trips where one would do (2026-09-05 audit, wasm-15).
        if (await _tokens.IsAccessTokenExpiredAsync())
            await _auth.TryRefreshAsync(ct);

        var token = await _tokens.GetAccessTokenAsync();
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await base.SendAsync(request, ct);
        if (response.StatusCode != HttpStatusCode.Unauthorized) return response;

        // A body too large to hold twice is not retried. The retry needs a copy, and copying means
        // the whole thing in memory a second time — which for a finished render is hundreds of
        // megabytes inside a browser tab. The 401 is returned instead, and the caller can act on
        // it (2026-09-05 audit, wasm-15).
        if (request.Content?.Headers.ContentLength is { } length && length > MaximumRetryableBody)
            return response;

        // One refresh, one retry. A second 401 means the session is genuinely over — surface it
        // rather than looping against an endpoint that has already said no twice.
        if (!await _auth.TryRefreshAsync(ct)) return response;

        var retryToken = await _tokens.GetAccessTokenAsync();
        if (retryToken is null) return response;

        response.Dispose();
        var retry = await CloneAsync(request, ct);
        retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", retryToken);
        return await base.SendAsync(retry, ct);
    }

    /// <summary>
    /// An HttpRequestMessage can only be sent once, so the retry needs a copy.
    /// </summary>
    /// <remarks>
    /// Content is buffered into memory, which is why <see cref="MaximumRetryableBody"/> gates
    /// reaching here at all. That gate is the enforcement of what this comment used to merely
    /// assert.
    /// </remarks>
    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var (key, values) in request.Headers)
            clone.Headers.TryAddWithoutValidation(key, values);

        if (request.Content is not null)
        {
            var bytes = await request.Content.ReadAsByteArrayAsync(ct);
            var content = new ByteArrayContent(bytes);
            foreach (var (key, values) in request.Content.Headers)
                content.Headers.TryAddWithoutValidation(key, values);
            clone.Content = content;
        }

        return clone;
    }
}
