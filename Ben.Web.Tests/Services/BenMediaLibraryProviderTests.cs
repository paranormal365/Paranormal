using System.Net;
using System.Text;
using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;
using Ben.Web.Services;
using Ben.Web.Services.WebApi;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Listing somebody's uploaded media from the site, and what happens when the server says no.
/// </summary>
/// <remarks>
/// This provider had no tests at all (2026-09-05 audit, site-17), and its most important behaviour
/// is one it did not used to have: telling a refusal apart from an empty library. Returning an
/// empty list for any failed response showed the Server tab as "no files" to somebody whose
/// session had simply expired, which reads as "you have not uploaded anything" — a different and
/// untrue thing (site-11).
/// </remarks>
public sealed class BenMediaLibraryProviderTests
{
    // ── A refusal is not an empty library ─────────────────────────────────────

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task A_refusal_is_reported_as_a_refusal(HttpStatusCode status)
    {
        var provider = Create(status, "[]");

        await Assert.ThrowsAsync<MediaLibraryUnauthorizedException>(() => provider.GetFilesAsync());
    }

    /// <summary>
    /// Anything else that fails still reads as an empty list, deliberately. A five-hundred is a
    /// fault the panel already reports elsewhere; only a refusal has an action attached to it.
    /// </summary>
    [Fact]
    public async Task Another_kind_of_failure_is_not_mistaken_for_a_refusal()
    {
        var provider = Create(HttpStatusCode.InternalServerError, "");

        Assert.Empty(await provider.GetFilesAsync());
    }

    // ── What comes back ───────────────────────────────────────────────────────

    [Fact]
    public async Task Media_the_editor_can_use_comes_back()
    {
        var provider = Create(HttpStatusCode.OK, """
            [
              { "id": "11111111-1111-1111-1111-111111111111", "fileName": "porch.mp4",
                "contentType": "video/mp4", "fileSize": 4096 }
            ]
            """);

        var file = Assert.Single(await provider.GetFilesAsync());

        Assert.Equal("porch.mp4", file.FileName);
        Assert.Equal(4096, file.FileSize);
        Assert.True(file.IsVideo);
    }

    /// <summary>
    /// The library holds a case's PDFs and everything else besides. Handing those to a video
    /// editor's media panel would offer somebody a document to put on a timeline.
    /// </summary>
    [Fact]
    public async Task Files_the_editor_cannot_use_are_left_out()
    {
        var provider = Create(HttpStatusCode.OK, """
            [
              { "id": "11111111-1111-1111-1111-111111111111", "fileName": "report.pdf",
                "contentType": "application/pdf", "fileSize": 100 },
              { "id": "22222222-2222-2222-2222-222222222222", "fileName": "evp.m4a",
                "contentType": "audio/mp4", "fileSize": 200 },
              { "id": "33333333-3333-3333-3333-333333333333", "fileName": "site.jpg",
                "contentType": "image/jpeg", "fileSize": 300 }
            ]
            """);

        var names = (await provider.GetFilesAsync()).Select(f => f.FileName).ToList();

        Assert.Equal(["evp.m4a", "site.jpg"], names);
    }

    [Fact]
    public async Task A_file_with_no_content_type_is_left_out_rather_than_guessed_at()
    {
        var provider = Create(HttpStatusCode.OK, """
            [{ "id": "11111111-1111-1111-1111-111111111111", "fileName": "mystery", "fileSize": 1 }]
            """);

        Assert.Empty(await provider.GetFilesAsync());
    }

    [Fact]
    public async Task An_empty_library_really_is_empty()
    {
        var provider = Create(HttpStatusCode.OK, "[]");

        Assert.Empty(await provider.GetFilesAsync());
    }

    // ── Letting the browser fetch the file itself ─────────────────────────────

    /// <summary>
    /// With a ticket minter the browser is given a URL and fetches the file itself.
    /// </summary>
    /// <remarks>
    /// The alternative pulls the file into the server's memory, copies it again into a byte array
    /// and ships it over the circuit — three copies of a file the browser could have fetched, with
    /// a 2 GB ceiling on the way (2026-09-05 audit, site-2 and media-6).
    /// </remarks>
    [Fact]
    public async Task The_browser_is_given_a_url_when_the_host_can_mint_one()
    {
        var minter = new Mock<IMediaTicketMinter>();
        minter.Setup(m => m.Mint(It.IsAny<Guid>(), "download")).Returns("/media/abc/download?t=x");

        var provider = Create(HttpStatusCode.OK, "[]", minter.Object);

        Assert.Equal("/media/abc/download?t=x", await provider.GetDownloadUrlAsync(Guid.NewGuid()));
    }

    /// <summary>
    /// Without one, null — which is not a failure. The caller falls back to the byte path, which
    /// is what makes a missing registration degrade rather than break.
    /// </summary>
    [Fact]
    public async Task Without_a_minter_there_is_no_url_and_that_is_not_an_error()
    {
        var provider = Create(HttpStatusCode.OK, "[]");

        Assert.Null(await provider.GetDownloadUrlAsync(Guid.NewGuid()));
    }

    // ── Support ───────────────────────────────────────────────────────────────

    private static BenMediaLibraryProvider Create(
        HttpStatusCode status, string body, IMediaTicketMinter? minter = null)
    {
        var handler = new StubHandler(status, body);
        var tokens  = new Mock<IWebApiTokenStore>();
        tokens.SetupGet(t => t.AccessToken).Returns("token");

        return new BenMediaLibraryProvider(
            new SingleClientFactory(handler),
            tokens.Object,
            Options.Create(new WebApiOptions { BaseUrl = "https://api.test" }),
            minter);
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
