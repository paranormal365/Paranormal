using Ben.Web.Services;
using Ben.Web.Services.WebApi;

namespace Ben.Web.Website.Services;

/// <summary>
/// Builds URLs a browser can fetch media from, for the viewer who is signed in.
/// </summary>
/// <remarks>
/// Scoped, because the ticket it mints belongs to one viewer. Components ask for a URL and put it
/// in an <c>src</c>; the bytes then travel browser-to-server-to-API without ever being
/// materialised in this process. See <see cref="MediaTicketService"/> for why a ticket is needed
/// at all.
/// </remarks>
public sealed class MediaUrlBuilder : IMediaUrlBuilder
{
    private readonly MediaTicketService _tickets;
    private readonly IWebApiTokenStore _tokens;

    /// <summary>
    /// One URL per file for the life of this circuit.
    /// </summary>
    /// <remarks>
    /// <b>Load-bearing.</b> Data Protection is randomised — protecting the same payload twice
    /// gives different ciphertext — so minting a ticket per call produced a different URL on
    /// every render. The browser treated each as a new image and fetched the lot again, and
    /// because a finished fetch triggers a re-render, the page never stopped fetching: it never
    /// reached network-idle and looked like it had hung. Caching the string makes the URL stable,
    /// which also lets the browser cache the bytes.
    /// </remarks>
    private readonly Dictionary<string, string> _urls = [];

    public MediaUrlBuilder(MediaTicketService tickets, IWebApiTokenStore tokens)
    {
        _tickets = tickets;
        _tokens = tokens;
    }

    public string Thumbnail(Guid fileId) => Build(fileId, "thumbnail");

    public string Download(Guid fileId) => Build(fileId, "download");

    public string FieldSessionFile(Guid sessionId, Guid fileId)
    {
        var cacheKey = $"session:{sessionId}:{fileId}";
        if (_urls.TryGetValue(cacheKey, out var cached)) return cached;

        var token = _tokens.AccessToken;
        var ticket = string.IsNullOrWhiteSpace(token) ? null : _tickets.Protect(fileId, token);
        var url = ticket is null
            ? $"/media/field-sessions/{sessionId}/files/{fileId}"
            : $"/media/field-sessions/{sessionId}/files/{fileId}?t={Uri.EscapeDataString(ticket)}";

        _urls[cacheKey] = url;
        return url;
    }

    private string Build(Guid fileId, string kind)
    {
        var cacheKey = $"{kind}:{fileId}";
        if (_urls.TryGetValue(cacheKey, out var cached)) return cached;

        var token = _tokens.AccessToken;
        // Signed out is not an error: public files still resolve, and the endpoint simply calls
        // the API without a bearer token, which is exactly what an anonymous visitor should get.
        var ticket = string.IsNullOrWhiteSpace(token) ? null : _tickets.Protect(fileId, token);
        var url = ticket is null
            ? $"/media/{fileId}/{kind}"
            : $"/media/{fileId}/{kind}?t={Uri.EscapeDataString(ticket)}";

        _urls[cacheKey] = url;
        return url;
    }
}
