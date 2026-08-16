using System.Net;
using System.Text;

namespace Ben.Video.Sidecar.Tests;

public sealed class SourceEndpointsTests : IClassFixture<SidecarWebApplicationFactory>
{
    private readonly SidecarWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public SourceEndpointsTests(SidecarWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateAuthenticatedClient(token: factory.ReadGeneratedPairingToken());
    }

    private static string RandomClipId() => Guid.NewGuid().ToString();

    [Fact]
    public async Task Head_UnknownClip_Returns404()
    {
        var response = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Head, $"/v1/sources/{RandomClipId()}?ext=mp4"));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Regression test for a real bug found during live verification: the HEAD handler didn't
    /// explicitly set Content-Length, so under HTTP/1.1 keep-alive a client had no way to know
    /// the (empty) response body had already ended and would hang indefinitely waiting for more
    /// bytes that were never coming. <see cref="HttpClient"/> masks this less obviously than raw
    /// curl did, so this test asserts the header directly rather than relying on a hang to prove
    /// its absence.
    /// </summary>
    [Fact]
    public async Task Head_AlwaysSetsExplicitContentLength()
    {
        var response = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Head, $"/v1/sources/{RandomClipId()}?ext=mp4"));

        Assert.NotNull(response.Content.Headers.ContentLength);
        Assert.Equal(0, response.Content.Headers.ContentLength);
    }

    [Fact]
    public async Task PutThenHead_ReturnsSizeAndExists()
    {
        var clipId = RandomClipId();
        var bytes = Encoding.UTF8.GetBytes("fake video bytes for a round-trip test");

        var putResponse = await _client.PutAsync(
            $"/v1/sources/{clipId}?ext=mp4", new ByteArrayContent(bytes));
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);

        var headResponse = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Head, $"/v1/sources/{clipId}?ext=mp4"));
        Assert.Equal(HttpStatusCode.OK, headResponse.StatusCode);
        Assert.Equal(bytes.Length.ToString(), headResponse.Headers.GetValues("X-BenVideo-Size").Single());
    }

    [Fact]
    public async Task PutThenDeleteThenHead_ReturnsNotFound()
    {
        var clipId = RandomClipId();
        await _client.PutAsync($"/v1/sources/{clipId}?ext=mp4", new ByteArrayContent([1, 2, 3]));

        var deleteResponse = await _client.DeleteAsync($"/v1/sources/{clipId}?ext=mp4");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var headResponse = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Head, $"/v1/sources/{clipId}?ext=mp4"));
        Assert.Equal(HttpStatusCode.NotFound, headResponse.StatusCode);
    }

    // ── Threat T4/T5: no request-supplied string ever reaches a filesystem path ──

    [Theory]
    [InlineData("..%2f..%2f..%2fetc%2fpasswd")]
    [InlineData("not-a-guid-at-all")]
    [InlineData("11111111-2222-3333-4444-5555555555555555")] // too long
    public async Task InvalidClipId_Returns400(string hostileId)
    {
        var response = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Head, $"/v1/sources/{hostileId}?ext=mp4"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("exe")]
    [InlineData("sh")]
    [InlineData("dll")]
    [InlineData("")]
    public async Task DisallowedExtension_PutIsRejected(string ext)
    {
        var response = await _client.PutAsync(
            $"/v1/sources/{RandomClipId()}?ext={ext}", new ByteArrayContent([1, 2, 3]));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MissingExtension_HeadIsRejected()
    {
        var response = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Head, $"/v1/sources/{RandomClipId()}"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

}
