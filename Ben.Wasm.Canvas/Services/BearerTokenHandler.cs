using System.Net;
using System.Net.Http.Headers;

namespace Ben.Wasm.Canvas.Services;

// Copied from Ben.Wasm.Video/Services/BearerTokenHandler.cs, with one addition: the token is attached
// only to requests addressed under the configured API base (review correction R15).

/// <summary>
/// Attaches the session's bearer token to outgoing Web API calls, refreshing once on 401.
/// </summary>
/// <remarks>
/// <para>Attached (in Program.cs) to the editor's persistence client only - NOT to the public client,
/// and NOT to AuthService's client, which must be able to call refresh with an expired token.</para>
///
/// <para><b>The token only goes to the API.</b> The persistence client could one day be handed an
/// absolute URL that came out of a document - a link card's picture, a pasted image address - and a
/// handler that attaches the token to every request would send a case member's credentials to that
/// third party. Anything not under <c>{ApiBaseUrl}/</c> is sent without it.</para>
/// </remarks>
public sealed class BearerTokenHandler : DelegatingHandler
{
    /// <summary>
    /// The largest body this will hold a second copy of in order to retry, in bytes.
    /// </summary>
    /// <remarks>
    /// Eight megabytes: above every JSON body these clients send, and far below a large upload.
    /// Above it a 401 is returned rather than the tab being asked for a second copy.
    /// </remarks>
    public const long MaximumRetryableBody = 8L * 1024 * 1024;

    private readonly TokenStore _tokens;
    private readonly AuthService _auth;
    private readonly string? _apiPrefix;

    /// <param name="tokens">The session's tokens.</param>
    /// <param name="auth">Refreshes an expired token.</param>
    /// <param name="apiBaseUrl">The API base; the token is attached only to requests under it. Null attaches it nowhere.</param>
    public BearerTokenHandler(TokenStore tokens, AuthService auth, string? apiBaseUrl)
    {
        _tokens = tokens;
        _auth = auth;
        _apiPrefix = string.IsNullOrWhiteSpace(apiBaseUrl) ? null : apiBaseUrl.Trim().TrimEnd('/') + "/";
    }

    /// <summary>True when <paramref name="uri"/> is addressed under the configured API base.</summary>
    public bool IsApiRequest(Uri? uri)
        => _apiPrefix is not null
        && uri is { IsAbsoluteUri: true }
        && uri.AbsoluteUri.StartsWith(_apiPrefix, StringComparison.OrdinalIgnoreCase);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        if (!IsApiRequest(request.RequestUri))
            return await base.SendAsync(request, ct);

        // Refresh before sending, not after being told: the store already knows the token expired.
        if (await _tokens.IsAccessTokenExpiredAsync())
            await _auth.TryRefreshAsync(ct);

        var token = await _tokens.GetAccessTokenAsync();
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await base.SendAsync(request, ct);
        if (response.StatusCode != HttpStatusCode.Unauthorized) return response;

        // A body too large to hold twice is not retried; the caller gets the 401.
        if (request.Content?.Headers.ContentLength is { } length && length > MaximumRetryableBody)
            return response;

        // One refresh, one retry. A second 401 means the session is genuinely over.
        if (!await _auth.TryRefreshAsync(ct)) return response;

        var retryToken = await _tokens.GetAccessTokenAsync();
        if (retryToken is null) return response;

        response.Dispose();
        var retry = await CloneAsync(request, ct);
        retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", retryToken);
        return await base.SendAsync(retry, ct);
    }

    /// <summary>An HttpRequestMessage can only be sent once, so the retry needs a copy.</summary>
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
