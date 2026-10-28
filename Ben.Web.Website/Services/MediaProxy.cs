namespace Ben.Web.Website.Services;

/// <summary>
/// Forwards a media request to the API and streams the reply back, holding nothing.
/// </summary>
/// <remarks>
/// <para><b>The one rule here is that the file never lands in this process.</b> The previous
/// approach fetched whole files server-side and base64'd them into the page — a copy of every
/// file in memory, per card, per render. A media library of recordings drove the website to
/// sixteen gigabytes and the host was killed. <c>ResponseHeadersRead</c> plus
/// <c>CopyToAsync</c> is the difference between forwarding a file and holding one.</para>
///
/// <para><b>This asserts no permissions.</b> It attaches the caller's own bearer token and lets
/// the API decide, so the audience rules stay in exactly one place and cannot drift.</para>
/// </remarks>
public static class MediaProxy
{
    /// <summary>Asked of the API on the browser's behalf, so a cached copy can be checked rather than fetched again.</summary>
    private static readonly string[] ConditionalHeaders = ["If-Range", "If-None-Match", "If-Modified-Since"];

    public static async Task<IResult> StreamAsync(
        string upstreamUrl, MediaCaller? accessToken,
        IHttpClientFactory httpFactory, HttpContext ctx, CancellationToken ct,
        string? cacheControl = null)
    {
        using var http = httpFactory.CreateClient();
        // A large recording is a slow read, not a failure.
        http.Timeout = TimeSpan.FromMinutes(10);

        HttpRequestMessage Request(string? bearer)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, upstreamUrl);
            if (!string.IsNullOrWhiteSpace(bearer))
                request.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);

            // Passed through so somebody can scrub a long recording without fetching all of it.
            if (ctx.Request.Headers.TryGetValue("Range", out var range))
                request.Headers.TryAddWithoutValidation("Range", range.ToString());
            foreach (var name in ConditionalHeaders)
                if (ctx.Request.Headers.TryGetValue(name, out var value))
                    request.Headers.TryAddWithoutValidation(name, value.ToString());

            // Who is actually asking. Without these every relayed request reached the API as this
            // website, so a share link's view log could not tell one visitor from another. The API
            // believes X-Forwarded-For only from the local machine, as it does for WebApiClient.
            if (ctx.Connection.RemoteIpAddress is { } visitor)
                request.Headers.TryAddWithoutValidation("X-Forwarded-For", visitor.ToString());
            if (ctx.Request.Headers.UserAgent is { Count: > 0 } agent)
                request.Headers.TryAddWithoutValidation("User-Agent", agent.ToString());
            return request;
        }

        HttpResponseMessage response;
        try
        {
            using (var first = Request(accessToken?.Token))
                response = await http.SendAsync(first, HttpCompletionOption.ResponseHeadersRead, ct);

            // Refused with a token the page has since renewed, or can renew now: once more with
            // the fresh one (10/28/2026). A recording playing for over an hour used to stop here.
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                && accessToken is { Source: { } source }
                && await source.RenewedAfterRefusalAsync(accessToken.Token, ct) is { Length: > 0 } renewed)
            {
                response.Dispose();
                using var again = Request(renewed);
                response = await http.SendAsync(again, HttpCompletionOption.ResponseHeadersRead, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return Results.Empty;   // the browser stopped asking: a seek, or the page closed
        }
        catch (Exception)
        {
            return Results.StatusCode(StatusCodes.Status502BadGateway);
        }

        using (response)
        {
            // The API's refusal is forwarded as it stands. A 401 or 403 must not be dressed up as
            // a missing file: "you may not see this" and "this is broken" are different answers
            // and the page says different things about them.
            if (!response.IsSuccessStatusCode)
            {
                // A range past the end says how long the file is, so the browser can ask again.
                if (response.Content.Headers.ContentRange is { } unsatisfied)
                    ctx.Response.Headers["Content-Range"] = unsatisfied.ToString();
                if (response.Headers.ETag is { } notModifiedTag)
                    ctx.Response.Headers["ETag"] = notModifiedTag.ToString();
                return Results.StatusCode((int)response.StatusCode);
            }

            ctx.Response.StatusCode = (int)response.StatusCode;
            ctx.Response.ContentType = response.Content.Headers.ContentType?.ToString()
                                       ?? "application/octet-stream";

            // Content-Length is forwarded only when the body is passed on exactly as the API sent it.
            //
            // HttpClient may decompress the upstream body transparently, in which case the
            // length it reports describes the COMPRESSED bytes while we copy the decompressed
            // ones. Promising a length and then writing a different number leaves the response
            // unfinished: the browser holds the connection open waiting for bytes that never
            // come, the page never reaches network-idle, and a media page appears to hang. With no
            // Content-Encoding the length is the length (10/28/2026): Safari wants it on a range of
            // a recording, and a chunked 206 was a risk worth not taking.
            if (response.Content.Headers.ContentEncoding.Count == 0
                && response.Content.Headers.ContentLength is { } length)
                ctx.Response.ContentLength = length;
            // The file's own name, when the API sends one: an event's guest pack should save as
            // "guest-pack.pdf", not as the last segment of this relay's address.
            if (response.Content.Headers.ContentDisposition is { } disposition)
                ctx.Response.Headers["Content-Disposition"] = disposition.ToString();
            if (response.Content.Headers.ContentRange is { } contentRange)
                ctx.Response.Headers["Content-Range"] = contentRange.ToString();
            if (response.Headers.ETag is { } etag)
                ctx.Response.Headers["ETag"] = etag.ToString();
            if (response.Content.Headers.LastModified is { } modified)
                ctx.Response.Headers["Last-Modified"] = modified.ToString("R");
            ctx.Response.Headers["Accept-Ranges"] = "bytes";
            // Private: one viewer's file. A shared cache must not keep it; the browser may, which
            // is what stops a scroll re-fetching every tile.
            // A caller serving something public and immutable (a store picture) says so instead.
            ctx.Response.Headers["Cache-Control"] = cacheControl ?? "private, max-age=3600";

            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                await stream.CopyToAsync(ctx.Response.Body, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // The browser let go mid-file — every seek does this to the read before it.
            }
            return Results.Empty;
        }
    }
}
