using System.Net;
using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Ben.Canvas.Tests.Support;

// Lifted from Ben.Wasm.Video.Tests/EditorHandoffServiceTests.cs so every canvas test uses the same
// doubles: an HTTP stub, a throwing handler, a JS runtime that records calls, one that does nothing,
// and a NavigationManager with a settable address. No mocking library.

/// <summary>
/// Answers every request with one queued response (or the last one, repeated) and records what was sent.
/// </summary>
public sealed class StubHandler : HttpMessageHandler
{
    private readonly List<(HttpStatusCode Status, string Body)> _responses = [];

    public StubHandler(HttpStatusCode status, string body)
    {
        _responses.Add((status, body));
    }

    /// <summary>Adds the response for the next request; once they run out the last one repeats.</summary>
    public StubHandler Then(HttpStatusCode status, string body)
    {
        _responses.Add((status, body));
        return this;
    }

    public string LastBody { get; private set; } = string.Empty;
    public string LastUrl { get; private set; } = string.Empty;
    public HttpMethod? LastMethod { get; private set; }
    public List<HttpRequestMessage> Requests { get; } = [];
    public List<string> Urls { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        LastUrl = request.RequestUri?.ToString() ?? string.Empty;
        Urls.Add(LastUrl);
        LastMethod = request.Method;
        LastBody = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(ct);

        var (status, body) = _responses[Math.Min(Requests.Count, _responses.Count) - 1];

        return new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }
}

/// <summary>A connection that never lands.</summary>
public sealed class ThrowingHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        => throw new HttpRequestException("no route to host");
}

/// <summary>Records what the app asked the browser to do; answers with configured values or defaults.</summary>
public sealed class RecordingJs : IJSRuntime
{
    private readonly Dictionary<string, object?> _returns = new(StringComparer.Ordinal);

    public List<(string Identifier, object?[]? Args)> Calls { get; } = [];

    /// <summary>Makes <paramref name="identifier"/> answer <paramref name="value"/>.</summary>
    public RecordingJs Returns(string identifier, object? value)
    {
        _returns[identifier] = value;
        return this;
    }

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
    {
        Calls.Add((identifier, args));
        return _returns.TryGetValue(identifier, out var value) && value is TValue typed
            ? ValueTask.FromResult(typed)
            : ValueTask.FromResult(default(TValue)!);
    }

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken ct, object?[]? args)
        => InvokeAsync<TValue>(identifier, args);

    /// <summary>The URL history.replaceState last left the page showing, or null.</summary>
    public string? RewrittenTo => Calls
        .Where(c => c.Identifier == "history.replaceState")
        .Select(c => c.Args?.ElementAtOrDefault(2) as string)
        .LastOrDefault();
}

/// <summary>A JS runtime that does nothing and answers defaults.</summary>
public sealed class NoJs : IJSRuntime
{
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        => ValueTask.FromResult(default(TValue)!);

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken ct, object?[]? args)
        => InvokeAsync<TValue>(identifier, args);
}

/// <summary>A NavigationManager whose address can be set and whose navigations are recorded.</summary>
public sealed class FakeNavigation : NavigationManager
{
    public FakeNavigation(string baseUri, string? uri = null) => Initialize(baseUri, uri ?? baseUri);

    public string? LastNavigatedTo { get; private set; }

    protected override void NavigateToCore(string uri, NavigationOptions options)
    {
        LastNavigatedTo = uri;
        Uri = ToAbsoluteUri(uri).ToString();
    }
}

/// <summary>A factory that hands out one client over one handler, whatever the name.</summary>
public sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}
