using System.Net;
using System.Net.Http.Json;
using Ben.Canvas.Core.Model;
using Ben.Canvas.Core.Options;
using Ben.Canvas.Editor.Extensions;
using Microsoft.Extensions.Options;

namespace Ben.Canvas.Editor.Services;

/// <summary>Our records, then the API's unfurl, then the host alone.</summary>
/// <remarks>
/// Our records come from the anonymous public preview, which answers only for ishaunted.com addresses and never
/// fetches a stranger's page. The unfurl does fetch a stranger's page, behind the API's SSRF guard, so it is asked
/// only for https addresses and only for a signed-in person (the endpoint refuses anyone else anyway).
/// </remarks>
public sealed class TieredLinkPreviewProvider(IHttpClientFactory http, IOptions<CanvasEditorOptions> options, ICanvasSignInState? signIn = null) : ILinkPreviewProvider
{
    private readonly Dictionary<string, LinkPreviewCard> _cache = new(StringComparer.Ordinal);

    /// <summary>
    /// What the anonymous endpoint answers. It describes our own pages from our own tables, and for
    /// anybody else's page it answers with a preview somebody signed-in has already had kept — so the
    /// trailing fields are filled in only in that second case, and a board with no account behind it
    /// still shows a proper card for a link the group has used before (Ben, 2026-09-17).
    /// </summary>
    private sealed record OurRecord(
        string? Kind, string? Title, string? Subtitle, string? Path,
        string? Description = null, string? ImageUrl = null, string? SiteName = null, string? Domain = null);

    private sealed record UnfurlRecord(string? Url, string? Host, string? Title, string? Description, string? ImageSourceUrl, string? SiteName);

    public async Task<LinkPreviewCard> GetAsync(string url, CancellationToken ct = default)
    {
        var text = (url ?? "").Trim();
        if (_cache.TryGetValue(text, out var known)) return known;

        if (!Uri.TryCreate(text, UriKind.Absolute, out var address) || address.Scheme is not ("http" or "https"))
            return new LinkPreviewCard(text, "", "Link", text, null, null, null, LinkPreviewTier.HostOnly);

        var hostOnly = new LinkPreviewCard(text, address.Host, "Link", null, null, null, null, LinkPreviewTier.HostOnly);
        var apiBase = string.IsNullOrWhiteSpace(options.Value.ApiBaseUrl) ? null : options.Value.ApiBaseUrl.Trim().TrimEnd('/');
        if (apiBase is null) return hostOnly;

        var escaped = Uri.EscapeDataString(text);
        var card = await OursAsync($"{apiBase}/api/public/link-preview?url={escaped}", text, address.Host, ct);

        if (card is null && options.Value.LinkUnfurl && (signIn?.IsSignedIn ?? false) && address.Scheme == "https")
        {
            // Ask the server to keep a preview: the card comes back with our own copy of the page's
            // picture, at an address anybody's browser can load. That is what makes a published
            // board's link cards keep their pictures for the people it was published for
            // (Ben, 2026-09-17). The older unfurl stays as the fallback — it needs no storage and
            // answers when the keeping service cannot.
            card = await KeptAsync($"{apiBase}/api/link-previews", text, address.Host, ct)
                ?? await UnfurlAsync($"{apiBase}/api/link-unfurl?url={escaped}", text, address.Host, ct);
        }

        // A host-only answer is not kept: signing in, or the page coming back, should be able to do better.
        if (card is null) return hostOnly;
        _cache[text] = card;
        return card;
    }

    /// <summary>The address of a card's picture through the API's image proxy - the only place that address is built.</summary>
    /// <remarks>The proxy needs the bearer token, so the card fetches it through the media store, never a bare img src.</remarks>
    public static string? ProxyImageUrl(string? apiBaseUrl, string? imageSourceUrl) =>
        Components.Nodes.LinkNode.ImageProxyUrl(apiBaseUrl?.Trim(), imageSourceUrl);

