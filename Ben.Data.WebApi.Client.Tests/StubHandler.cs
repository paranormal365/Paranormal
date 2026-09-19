using System.Net;
using System.Text;

namespace Ben.Data.WebApi.Client.Tests;

/// <summary>
/// An HTTP handler that answers from a script instead of a network, and records what it was asked.
/// </summary>
/// <remarks>
/// The same shape <c>Ben.Wasm.Video.Tests</c> uses. What a client SENDS matters as much as what it
/// does with the answer — a two-factor field that goes out as an empty string rather than being
/// omitted burns an attempt against an account that never had two-factor on, and no assertion about
/// the response would ever catch it.
/// </remarks>
public sealed class StubHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _script = new();

    public List<HttpRequestMessage> Requests { get; } = new();
    public List<string> Bodies { get; } = new();

    public string? LastBody => Bodies.Count == 0 ? null : Bodies[^1];
    public HttpRequestMessage? LastRequest => Requests.Count == 0 ? null : Requests[^1];
    public int CallCount => Requests.Count;

    /// <summary>Answers every request the same way.</summary>
    public static StubHandler Always(HttpStatusCode status, string body = "", string contentType = "application/json")
    {
        var handler = new StubHandler();
        handler._fallback = _ => Respond(status, body, contentType);
        return handler;
    }

    /// <summary>Queues one answer. Answers are used in order; the last one repeats.</summary>
    public StubHandler Then(HttpStatusCode status, string body = "", string contentType = "application/json")
    {
        _script.Enqueue(_ => Respond(status, body, contentType));
        return this;
    }

    /// <summary>Queues an answer built from the request, for asserting on what was sent.</summary>
    public StubHandler Then(Func<HttpRequestMessage, HttpResponseMessage> answer)
    {
        _script.Enqueue(answer);
        return this;
    }

    /// <summary>Queues a thrown <see cref="HttpRequestException"/> — an unreachable server.</summary>
    public StubHandler ThenUnreachable()
    {
        _script.Enqueue(_ => throw new HttpRequestException("unreachable"));
        return this;
    }

    private Func<HttpRequestMessage, HttpResponseMessage>? _fallback;
    private Func<HttpRequestMessage, HttpResponseMessage>? _last;

    /// <summary>
    /// When set, every answer waits for this before it is returned — and waits by yielding, not by
    /// blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// The distinction is the whole point. A handler that sleeps synchronously runs the entire
    /// "request" inside whatever lock its caller holds, so callers never actually overlap and a
    /// concurrency test proves nothing. A first draft of the single-flight test did exactly that
    /// and passed with the guard removed.
    /// </remarks>
    public TaskCompletionSource? Gate { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));

        var answer = _script.Count > 0 ? _script.Dequeue() : (_last ?? _fallback);
        if (answer is null)
            throw new InvalidOperationException("StubHandler was asked for an answer it has no script for.");

        if (_script.Count == 0) _last ??= answer;

        if (Gate is { } gate) await gate.Task.WaitAsync(cancellationToken);

        return answer(request);
    }

    private static HttpResponseMessage Respond(HttpStatusCode status, string body, string contentType) =>
        new(status)
        {
            Content = body.Length == 0
                ? new StringContent(string.Empty)
                : new StringContent(body, Encoding.UTF8, contentType),
        };
}

/// <summary>Reads the captured API answers in <c>Fixtures/</c>.</summary>
public static class Fixture
{
    public static string Read(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
}
