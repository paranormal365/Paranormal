using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;

namespace Ben.Data.WebApi.Services.LinkUnfurl;

/// <summary>What one guarded fetch produced.</summary>
/// <param name="StatusCode">The final answer's HTTP status, or 0 when nothing answered or the fetch was refused.</param>
/// <param name="ContentType">The final answer's media type, when there was one.</param>
/// <param name="Body">The body, only for a 200 of the accepted kind within the byte cap.</param>
/// <param name="FinalUrl">The address the answer came from, after redirects.</param>
/// <param name="Refusal">Why the fetch was refused or abandoned, as a sentence; null when it ran.</param>
public sealed record SafeFetchResult(int StatusCode, string? ContentType, byte[]? Body, Uri FinalUrl, string? Refusal);

/// <summary>Fetches a stranger's https address without letting it reach anything of ours.</summary>
/// <remarks>An interface so the unfurl service and the image proxy can be tested without a network.</remarks>
public interface ISafeUrlFetcher
{
    /// <summary>
    /// GETs <paramref name="url"/>, accepting only a 200 whose content type starts with
    /// <paramref name="acceptPrefix"/> and whose body is at most <paramref name="maxBytes"/>.
    /// </summary>
    Task<SafeFetchResult> FetchAsync(Uri url, string acceptPrefix, long maxBytes, CancellationToken ct);
}

/// <summary>
/// The only way this server fetches an address somebody pasted (canvas plan M6-08, review R21).
/// </summary>
/// <remarks>
/// <para><b>Vetted-address connect.</b> The name is resolved here, every address is checked against
/// <see cref="SafeUrlPolicy.IsForbidden"/>, and the request carries the chosen address in its
/// options. The handler's connect callback dials <b>that address</b> and nothing else — it never
/// resolves the name again. Without this, the check and the connect would be two DNS lookups, and a
/// name with a zero TTL can answer a public address to the first and 169.254.169.254 to the second
/// (DNS rebinding). TLS still validates the certificate against the name in the URL, because SNI and
/// certificate checks use the request URI, not the socket.</para>
///
/// <para><b>Any forbidden address refuses the whole name.</b> A name answering one public and one
/// private address is either misconfigured or bait; either way there is no reason to pick.</para>
///
/// <para><b>Everything else that could leak or amplify:</b> no proxy (a system proxy would dial the
/// name itself, after our check); no cookies; no automatic redirects — each hop is re-vetted, at most
/// three; five seconds for the whole fetch including every hop; the body counted as it arrives
/// (after decompression), never trusted from <c>Content-Length</c>; response headers capped at
/// 64 KB; pooled connections recycled within a minute so a connection outlives its vetting by
/// little. Refusals are logged at Information with the host only — a pasted URL can carry a token.</para>
/// </remarks>
public sealed class SafeUrlFetcher : ISafeUrlFetcher
{
    /// <summary>The named HttpClient whose primary handler is <see cref="CreateHandler"/>.</summary>
    public const string ClientName = "LinkUnfurl";

    /// <summary>Where the vetted address rides on a request, for the connect callback.</summary>
    public static readonly HttpRequestOptionsKey<IPAddress> VettedAddressKey = new("ishaunted.link-unfurl.vetted-address");

    /// <summary>The whole fetch, every hop included.</summary>
    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(5);

    private const int MaxRedirects = 3;
    private const int ChunkBytes = 16 * 1024;
    private const string UserAgent = "IsHaunted-LinkPreview/1.0 (+https://ishaunted.com)";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SafeUrlFetcher> _logger;
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _resolve;

