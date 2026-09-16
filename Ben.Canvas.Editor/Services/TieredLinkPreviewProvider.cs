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

    private sealed record OurRecord(string? Kind, string? Title, string? Subtitle, string? Path);

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
            card = await UnfurlAsync($"{apiBase}/api/link-unfurl?url={escaped}", text, address.Host, ct);

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
        return record is null || string.IsNullOrWhiteSpace(record.Title)
            ? null
            : new LinkPreviewCard(url, host, record.Kind ?? "Link", record.Title, record.Subtitle, null, "IsHaunted", LinkPreviewTier.OurRecords);
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