    private async Task<LinkPreviewCard?> OursAsync(string requestUrl, string url, string host, CancellationToken ct)
    {
        var record = await ReadAsync<OurRecord>(CanvasEditorServiceCollectionExtensions.PublicHttpClientName, requestUrl, ct);
        if (record is null || string.IsNullOrWhiteSpace(record.Title)) return null;

        // Our own pages are described from our own tables and carry no domain; a kept preview of
        // somebody else's page always does. That is the difference between the two answers this one
        // endpoint gives.
        if (string.IsNullOrWhiteSpace(record.Domain))
            return new LinkPreviewCard(url, host, record.Kind ?? "Link", record.Title, record.Subtitle, null, "IsHaunted", LinkPreviewTier.OurRecords);

        // Somebody else's page, kept earlier by somebody signed in: the whole card, picture included.
        return new LinkPreviewCard(url,
            string.IsNullOrWhiteSpace(record.Domain) ? host : record.Domain!,
            "Link", record.Title, record.Description, null, record.SiteName,
            LinkPreviewTier.Unfurled, ThumbnailPath(record.ImageUrl));
    }

    private sealed record KeptRecord(
        string? Kind, string? Title, string? Subtitle, string? Path,
        string? Description, string? ImageUrl, string? SiteName, string? Domain);

    private async Task<LinkPreviewCard?> KeptAsync(string requestUrl, string url, string host, CancellationToken ct)
    {
        KeptRecord? record;
        try
        {
            using var response = await http.CreateClient(CanvasEditorServiceCollectionExtensions.PersistenceHttpClientName)
                .PostAsJsonAsync(requestUrl, new { Url = url, Refresh = false }, HttpCanvasServerStore.Json, ct);
            if (response.StatusCode != HttpStatusCode.OK) return null;
            record = await response.Content.ReadFromJsonAsync<KeptRecord>(HttpCanvasServerStore.Json, ct);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException
                                      or System.Text.Json.JsonException or NotSupportedException && !ct.IsCancellationRequested)
        {
            return null;
        }

        if (record is null || string.IsNullOrWhiteSpace(record.Title)) return null;

        return new LinkPreviewCard(url,
            string.IsNullOrWhiteSpace(record.Domain) ? host : record.Domain!,
            "Link", record.Title, record.Description, null, record.SiteName,
            LinkPreviewTier.Unfurled, ThumbnailPath(record.ImageUrl));
    }

    /// <summary>
    /// The one shape of picture address this accepts, turned into the API's own public route for it.
    /// </summary>
    /// <remarks>
    /// The server answers with the website's relay path. A board is not always served beside the
    /// website — in development it runs on its own port — so the API's route is used instead, and
    /// anything that is not exactly one kept preview's path is refused rather than passed through to
    /// an img src.
    /// </remarks>
    internal static string? ThumbnailPath(string? imageUrl)
    {
        const string prefix = "/media/link-preview/";
        if (string.IsNullOrWhiteSpace(imageUrl) || !imageUrl.StartsWith(prefix, StringComparison.Ordinal)) return null;

        return Guid.TryParse(imageUrl[prefix.Length..], out var id)
            ? $"/api/public/link-previews/{id}/thumbnail"
            : null;
    }

    private async Task<LinkPreviewCard?> UnfurlAsync(string requestUrl, string url, string host, CancellationToken ct)
    {
        var record = await ReadAsync<UnfurlRecord>(CanvasEditorServiceCollectionExtensions.PersistenceHttpClientName, requestUrl, ct);
        return record is null
            ? null
            : new LinkPreviewCard(url, string.IsNullOrWhiteSpace(record.Host) ? host : record.Host, "Link",
                record.Title, record.Description, record.ImageSourceUrl, record.SiteName, LinkPreviewTier.Unfurled);
    }

    private async Task<T?> ReadAsync<T>(string clientName, string requestUrl, CancellationToken ct) where T : class
    {
        try
        {
            using var response = await http.CreateClient(clientName).GetAsync(requestUrl, ct);
            if (response.StatusCode != HttpStatusCode.OK) return null;
            return await response.Content.ReadFromJsonAsync<T>(HttpCanvasServerStore.Json, ct);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or NotSupportedException && !ct.IsCancellationRequested)
        {
            return null;
        }
    }
}
