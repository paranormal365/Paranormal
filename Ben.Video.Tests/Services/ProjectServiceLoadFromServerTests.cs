using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ben.Video.Editor.Extensions;
using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;
using Microsoft.Extensions.Options;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Unit tests for <see cref="ProjectService.LoadFromServerAsync"/>.
/// </summary>
public sealed class ProjectServiceLoadFromServerTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented               = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
        Converters                  = { new JsonStringEnumConverter() },
    };

    private sealed class StubHttpHandler(HttpStatusCode status, object? body) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken _)
        {
            LastRequest = request;
            var response = new HttpResponseMessage(status);
            if (body is not null)
                response.Content = new StringContent(
                    JsonSerializer.Serialize(body, JsonOpts),
                    System.Text.Encoding.UTF8,
                    "application/json");
            return Task.FromResult(response);
        }
    }

    private sealed class ThrowingHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken _)
            => throw new HttpRequestException("Network error");
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler);
    }

    private sealed class FakeJsRuntime : Microsoft.JSInterop.IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => throw new NotSupportedException();
        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier, CancellationToken ct, object?[]? args)
            => throw new NotSupportedException();
    }

    private static ProjectService CreateService(
        VideoEditorOptions options, HttpMessageHandler handler)
    {
        var opts    = Options.Create(options);
        var store   = new ClipStore(opts);
        var factory = new StubHttpClientFactory(handler);
        return new ProjectService(store, new MotionKeyframeService(), new FakeJsRuntime(), factory, opts);
    }

    private static ProjectFile MakeProjectFile(string name = "test") => new()
    {
        ProjectName = name,
        SavedAt     = DateTime.UtcNow,
        Tracks      = [],
        Markers     = [],
    };

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LoadFromServerAsync_NullUrl_AndNullOption_Throws()
    {
        var svc = CreateService(
            new VideoEditorOptions { DocumentSaveUrl = null },
            new ThrowingHttpHandler());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.LoadFromServerAsync());
    }

    [Fact]
    public async Task LoadFromServerAsync_UsesDocumentSaveUrl_WhenNoOverride()
    {
        const string expectedUrl = "https://api.example.com/api/projects/1";
        var project = MakeProjectFile("my-project");
        var handler = new StubHttpHandler(HttpStatusCode.OK, project);
        var svc     = CreateService(
            new VideoEditorOptions { DocumentSaveUrl = expectedUrl },
            handler);

        await svc.LoadFromServerAsync();

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(new Uri(expectedUrl), handler.LastRequest!.RequestUri);
        Assert.Equal(HttpMethod.Get, handler.LastRequest.Method);
    }

    [Fact]
    public async Task LoadFromServerAsync_UrlOverride_TakesPrecedenceOverOption()
    {
        const string optionUrl   = "https://api.example.com/api/projects/1";
        const string overrideUrl = "https://staging.example.com/api/projects/99";
        var project = MakeProjectFile();
        var handler = new StubHttpHandler(HttpStatusCode.OK, project);
        var svc     = CreateService(
            new VideoEditorOptions { DocumentSaveUrl = optionUrl },
            handler);

        await svc.LoadFromServerAsync(overrideUrl);

        Assert.Equal(new Uri(overrideUrl), handler.LastRequest!.RequestUri);
    }

    [Fact]
    public async Task LoadFromServerAsync_ValidResponse_ReturnsDeserializedProjectFile()
    {
        var project = MakeProjectFile("loaded-project");
        var handler = new StubHttpHandler(HttpStatusCode.OK, project);
        var svc     = CreateService(
            new VideoEditorOptions { DocumentSaveUrl = "https://api.example.com/api/projects/1" },
            handler);

        var result = await svc.LoadFromServerAsync();

        Assert.NotNull(result);
        Assert.Equal("loaded-project", result!.ProjectName);
    }

    [Fact]
    public async Task LoadFromServerAsync_NetworkError_ReturnsNull()
    {
        var svc = CreateService(
            new VideoEditorOptions { DocumentSaveUrl = "https://api.example.com/api/projects/1" },
            new ThrowingHttpHandler());

        var result = await svc.LoadFromServerAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task LoadFromServerAsync_ServerError_ReturnsNull()
    {
        var handler = new StubHttpHandler(HttpStatusCode.InternalServerError, null);
        var svc     = CreateService(
            new VideoEditorOptions { DocumentSaveUrl = "https://api.example.com/api/projects/1" },
            handler);

        // GetFromJsonAsync throws on non-success when body is empty — should be caught and return null
        var result = await svc.LoadFromServerAsync();

        Assert.Null(result);
    }

    [Fact]
    public void DocumentSaveUrl_DefaultsToNull_InVideoEditorOptions()
    {
        Assert.Null(new VideoEditorOptions().DocumentSaveUrl);
    }
}
