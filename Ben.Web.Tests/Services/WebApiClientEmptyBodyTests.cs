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
}
