using System.Net;
using System.Text;
using System.Text.Json;
using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;
using Microsoft.Extensions.Options;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Unit tests verifying that <see cref="ProjectService.SaveToServerAsync"/> and
/// <see cref="ProjectService.LoadFromServerAsync"/> fire <see cref="IProgress{T}"/>
/// callbacks with accurate <see cref="TransferProgress"/> snapshots.
/// </summary>
public sealed class ProjectServiceTransferProgressTests
{
    // ── shared fakes ─────────────────────────────────────────────────────────

    private sealed class FakeJsRuntime : Microsoft.JSInterop.IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => throw new NotSupportedException();
        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier, CancellationToken ct, object?[]? args)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Synchronous IProgress stub so callbacks fire immediately on the calling
    /// thread rather than being dispatched via a SynchronizationContext.
    /// </summary>
    private sealed class SyncProgress<T>(List<T> reports) : IProgress<T>
    {
        public void Report(T value) => reports.Add(value);
    }

    /// <summary>
    /// Handler that reads the request body fully (so ProgressContent.SerializeToStreamAsync
    /// runs to completion) and returns a canned JSON response.
    /// </summary>
    private sealed class ReadingStubHandler(
        string responseBody,
        bool includeContentLength = true,
        HttpStatusCode status = HttpStatusCode.OK,
        Stream? overrideStream = null) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken _)
        {
            if (request.Content is not null)
                await request.Content.ReadAsStringAsync();

            var bytes = Encoding.UTF8.GetBytes(responseBody);

            HttpContent content;
            if (includeContentLength)
            {
                var ba = new ByteArrayContent(bytes);
                ba.Headers.ContentType =
                    new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
                content = ba;
            }
            else
            {
                // Use a non-seekable stream so HttpClient cannot compute Content-Length.
                var sc = new StreamContent(overrideStream ?? new MemoryStream(bytes));
                sc.Headers.ContentType =
                    new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
                content = sc;
            }

            return new HttpResponseMessage(status) { Content = content };
        }
    }

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler);
    }

    private static ProjectService CreateService(
        VideoEditorOptions options, HttpMessageHandler handler)
    {
        var opts  = Options.Create(options);
        var store = new ClipStore(opts);
        return new ProjectService(store, new MotionKeyframeService(), new FakeJsRuntime(), new StubFactory(handler), opts);
    }

    // ── upload progress ───────────────────────────────────────────────────────

    [Fact]
    public async Task SaveToServer_WithProgress_ReportsAtLeastOneFiredCallback()
    {
        var handler = new ReadingStubHandler("{\"id\":\"1\"}");
        var svc     = CreateService(
            new VideoEditorOptions { DocumentPostUrl = "https://example.com/save" },
            handler);

        var reports  = new List<TransferProgress>();
        var progress = new SyncProgress<TransferProgress>(reports);

        var response = await svc.SaveToServerAsync("myproject", progress: progress);

        Assert.True(response.IsSuccessStatusCode);
        Assert.NotEmpty(reports);
    }

    [Fact]
    public async Task SaveToServer_WithProgress_FinalReportHasBytesGreaterThanZero()
    {
        var handler = new ReadingStubHandler("{\"ok\":true}");
        var svc     = CreateService(
            new VideoEditorOptions { DocumentPostUrl = "https://example.com/save" },
            handler);

        var reports  = new List<TransferProgress>();
        var progress = new SyncProgress<TransferProgress>(reports);

        await svc.SaveToServerAsync("proj", progress: progress);

        Assert.NotEmpty(reports);
        Assert.True(reports[^1].Bytes > 0, "Expected at least one byte to be reported.");
    }

    [Fact]
    public async Task SaveToServer_WithProgress_PercentIs100WhenFinalChunkFlushed()
    {
        var handler = new ReadingStubHandler("{\"ok\":true}");
        var svc     = CreateService(
            new VideoEditorOptions { DocumentPostUrl = "https://example.com/save" },
            handler);

        var reports  = new List<TransferProgress>();
        var progress = new SyncProgress<TransferProgress>(reports);

        await svc.SaveToServerAsync("proj", progress: progress);

        Assert.NotEmpty(reports);
        Assert.Equal(100, reports[^1].Percent);
    }

    // ── download progress ─────────────────────────────────────────────────────

    private static string BuildMinimalProjectJson()
    {
        var pf = new ProjectFile
        {
            ProjectName = "test",
            Tracks      = [],
            Markers     = [],
        };
        return JsonSerializer.Serialize(pf,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    [Fact]
    public async Task LoadFromServer_WithProgress_ReportsAtLeastOneFiredCallback()
    {
        var json    = BuildMinimalProjectJson();
        var handler = new ReadingStubHandler(json, includeContentLength: true);
        var svc     = CreateService(
            new VideoEditorOptions { DocumentSaveUrl = "https://example.com/load" },
            handler);

        var reports  = new List<TransferProgress>();
        var progress = new SyncProgress<TransferProgress>(reports);

        var result = await svc.LoadFromServerAsync(progress: progress);

        Assert.NotNull(result);
        Assert.NotEmpty(reports);
    }

    [Fact]
    public async Task LoadFromServer_WithProgress_FinalPercentIs100WhenContentLengthKnown()
    {
        var json    = BuildMinimalProjectJson();
        var handler = new ReadingStubHandler(json, includeContentLength: true);
        var svc     = CreateService(
            new VideoEditorOptions { DocumentSaveUrl = "https://example.com/load" },
            handler);

        var reports  = new List<TransferProgress>();
        var progress = new SyncProgress<TransferProgress>(reports);

        var result = await svc.LoadFromServerAsync(progress: progress);

        Assert.NotNull(result);
        Assert.NotEmpty(reports);
        Assert.Equal(100, reports[^1].Percent);
    }

    [Fact]
    public async Task LoadFromServer_WithProgress_PercentIsNegativeWhenNoContentLength()
    {
        var json  = BuildMinimalProjectJson();
        var bytes = Encoding.UTF8.GetBytes(json);

        // Wrap MemoryStream in a non-seekable stream so StreamContent cannot
        // compute Content-Length and therefore omits the header.
        var nonSeekable = new NonSeekableStream(new MemoryStream(bytes));
        var handler = new ReadingStubHandler(json, includeContentLength: false,
            overrideStream: nonSeekable);
        var svc = CreateService(
            new VideoEditorOptions { DocumentSaveUrl = "https://example.com/load" },
            handler);

        var reports  = new List<TransferProgress>();
        var progress = new SyncProgress<TransferProgress>(reports);

        var result = await svc.LoadFromServerAsync(progress: progress);

        Assert.NotNull(result);
        Assert.True(reports.All(r => r.TotalBytes == -1),
            "Expected TotalBytes=-1 (no Content-Length header).");
    }

    private sealed class NonSeekableStream(Stream inner) : Stream
    {
        public override bool CanRead  => true;
        public override bool CanSeek  => false;
        public override bool CanWrite => false;
        public override long Length   => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
        public override int  Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            => inner.ReadAsync(buffer, offset, count, ct);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
            => inner.ReadAsync(buffer, ct);
        public override void Flush()                                      => inner.Flush();
        public override long Seek(long offset, SeekOrigin origin)         => throw new NotSupportedException();
        public override void SetLength(long value)                        => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count)  => throw new NotSupportedException();
    }

    // ── TransferProgress model ────────────────────────────────────────────────

    [Theory]
    [InlineData(512,         1_024,    50)]
    [InlineData(1_024,       1_024,   100)]
    [InlineData(0,           1_024,     0)]
    [InlineData(0,              -1,    -1)]
    public void TransferProgress_Percent_IsCorrect(long bytes, long total, int expected)
    {
        var tp = new TransferProgress { Bytes = bytes, TotalBytes = total };
        Assert.Equal(expected, tp.Percent);
    }

    [Theory]
    [InlineData(512,              "512 B")]
    [InlineData(1_536,            "1.5 KB")]
    [InlineData(2_097_152,        "2.0 MB")]
    [InlineData(1_073_741_824,    "1.0 GB")]
    public void TransferProgress_FormattedBytes_IsHumanReadable(long bytes, string expected)
    {
        var tp = new TransferProgress { Bytes = bytes, TotalBytes = -1 };
        Assert.Equal(expected, tp.FormattedBytes);
    }

    [Fact]
    public void TransferProgress_FormattedTotal_IsQuestionMarkWhenUnknown()
    {
        var tp = new TransferProgress { Bytes = 100, TotalBytes = -1 };
        Assert.Equal("?", tp.FormattedTotal);
    }
}
