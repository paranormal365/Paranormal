using System.Net;
using System.Net.Http.Json;

namespace Ben.Web.Services.WebApi;

/// <summary>
/// The one place an HTTP answer from Ben.Data.WebApi becomes an <see cref="ItemResult{T}"/> or a
/// <see cref="LoadResult{T}"/>.
/// </summary>
/// <remarks>
/// <para>Item 225 lifted this out of <c>WebApiClient</c>, which lives in a project a non-web
/// client cannot reference. The rules below are the accumulated answer to a specific class of bug:
/// a page that cannot tell "refused" from "empty", and tells a person there is nothing here when
/// the truth is that nothing was asked. Every client has that problem, so every client should get
/// the same answer to it rather than reinventing one.</para>
///
/// <para>The Swift port in Ben.iOS (BenKit <c>ResponseMapping</c>) matches these rules
/// deliberately, including the prose test's 400-character bound. Change one, change all three.</para>
/// </remarks>
public static class ApiResponseMapper
{
    /// <summary>
    /// Sends a request and reads a single object from the answer.
    /// </summary>
    /// <remarks>
    /// Disposes <paramref name="request"/>. An <see cref="HttpRequestException"/> is caught here
    /// rather than thrown: an unreachable API used to escape a page's initialisation and kill the
    /// Blazor circuit, and in a desktop app it would be an unhandled crash on a dropped Wi-Fi
    /// connection.
    /// </remarks>
    public static async Task<ItemResult<T>> ReadItemAsync<T>(
        HttpClient http, HttpRequestMessage request, CancellationToken token = default)
    {
        using var req = request;

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(req, token);
        }
        catch (HttpRequestException)
        {
            // The API is unreachable — emphatically not "this thing does not exist".
            return ItemResult<T>.Failure();
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                // 401 before anything else, and this is the case the whole type exists for: a dead
                // token is not a missing record. On 2026-08-27 a restarted API had invalidated every
                // bearer token and the profile page could only say the session "may" have expired,
                // because null was all it was given. Now it is told.
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                    return ItemResult<T>.SessionEnded();

                return ItemResult<T>.Failure(await RefusalAsync(response, token));
            }

            // Ok(null) from a controller becomes 204 WITH AN EMPTY BODY (HttpNoContentOutputFormatter),
            // and ReadFromJsonAsync throws on an empty stream. That exception surfaced inside a page's
            // OnInitializedAsync and killed the circuit — the Price Bands screen died on production
            // precisely when the price list was HEALTHY, because healthy is when the endpoint answers
            // "nothing to report". An empty success is a success with nothing in it.
            if (response.StatusCode == HttpStatusCode.NoContent
                || response.Content.Headers.ContentLength == 0)
                return ItemResult<T>.Ok(default);

            return ItemResult<T>.Ok(await response.Content.ReadFromJsonAsync<T>(cancellationToken: token));
        }
    }

    /// <summary>
    /// Sends a request and reads a list from the answer.
    /// </summary>
    /// <remarks>
    /// Anonymous surfaces need this as much as signed-in ones. A public group page whose fetch is
    /// refused shows a visitor an organisation with nothing in it, and the visitor has no account,
    /// no error and no reason to try again — the one audience least able to tell a broken page from
    /// an empty one.
    /// </remarks>
    public static async Task<LoadResult<T>> ReadListAsync<T>(
        HttpClient http, HttpRequestMessage request, CancellationToken token = default)
    {
        using var req = request;

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(req, token);
        }
        catch (HttpRequestException)
        {
            // The API is unreachable. Emphatically not "there is nothing here" — this is the case
            // that used to render as an empty group.
            return LoadResult<T>.Failure();
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                // 401 before anything else. A dead token is not a broken list: the page should say
                // the session ended and offer a way back, not "couldn't load this — try again",
                // which invites a retry that is certain to fail the same way. Item 133.
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                    return LoadResult<T>.SessionEnded();

                return LoadResult<T>.Failure(await RefusalAsync(response, token));
            }

            var items = await response.Content.ReadFromJsonAsync<List<T>>(cancellationToken: token);
            return LoadResult<T>.Ok(items);
        }
    }

    /// <summary>
    /// What to show a person about a refusal: the server's own sentence when it wrote one,
    /// otherwise the status.
    /// </summary>
    /// <remarks>
    /// The status is the single most useful thing a person debugging a deployment can be told. A
    /// blank page says nothing; "the server answered 404" says the path is wrong and "403" says the
    /// path is right and the caller is not allowed. That distinction cost a day of guessing on the
    /// ishaunted.com deploy (item 126).
    /// </remarks>
    private static async Task<string> RefusalAsync(HttpResponseMessage response, CancellationToken token)
    {
        var body = await response.Content.ReadAsStringAsync(token);

        return LooksLikeProse(body)
            ? body.Trim('"', ' ', '\n')
            : $"The server answered {(int)response.StatusCode} ({response.ReasonPhrase}).";
    }

    /// <summary>The opening of the sentence <see cref="RefusalAsync"/> writes when there is no prose.</summary>
    private const string StatusSentenceOpening = "The server answered ";

    /// <summary>
    /// Whether a reason is the status sentence this class generated rather than something a
    /// person wrote.
    /// </summary>
    /// <remarks>
    /// <para>The distinction is invisible by the time a caller holds a <c>Reason</c>, and it
    /// matters most on a PUBLIC page: "The server answered 404 (Not Found)." is the most useful
    /// thing a person debugging a deployment can read and the least useful thing a visitor can,
    /// and putting it in a red alert on a page a stranger came to read about a ghost walk is worse
    /// than saying nothing.</para>
    ///
    /// <para>Comparing against our own generated shape rather than inferring from a status code,
    /// because <see cref="ItemResult{T}"/> and <see cref="LoadResult{T}"/> deliberately do not
    /// carry one — a screen is meant to act on the sentence, not on the number.</para>
    /// </remarks>
    public static bool IsGeneratedStatusSentence(string? reason)
        => reason is not null && reason.StartsWith(StatusSentenceOpening, StringComparison.Ordinal);

    /// <summary>
    /// Whether a response body is a sentence we wrote rather than machinery.
    /// </summary>
    /// <remarks>
    /// A refusal we wrote is a sentence; a framework error is a ProblemDetails blob or an HTML
    /// page, and showing either to a person is worse than saying nothing useful. The length bound
    /// catches the stack-trace-shaped bodies that are neither.
    /// </remarks>
    public static bool LooksLikeProse(string? body) =>
        !string.IsNullOrWhiteSpace(body)
        && body.Length < 400
        && !body.TrimStart().StartsWith('{')
        && !body.TrimStart().StartsWith('<');
}
