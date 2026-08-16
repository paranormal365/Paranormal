using System.Net;
using Ben.Video.Editor.Extensions;
using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;
using Microsoft.Extensions.Options;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Unit tests for <see cref="ProjectService.SaveToServerAsync"/>.
/// Uses a fake <see cref="IHttpClientFactory"/> backed by a stub handler
/// so no real network calls are made.
/// </summary>
public sealed class ProjectServiceSaveToServerTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private sealed class StubHttpHandler : HttpMessageHandler
    {
        public HttpRequestMessage?  LastRequest  { get; private set; }
        public HttpStatusCode       StatusCode   { get; set; } = HttpStatusCode.OK;
        public string               ResponseBody { get; set; } = "{\"id\":\"42\"}";

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken _)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(StatusCode)
            {
                Content = new StringContent(ResponseBody),
            });
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler);
    }

    private static ProjectService CreateService(
        VideoEditorOptions? options = null,
        HttpMessageHandler? handler = null)
    {
        var opts    = Options.Create(options ?? new VideoEditorOptions());
        var factory = new StubHttpClientFactory(handler ?? new StubHttpHandler());
        // ProjectService ctor: (ClipStore, IJSRuntime, IHttpClientFactory, IOptions)
        var store   = new ClipStore(opts);
        return new ProjectService(store, new MotionKeyframeService(), new FakeJsRuntime(), factory, opts);
    }

    // Minimal IJSRuntime stub — SaveToServerAsync does not call JS.
    private sealed class FakeJsRuntime : Microsoft.JSInterop.IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => throw new NotSupportedException();
        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier, CancellationToken ct, object?[]? args)
            => throw new NotSupportedException();
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SaveToServerAsync_NullUrl_AndNullOption_Throws()
    {
        var svc = CreateService(new VideoEditorOptions { DocumentPostUrl = null });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.SaveToServerAsync("myproject"));
    }

    [Fact]
    public async Task SaveToServerAsync_UsesDocumentPostUrl_WhenNoOverride()
    {
        const string expectedUrl = "https://api.example.com/api/projects";
        var handler = new StubHttpHandler();
        var svc     = CreateService(
            new VideoEditorOptions { DocumentPostUrl = expectedUrl },
            handler);

        await svc.SaveToServerAsync("myproject");

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(new Uri(expectedUrl), handler.LastRequest!.RequestUri);
        Assert.Equal(HttpMethod.Post,      handler.LastRequest.Method);
    }

    [Fact]
    public async Task SaveToServerAsync_UrlOverride_TakesPrecedenceOverOption()
    {
        const string optionUrl   = "https://api.example.com/api/projects";
        const string overrideUrl = "https://staging.example.com/api/projects";
        var handler = new StubHttpHandler();
        var svc     = CreateService(
            new VideoEditorOptions { DocumentPostUrl = optionUrl },
            handler);

        await svc.SaveToServerAsync("myproject", urlOverride: overrideUrl);

        Assert.Equal(new Uri(overrideUrl), handler.LastRequest!.RequestUri);
    }

    [Fact]
    public async Task SaveToServerAsync_ReturnsServerResponse()
    {
        var handler = new StubHttpHandler { StatusCode = HttpStatusCode.Created };
        var svc     = CreateService(
            new VideoEditorOptions { DocumentPostUrl = "https://api.example.com/api/projects" },
            handler);

        var response = await svc.SaveToServerAsync("myproject");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task SaveToServerAsync_HttpClientName_IsProjectPersistenceName()
    {
        // Verify the constant used matches the registration in ServiceCollectionExtensions
        Assert.Equal("BenVideo.ProjectPersistence",
            ServiceCollectionExtensions.ProjectPersistenceHttpClientName);
    }

    [Fact]
    public void DocumentPostUrl_DefaultsToNull_InVideoEditorOptions()
    {
        Assert.Null(new VideoEditorOptions().DocumentPostUrl);
    }

    [Fact]
    public void DocumentSaveUrl_DefaultsToNull_InVideoEditorOptions()
    {
        Assert.Null(new VideoEditorOptions().DocumentSaveUrl);
    }
}