    public SafeUrlFetcher(IHttpClientFactory httpClientFactory, ILogger<SafeUrlFetcher> logger,
        Func<string, CancellationToken, Task<IPAddress[]>>? resolve = null)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _resolve = resolve ?? ((host, ct) => Dns.GetHostAddressesAsync(host, ct));
    }

    /// <inheritdoc />
    public async Task<SafeFetchResult> FetchAsync(Uri url, string acceptPrefix, long maxBytes, CancellationToken ct)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(Budget);

        var current = url;
        for (var hop = 0; ; hop++)
        {
            if (hop > MaxRedirects) return Refused(current, "The link redirected too many times to preview.");

            if (SafeUrlPolicy.Refuse(current) is { } refusal) return Refused(current, refusal);
            current = WithoutFragment(current);

            IPAddress[] addresses;
            try
            {
                addresses = await _resolve(current.IdnHost, budget.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return Refused(current, "The site took too long to answer.");
            }
            catch (SocketException)
            {
                return Refused(current, "That site could not be found.");
            }

            if (addresses.Length == 0) return Refused(current, "That site could not be found.");
            if (addresses.Any(SafeUrlPolicy.IsForbidden))
                return Refused(current, "That site's address is on a private network, so it cannot be previewed.");

            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Options.Set(VettedAddressKey, addresses[0]);
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            request.Headers.TryAddWithoutValidation("Accept",
                acceptPrefix.EndsWith('/') ? acceptPrefix + "*;q=0.9,*/*;q=0.1" : acceptPrefix + ",*/*;q=0.1");
            request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue("en"));

            HttpResponseMessage response;
            try
            {
                var client = _httpClientFactory.CreateClient(ClientName);
                client.Timeout = Timeout.InfiniteTimeSpan;   // the budget above is the timeout
                response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, budget.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return Refused(current, "The site took too long to answer.");
            }
            catch (HttpRequestException)
            {
                return Refused(current, "That site could not be reached.");
            }

            using (response)
            {
                var status = (int)response.StatusCode;
                if (status is 301 or 302 or 303 or 307 or 308)
                {
                    if (response.Headers.Location is not { } location)
                        return new SafeFetchResult(status, null, null, current, "The site redirected without saying where.");
                    current = location.IsAbsoluteUri ? location : new Uri(current, location);
                    continue;
                }

                var contentType = response.Content.Headers.ContentType?.MediaType;
                if (status != 200) return new SafeFetchResult(status, contentType, null, current, null);

                if (contentType is null || !contentType.StartsWith(acceptPrefix, StringComparison.OrdinalIgnoreCase))
                    return Refused(current, $"The link is not {acceptPrefix.TrimEnd('/')}.", status, contentType);

                if (response.Content.Headers.ContentLength is { } promised && promised > maxBytes)
                    return Refused(current, "The page is too large to preview.", status, contentType);

                try
                {
                    await using var stream = await response.Content.ReadAsStreamAsync(budget.Token);
                    using var buffer = new MemoryStream();
                    var chunk = new byte[ChunkBytes];
                    int read;
                    while ((read = await stream.ReadAsync(chunk, budget.Token)) > 0)
                    {
                        if (buffer.Length + read > maxBytes)
                            return Refused(current, "The page is too large to preview.", status, contentType);
                        buffer.Write(chunk, 0, read);
                    }
                    return new SafeFetchResult(status, contentType, buffer.ToArray(), current, null);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    return Refused(current, "The site took too long to answer.", status, contentType);
                }
                catch (IOException)
                {
                    return Refused(current, "That site could not be reached.", status, contentType);
                }
                catch (HttpRequestException)
                {
                    return Refused(current, "That site could not be reached.", status, contentType);
                }
            }
        }
    }

    /// <summary>
    /// The primary handler for <see cref="ClientName"/>: dials only the vetted address, follows no
    /// redirect, sends no cookie, uses no proxy.
    /// </summary>
    public static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect           = false,
        UseCookies                  = false,
        UseProxy                    = false,
        Proxy                       = null,
        AutomaticDecompression      = DecompressionMethods.All,
        ConnectTimeout              = TimeSpan.FromSeconds(5),
        MaxResponseHeadersLength    = 64,     // kilobytes
        PooledConnectionLifetime    = TimeSpan.FromMinutes(1),
        PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
        MaxConnectionsPerServer     = 4,
        ConnectCallback             = ConnectToVettedAddressAsync,
    };

    /// <summary>
    /// Opens the socket to the address <see cref="FetchAsync"/> vetted. Refuses a request that
    /// carries none, and re-checks the one it carries — belt and braces against a future caller that
    /// uses the named client without going through <see cref="FetchAsync"/>.
    /// </summary>
    private static async ValueTask<Stream> ConnectToVettedAddressAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        if (!context.InitialRequestMessage.Options.TryGetValue(VettedAddressKey, out var address) || address is null)
            throw new InvalidOperationException("The link-unfurl client only connects to a vetted address, and this request carries none.");
        if (SafeUrlPolicy.IsForbidden(address))
            throw new InvalidOperationException("The link-unfurl client refused to connect to a forbidden address.");

        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), ct);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private SafeFetchResult Refused(Uri url, string refusal, int status = 0, string? contentType = null)
    {
        _logger.LogInformation("Link preview of a page on {Host} refused: {Refusal}",
            url.IsAbsoluteUri ? url.Host : "(relative)", refusal);
        return new SafeFetchResult(status, contentType, null, url, refusal);
    }

    private static Uri WithoutFragment(Uri url)
        => string.IsNullOrEmpty(url.Fragment) ? url : new UriBuilder(url) { Fragment = string.Empty }.Uri;
}
