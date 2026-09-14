using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using AngleSharp.Html.Parser;

namespace Ben.Data.WebApi.Services.LinkPreviews;

/// <summary>What a page says about itself.</summary>
public sealed record PageSummary(Uri FinalUrl, string? Title, string? Description, string? SiteName, Uri? ImageUrl);

/// <summary>A fetch that did not produce a summary, and why — for the log, never for a reader.</summary>
public sealed class PageFetchRefusedException(string reason) : Exception(reason);

/// <summary>
/// Reads the title, description, site name and picture a web page declares for itself (Open Graph, then the ordinary
/// title and description), within strict limits.
/// </summary>
/// <remarks>
/// <para>The limits are the point. Every connection is made to an address <see cref="OutboundUrlGuard"/> approved at
/// the moment of connecting — the resolved IP is what the socket dials, so a DNS answer that changes between the check
/// and the connection cannot redirect it inward. Redirects are followed by hand, at most three, each one checked again.
/// Only HTML is read, at most 512 KB of it, within five seconds, so a slow or enormous page costs nothing.</para>
/// </remarks>
public class OpenGraphFetcher
{
    public const int MaxHtmlBytes = 512 * 1024;
    public const int MaxRedirects = 3;
    public const int MaxImageBytes = 2 * 1024 * 1024;
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly HttpClient _http;

    public OpenGraphFetcher(HttpClient http) => _http = http;

    /// <summary>The handler the named client uses: no automatic redirects, and every socket dialled to a vetted address.</summary>
    public static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        UseCookies = false,
        ConnectTimeout = TimeSpan.FromSeconds(4),
        ConnectCallback = async (context, ct) =>
        {
            var addresses = await OutboundUrlGuard.PublicAddressesAsync(context.DnsEndPoint.Host, ct);
            if (addresses.Count == 0)
                throw new HttpRequestException("The address is not on the public internet.");

            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(addresses[0], context.DnsEndPoint.Port, ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        },
    };

    public async Task<PageSummary> FetchAsync(Uri url, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);

        var (response, finalUrl) = await SendFollowingRedirectsAsync(url, "text/html,application/xhtml+xml;q=0.9,*/*;q=0.1", timeout.Token);
        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new PageFetchRefusedException($"the page answered {(int)response.StatusCode}");

            var type = response.Content.Headers.ContentType?.MediaType ?? "";
            if (type is not ("text/html" or "application/xhtml+xml"))
                throw new PageFetchRefusedException($"not a web page ({(type.Length == 0 ? "no type" : type)})");

            var html = await ReadCappedAsync(response, MaxHtmlBytes, timeout.Token, charset: response.Content.Headers.ContentType?.CharSet);
            return Parse(html, finalUrl);
        }
    }

    /// <summary>The bytes of a picture, when the address is a picture of at most <see cref="MaxImageBytes"/>.</summary>
    public async Task<(byte[] Bytes, string ContentType)> FetchImageAsync(Uri url, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);

        var (response, _) = await SendFollowingRedirectsAsync(url, "image/*", timeout.Token);
        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new PageFetchRefusedException($"the picture answered {(int)response.StatusCode}");
            var type = response.Content.Headers.ContentType?.MediaType ?? "";
            if (!type.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                throw new PageFetchRefusedException("the picture is not an image");
            if (response.Content.Headers.ContentLength > MaxImageBytes)
                throw new PageFetchRefusedException("the picture is too large");

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk, timeout.Token)) > 0)
            {
                if (buffer.Length + read > MaxImageBytes) throw new PageFetchRefusedException("the picture is too large");
                buffer.Write(chunk, 0, read);
            }
            return (buffer.ToArray(), type);
        }
    }

    private async Task<(HttpResponseMessage Response, Uri FinalUrl)> SendFollowingRedirectsAsync(Uri url, string accept, CancellationToken ct)
    {
        var current = url;
        for (var hop = 0; ; hop++)
        {
            if (OutboundUrlGuard.WhyNot(current) is { } why) throw new PageFetchRefusedException(why);

            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.Accept.ParseAdd(accept);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("IsHauntedBot", "1.0"));

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            }
            catch (HttpRequestException ex)
            {
                throw new PageFetchRefusedException($"could not connect ({ex.Message})");
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new PageFetchRefusedException("took too long");
            }

            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
            {
                response.Dispose();
                if (hop >= MaxRedirects) throw new PageFetchRefusedException("too many redirects");
                current = location.IsAbsoluteUri ? location : new Uri(current, location);
                continue;
            }

            return (response, current);
        }
    }

    private static async Task<string> ReadCappedAsync(HttpResponseMessage response, int maxBytes, CancellationToken ct, string? charset)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var buffer = new byte[maxBytes];
        var total = 0;
        int read;
        // Stops at the cap rather than refusing: the head of a page, where its title and Open Graph tags are, comes first.
        while (total < maxBytes && (read = await stream.ReadAsync(buffer.AsMemory(total, maxBytes - total), ct)) > 0)
            total += read;

        Encoding encoding;
        try { encoding = string.IsNullOrWhiteSpace(charset) ? Encoding.UTF8 : Encoding.GetEncoding(charset.Trim('"')); }
        catch (ArgumentException) { encoding = Encoding.UTF8; }
        return encoding.GetString(buffer, 0, total);
    }

    /// <summary>The summary a page's HTML declares. Public for tests: the parsing rules, without a network.</summary>
    public static PageSummary Parse(string html, Uri pageUrl)
    {
        var doc = new HtmlParser().ParseDocument(html);

        string? Meta(params string[] keys)
        {
            foreach (var key in keys)
            {
                var el = doc.QuerySelector($"meta[property='{key}' i], meta[name='{key}' i]");
                var content = el?.GetAttribute("content");
                if (!string.IsNullOrWhiteSpace(content)) return Clean(content);
            }
            return null;
        }

        var title = Meta("og:title", "twitter:title") ?? Clean(doc.Title);
        var description = Meta("og:description", "twitter:description", "description");
        var siteName = Meta("og:site_name", "application-name");
        var image = Meta("og:image:secure_url", "og:image", "twitter:image", "twitter:image:src");

        Uri? imageUrl = null;
        if (image is not null && Uri.TryCreate(pageUrl, image, out var resolved) && resolved.Scheme is "http" or "https")
            imageUrl = resolved;

        return new PageSummary(pageUrl, Cap(title, 200), Cap(description, 500), Cap(siteName, 100), imageUrl);
    }

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var decoded = WebUtility.HtmlDecode(value);
        var collapsed = string.Join(' ', decoded.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length == 0 ? null : collapsed;
    }

    private static string? Cap(string? value, int max) => value is null ? null : value.Length <= max ? value : value[..(max - 1)] + "…";
}
