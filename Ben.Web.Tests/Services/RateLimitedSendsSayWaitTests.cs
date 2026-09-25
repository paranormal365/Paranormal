using System.Net;
using System.Text;
using Ben.Web.Services.WebApi;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// A request the API's rate limiter turns away is told to wait, not that something broke.
/// </summary>
/// <remarks>
/// Found by the storefront's browser suite (S4.11): the checkout allows ten tries a minute, the
/// limiter answers 429 with a JSON body, the prose test drops JSON — and the page said "The
/// checkout couldn't be started just now", which was false and gave the buyer nothing to do.
/// </remarks>
public sealed class RateLimitedSendsSayWaitTests
{
    private sealed class CannedHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(response);
    }

    private static WebApiClient Client() => new(
        new HttpClient(new CannedHandler(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("{\"error\":\"Too many requests. Please retry shortly.\"}", Encoding.UTF8, "application/json"),
        })) { BaseAddress = new Uri("http://unit.test") },
        new WebApiTokenStore());

    [Fact]
    public async Task A_refusal_expecting_send_says_wait_a_minute()
    {
        var (_, error) = await Client().SendExpectingReasonAsync<object, object>(HttpMethod.Post, "/api/store/cart/items", new { });
        Assert.Equal(WebApiClient.TooManyTries, error);
    }

    [Fact]
    public async Task A_conflict_expecting_send_says_wait_a_minute()
    {
        var (_, error, conflict) = await Client().SendExpectingConflictAsync<object, object, object>(HttpMethod.Post, "/api/store/checkout/payment-intent", new { });
        Assert.Equal(WebApiClient.TooManyTries, error);
        Assert.Null(conflict);
    }
}
