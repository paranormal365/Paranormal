namespace Ben.Web.Website.Services;

/// <summary>
/// Sends one prepared request to the API and hands the JSON answer back — status, body and all.
/// </summary>
/// <remarks>
/// The chunk-relay counterpart of <see cref="MediaProxy"/>: where that streams a large RESPONSE
/// out, this forwards small JSON answers about large REQUESTS that have already streamed through.
/// The API's refusal sentences ("that file is … bytes", "chunks are missing: …") are forwarded
/// verbatim with their status codes — the page shows the server's own words, so the reason never
/// degrades to a generic failure on the way through.
/// </remarks>
public static class UploadRelay
{
    public static async Task<IResult> ForwardAsync(
        HttpClient http, HttpRequestMessage request, CancellationToken ct)
    {
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
            var body = await response.Content.ReadAsStringAsync(ct);
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/json";
            return Results.Text(body, contentType, statusCode: (int)response.StatusCode);
        }
    }
}
