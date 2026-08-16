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
        var token = await _tokens.GetAccessTokenAsync();
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await base.SendAsync(request, ct);
        if (response.StatusCode != HttpStatusCode.Unauthorized) return response;

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
    /// An HttpRequestMessage can only be sent once, so the retry needs a copy. Content is buffered
    /// into memory — fine for the JSON bodies these clients send; large uploads (the final render)
    /// go up once with a fresh token rather than through this retry path.
    /// </summary>
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
