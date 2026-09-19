using Ben.Web.Services.WebApi;
using System.Net;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// A single-object GET has to say which kind of nothing it got.
/// </summary>
/// <remarks>
/// <para>Lists got this in item 120; objects did not. <c>GetAsync</c> answered a 401, a 403, a 404
/// and an empty success with the same <c>null</c>, so on 2026-08-27 — when a restarted API had
/// invalidated every bearer token — the profile page could only say the session "may" have expired.
/// It had nothing better to go on.</para>
///
/// <para>Each test here fails against the old <c>GetAsync</c>, which is the point: the old one
/// returned <c>default</c> for every case below and threw outright for the last.</para>
/// </remarks>
public sealed class ItemResultTests
{
    private sealed class CannedHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond());
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("connection refused");
    }

    private static WebApiClient Client(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://unit.test") }, new WebApiTokenStore());

    private static WebApiClient Client(HttpStatusCode status, string? body = null, string? reason = null)
    {
        var response = new HttpResponseMessage(status);
        if (reason is not null) response.ReasonPhrase = reason;
        if (body is not null)
            response.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        return Client(new CannedHandler(() => response));
    }

    [Fact]
    public async Task A_401_reports_the_session_ended_rather_than_a_missing_record()
    {
        var result = await Client(HttpStatusCode.Unauthorized).GetItemAsync<string>("/api/me");

        Assert.True(result.SessionExpired);
        Assert.True(result.Failed);
        Assert.False(result.IsEmpty);
        // No reason string: "the server answered 401" is a fact about HTTP, not a sentence for a
        // person. The surface writes its own for this state.
        Assert.Null(result.Reason);
    }

    [Fact]
    public async Task A_403_is_a_failure_but_not_a_dead_session()
    {
        var result = await Client(HttpStatusCode.Forbidden).GetItemAsync<string>("/api/me");

        Assert.True(result.Failed);
        // Telling somebody to sign in again over a 403 sends them round a loop ending where it
        // started — the session is fine, the thing is not theirs.
        Assert.False(result.SessionExpired);
    }

    [Fact]
    public async Task A_404_is_a_failure_carrying_the_status_rather_than_a_silent_null()
    {
        var result = await Client(HttpStatusCode.NotFound, reason: "Not Found").GetItemAsync<string>("/api/typo");

        Assert.True(result.Failed);
        Assert.False(result.IsEmpty);
        Assert.Contains("404", result.Reason);
    }

    [Fact]
    public async Task A_refusal_written_as_prose_is_shown_and_a_ProblemDetails_blob_is_not()
    {
        var prose = await Client(HttpStatusCode.BadRequest, "\"That case has already been closed.\"")
            .GetItemAsync<string>("/x");
        Assert.Equal("That case has already been closed.", prose.Reason);

        var blob = await Client(HttpStatusCode.InternalServerError,
                                "{\"type\":\"about:blank\",\"title\":\"An error occurred\"}",
                                reason: "Internal Server Error")
            .GetItemAsync<string>("/x");
        Assert.DoesNotContain("about:blank", blob.Reason);
        Assert.Contains("500", blob.Reason);
    }

    [Fact]
    public async Task An_empty_success_is_empty_and_not_failed()
    {
        var result = await Client(HttpStatusCode.NoContent).GetItemAsync<string>("/x");

        Assert.False(result.Failed);
        Assert.True(result.IsEmpty);
        Assert.Null(result.Item);
    }

    [Fact]
    public async Task A_real_body_still_arrives()
    {
        var result = await Client(HttpStatusCode.OK, "\"Sarah Mitchell\"").GetItemAsync<string>("/api/me");

        Assert.False(result.Failed);
        Assert.False(result.IsEmpty);
        Assert.Equal("Sarah Mitchell", result.Item);
    }

    [Fact]
    public async Task An_unreachable_api_is_a_failure_rather_than_an_exception_out_of_the_page()
    {
        // The regression this is really guarding: the old GetAsync had no catch at all, so this
        // threw out of OnInitializedAsync and took the Blazor circuit with it. The list path has
        // caught it since item 120; the object path never did.
        var result = await Client(new ThrowingHandler()).GetItemAsync<string>("/api/me");

        Assert.True(result.Failed);
        Assert.Null(result.Reason);
    }

    [Fact]
    public async Task The_untyped_GetAsync_no_longer_throws_when_the_api_is_unreachable()
    {
        // Every one of the ~90 existing call sites gets this for free — GetAsync is now a wrapper
        // that keeps the value and drops the outcome, so its behaviour is unchanged except here.
        Assert.Null(await Client(new ThrowingHandler()).GetAsync<string>("/api/me"));
    }

    [Fact]
    public void Map_carries_the_outcome_and_does_not_silently_drop_it()
    {
        var expired = ItemResult<string>.SessionEnded().Map(s => s.Length);
        Assert.True(expired.SessionExpired);
        Assert.True(expired.Failed);

        var refused = ItemResult<string>.Failure("Not yours to see.").Map(s => s.Length);
        Assert.Equal("Not yours to see.", refused.Reason);

        Assert.Equal(5, ItemResult<string>.Ok("Sarah").Map(s => (int?)s.Length).Item);
    }
}
