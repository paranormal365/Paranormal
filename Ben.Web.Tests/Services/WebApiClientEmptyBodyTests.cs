using Ben.Web.Services.WebApi;
using Microsoft.Extensions.Options;
using System.Net;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// An empty 204 from the server is a null answer, not a crash.
/// </summary>
/// <remarks>
/// <c>Ok(null)</c> in a controller becomes 204 with an empty body, and
/// <c>ReadFromJsonAsync</c> throws on an empty stream. That exception escaped a page's
/// <c>OnInitializedAsync</c> and terminated the circuit — the Price Bands screen died on
/// production precisely when the price list was HEALTHY, because healthy is when the validation
/// endpoint answers "nothing to report". Reported live by Ben on 2026-08-22; the browser log's
/// Telerik frames were the aftermath of the dead circuit, not the cause.
/// </remarks>
public sealed class WebApiClientEmptyBodyTests
{
    private sealed class CannedHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(response);
    }

    private static WebApiClient Client(HttpResponseMessage canned) =>
        new(new HttpClient(new CannedHandler(canned)) { BaseAddress = new Uri("http://unit.test") },
            new WebApiTokenStore());

    [Fact]
    public async Task A_204_with_no_body_reads_as_null_rather_than_throwing()
    {
        var result = await Client(new HttpResponseMessage(HttpStatusCode.NoContent))
            .GetAsync<string?>("/api/admin/subscription-tiers/validation");

        Assert.Null(result);
    }

    [Fact]
    public async Task A_200_with_a_zero_length_body_reads_as_null_rather_than_throwing()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") };
        response.Content.Headers.ContentLength = 0;

        Assert.Null(await Client(response).GetAsync<string?>("/x"));
    }

    [Fact]
    public async Task A_real_body_still_deserializes()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent("\"the list has a gap\"", System.Text.Encoding.UTF8, "application/json") };

        Assert.Equal("the list has a gap", await Client(response).GetAsync<string?>("/x"));
    }

    // ── The same hole in POST and PUT (2026-09-09) ───────────────────────────
    //
    // GetAsync learned this in August; PostAsync, PutAsync and the two anonymous posts did not.
    // CompleteMyOnboardingAsync posts to a 204 endpoint, the throw was swallowed by the page's own
    // catch, and the first-run wizard was never stamped as answered — so every account made on the
    // live site was asked to set itself up again on every visit, and Skip could not stop it.

    [Fact]
    public async Task A_post_to_a_void_endpoint_completes_rather_than_throwing()
    {
        var result = await Client(new HttpResponseMessage(HttpStatusCode.NoContent))
            .PostAsync<object, object>("/api/me/onboarding/complete", new { });

        Assert.Null(result);
    }

    [Fact]
    public async Task A_put_to_a_void_endpoint_completes_rather_than_throwing()
    {
        var result = await Client(new HttpResponseMessage(HttpStatusCode.NoContent))
            .PutAsync<object, object>("/api/anything", new { });

        Assert.Null(result);
    }

    [Fact]
    public async Task An_anonymous_post_to_a_void_endpoint_completes_rather_than_throwing()
    {
        var result = await Client(new HttpResponseMessage(HttpStatusCode.NoContent))
            .PostAnonymousAsync<object, object>("/api/public/anything", new { });

        Assert.Null(result);
    }

    [Fact]
    public async Task A_post_that_does_answer_still_deserializes()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent("\"kept\"", System.Text.Encoding.UTF8, "application/json") };

        Assert.Equal("kept", await Client(response).PostAsync<object, string>("/x", new { }));
    }
}
