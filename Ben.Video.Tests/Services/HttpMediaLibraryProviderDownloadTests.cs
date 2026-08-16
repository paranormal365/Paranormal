using System.Net;
using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;
using Microsoft.Extensions.Options;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Phase 150 — <see cref="HttpMediaLibraryProvider.DownloadFileAsync"/> had zero prior test
/// coverage. Covers the new streaming-with-progress read (added so the Server tab's own
/// download-then-cache flow can show a real per-file progress bar instead of jumping from 0% to
/// 100%) — correctness of the returned bytes, monotonic progress reporting when Content-Length is
/// present, and the best-effort "no progress calls, but still correct bytes" fallback when it
/// isn't (some real hosts may not set it, e.g. chunked responses).
/// </summary>
public sealed class HttpMediaLibraryProviderDownloadTests
{
    // ── Fakes ─────────────────────────────────────────────────────────────────

    private sealed class StubHttpHandler(byte[] body, bool includeContentLength) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var content = new ByteArrayContent(body);
            if (!includeContentLength)
                content.Headers.ContentLength = null; // simulate a chunked/unknown-length response
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler);
    }

    private static HttpMediaLibraryProvider CreateProvider(byte[] body, bool includeContentLength = true)
    {
        var opts    = Options.Create(new VideoEditorOptions { MediaLibraryBaseUrl = "https://example.test" });
        var handler = new StubHttpHandler(body, includeContentLength);
        var factory = new StubHttpClientFactory(handler);
        return new HttpMediaLibraryProvider(factory, opts);
    }

    private sealed class RecordingProgress : IProgress<double>
    {
        public List<double> Reports { get; } = [];
        public void Report(double value) => Reports.Add(value);
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DownloadFileAsync_ReturnsExactBytes()
    {
        var body = Enumerable.Range(0, 200_000).Select(i => (byte)(i % 251)).ToArray();
        var provider = CreateProvider(body);

        var result = await provider.DownloadFileAsync(Guid.NewGuid());

        Assert.Equal(body, result);
    }

    [Fact]
    public async Task DownloadFileAsync_WithContentLength_ReportsMonotonicProgressEndingAtOne()
    {
        var body = new byte[500_000]; // large enough to span multiple 80KB read chunks
        Random.Shared.NextBytes(body);
        var provider = CreateProvider(body);
        var progress = new RecordingProgress();

        var result = await provider.DownloadFileAsync(Guid.NewGuid(), progress: progress);

        Assert.Equal(body, result);
        Assert.NotEmpty(progress.Reports);
        Assert.True(progress.Reports.SequenceEqual(progress.Reports.OrderBy(v => v)),
            "progress reports must be non-decreasing");
        Assert.All(progress.Reports, v => Assert.InRange(v, 0.0, 1.0));
        Assert.Equal(1.0, progress.Reports[^1]);
    }

    [Fact]
    public async Task DownloadFileAsync_WithoutContentLength_StillReturnsCorrectBytes_NoProgressRequired()
    {
        var body = new byte[50_000];
        Random.Shared.NextBytes(body);
        var provider = CreateProvider(body, includeContentLength: false);
        var progress = new RecordingProgress();

        var result = await provider.DownloadFileAsync(Guid.NewGuid(), progress: progress);

        // Progress is documented as best-effort on the interface — no guarantee of any reports
        // when the response doesn't carry a length, but the bytes themselves must still be correct.
        Assert.Equal(body, result);
    }

    [Fact]
    public async Task DownloadFileAsync_NullProgress_DoesNotThrow()
    {
        var body = new byte[1000];
        var provider = CreateProvider(body);

        var result = await provider.DownloadFileAsync(Guid.NewGuid(), progress: null);

        Assert.Equal(body, result);
    }

    [Fact]
    public async Task DownloadFileAsync_EmptyFile_ReturnsEmptyArray()
    {
        var provider = CreateProvider([]);

        var result = await provider.DownloadFileAsync(Guid.NewGuid());

        Assert.Empty(result);
    }
}
