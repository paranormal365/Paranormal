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
    public static async Task<IResult> StreamAsync(
        string upstreamUrl, string? accessToken,
        IHttpClientFactory httpFactory, HttpContext ctx, CancellationToken ct)
    {
        using var http = httpFactory.CreateClient();
        // A large recording is a slow read, not a failure.
        http.Timeout = TimeSpan.FromMinutes(10);

        using var request = new HttpRequestMessage(HttpMethod.Get, upstreamUrl);
        if (!string.IsNullOrWhiteSpace(accessToken))
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        // Passed through so somebody can scrub a long recording without fetching all of it.
        if (ctx.Request.Headers.TryGetValue("Range", out var range))
            request.Headers.TryAddWithoutValidation("Range", range.ToString());

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
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
                return Results.StatusCode((int)response.StatusCode);

            ctx.Response.StatusCode = (int)response.StatusCode;
            ctx.Response.ContentType = response.Content.Headers.ContentType?.ToString()
                                       ?? "application/octet-stream";

            // Content-Length is deliberately NOT forwarded.
            //
            // HttpClient may decompress the upstream body transparently, in which case the
            // length it reports describes the COMPRESSED bytes while we copy the decompressed
            // ones. Promising a length and then writing a different number leaves the response
            // unfinished: the browser holds the connection open waiting for bytes that never
            // come, the page never reaches network-idle, and a media page appears to hang. Let
            // Kestrel chunk it instead — it knows when we stopped writing.
            if (response.Content.Headers.ContentRange is { } contentRange)
                ctx.Response.Headers["Content-Range"] = contentRange.ToString();
            ctx.Response.Headers["Accept-Ranges"] = "bytes";
            // Private: one viewer's file. A shared cache must not keep it; the browser may, which
            // is what stops a scroll re-fetching every tile.
            ctx.Response.Headers["Cache-Control"] = "private, max-age=3600";

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            await stream.CopyToAsync(ctx.Response.Body, ct);
            return Results.Empty;
        }
    }
}
