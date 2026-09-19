using Ben.Web.Services.WebApi;
using System.Net;
using System.Net.Http.Headers;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// A successful call that returns no body must be a success, not an exception.
/// </summary>
/// <remarks>
/// <para>Found 2026-09-02 by trying to change a password on the live site: the button sat on
/// "Saving…" for ever. <c>POST api/me/password</c> answers <c>NoContent</c>, and every one of
/// these helpers then called <c>ReadFromJsonAsync</c> on an empty stream, which throws. Nothing
/// between the helper and the button caught it, so the page's busy flag was never cleared — while
/// the password had in fact been changed. The worst shape of bug: it looks like nothing happened
/// and something did.</para>
///
/// <para><c>SendItemAsync</c> already carried this guard (the Price Bands screen died on the same
/// trap); the other three never got it. Each test below fails against the unguarded code with a
/// <c>JsonException</c>, which is the point.</para>
/// </remarks>
public sealed class EmptyBodySuccessTests
{
    private sealed class CannedHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(response);
    }

    private static WebApiClient Client(HttpResponseMessage response) =>
        new(new HttpClient(new CannedHandler(response)) { BaseAddress = new Uri("http://unit.test") },
            new WebApiTokenStore());

    /// <summary>204 with no content at all — what <c>return NoContent()</c> produces.</summary>
    private static HttpResponseMessage NoContent() => new(HttpStatusCode.NoContent);

    /// <summary>200 with a zero-length body — what some proxies turn a 204 into.</summary>
    private static HttpResponseMessage EmptyOk()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([]),
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        response.Content.Headers.ContentLength = 0;
        return response;
    }

    [Fact]
    public async Task SendExpectingReason_treats_NoContent_as_success_with_no_reason()
    {
        var (result, error) = await Client(NoContent())
            .SendExpectingReasonAsync<object, object>(HttpMethod.Post, "/api/me/password", new { });

        Assert.Null(error);     // no refusal to show
        Assert.Null(result);    // and nothing to deserialize
    }

    [Fact]
    public async Task SendExpectingReason_treats_an_empty_200_as_success()
    {
        var (result, error) = await Client(EmptyOk())
            .SendExpectingReasonAsync<object, object>(HttpMethod.Post, "/api/me/password", new { });

        Assert.Null(error);
        Assert.Null(result);
    }

    [Fact]
    public async Task PostExpectingConflict_treats_NoContent_as_success()
    {
        var (result, conflict) = await Client(NoContent())
            .PostExpectingConflictAsync<object, object, object>("/api/equipment/brands", new { });

        Assert.Null(conflict);
        Assert.Null(result);
    }

    [Fact]
    public async Task PostMultipartExpectingReason_treats_NoContent_as_success()
    {
        using var content = new MultipartFormDataContent { { new ByteArrayContent([1, 2, 3]), "file", "a.bin" } };

        var (result, error) = await Client(NoContent())
            .PostMultipartExpectingReasonAsync<object>("/api/uploads", content);

        Assert.Null(error);
        Assert.Null(result);
    }

    /// <summary>
    /// The guard must not swallow a real refusal: a 400 with our own sentence still comes back as
    /// that sentence, so the panel shows the server's words rather than a shrug.
    /// </summary>
    [Fact]
    public async Task A_refusal_still_arrives_as_its_sentence()
    {
        var refusal = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("Enter your current password to change it."),
        };

        var (_, error) = await Client(refusal)
            .SendExpectingReasonAsync<object, object>(HttpMethod.Post, "/api/me/password", new { });

        Assert.Equal("Enter your current password to change it.", error);
    }
}
