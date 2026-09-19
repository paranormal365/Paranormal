using System.Text.Json;
using Ben.Video.Core.SidecarContracts;
using Microsoft.JSInterop;

namespace Ben.Video.Editor.Services;

/// <summary>
/// The single path every sidecar request takes — item #70 phase 173.
///
/// <para><b>Why this exists instead of <see cref="HttpClient"/>.</b> The sidecar binds to
/// <c>127.0.0.1</c> on the machine the <i>user</i> is sitting at. A C# <see cref="HttpClient"/>
/// call resolves that loopback address wherever the Blazor code happens to be executing: in the
/// browser under WebAssembly (correct by accident), but on the <i>server</i> under Blazor Server —
/// a different machine entirely in any real deployment. Worse, a server-side request carries no
/// <c>Origin</c> header, and <c>SecurityMiddleware</c> 403s every endpoint except bare
/// <c>/v1/health</c> without one. That combination fails in the least helpful way possible: the
/// health probe succeeds (so the toolbar chip appears and says a sidecar was found) and then
/// pairing is rejected with "that code was rejected", which reads as a wrong pairing code rather
/// than as a request that never reached the user's machine.</para>
///
/// <para>Routing through <c>fetch</c> in the browser fixes both halves at once — the request
/// originates on the user's machine, and the browser attaches the <c>Origin</c> header the
/// sidecar's allowlist is built around. It also makes the two transports this feature used to
/// straddle (C# for JSON, JS for byte-moving uploads and blob URLs) into one, so "which machine
/// did this call come from" is no longer a per-call-site question.</para>
///
/// <para>Deserialization stays in C# with <see cref="SidecarJsonOptions.LenientResponses"/> — the
/// phase-158 rule that responses must tolerate unknown fields is unchanged and is exactly why the
/// JS side hands back a raw string rather than parsing anything itself.</para>
/// </summary>
public sealed class SidecarTransport(IJSRuntime js) : IAsyncDisposable
{
    private const string ModulePath = "js/sidecarInterop.js";

    private IJSObjectReference? _module;
    private readonly SemaphoreSlim _moduleGate = new(1, 1);

    /// <summary>One sidecar response. <see cref="Status"/> is a real HTTP status code; transport
    /// failures never reach the caller as a status, they throw.</summary>
    public sealed record Response(int Status, string Body)
    {
        public bool IsSuccess => Status is >= 200 and < 300;

        /// <summary>Throws unless the sidecar answered 2xx — the equivalent of
        /// <c>EnsureSuccessStatusCode</c>, kept so call sites that only ever expected success read
        /// the way they did before.</summary>
        public Response EnsureSuccess()
        {
            if (!IsSuccess)
                throw new SidecarTransportException($"Sidecar returned HTTP {Status}.");
            return this;
        }

        /// <summary>Deserializes the body with <see cref="SidecarJsonOptions.LenientResponses"/>,
        /// never <c>Default</c>: a newer sidecar adding a field to a response must be ignorable by
        /// an older browser build, not fatal (item #70 phase 158).</summary>
        public T? ReadJson<T>() =>
            string.IsNullOrWhiteSpace(Body)
                ? default
                : JsonSerializer.Deserialize<T>(Body, SidecarJsonOptions.LenientResponses);
    }

    /// <summary>
    /// Issues one request and returns the sidecar's answer.
    /// </summary>
    /// <param name="timeout">Wall-clock cap enforced in JS by aborting the fetch. Pass null for no
    /// timeout — callers that manage their own deadline via <paramref name="ct"/> should do that,
    /// matching the <c>Timeout.InfiniteTimeSpan</c> they used to set on the HttpClient.</param>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> fired.</exception>
    /// <exception cref="SidecarTransportException">Nothing listening, the connection dropped, or
    /// the <paramref name="timeout"/> elapsed.</exception>
    public async Task<Response> SendAsync(
        string method, string url, string token, object? body = null,
        TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var json = body is null ? null : JsonSerializer.Serialize(body, SidecarJsonOptions.Default);

        var raw = await InvokeAsync<RawResponse>(
            ct, timeout,
            (module, id, ms) => module.InvokeAsync<RawResponse>(
                "sendRequest", id, method, url, token, json, ms));

        switch (raw.Outcome)
        {
            case "ok":
                return new Response(raw.Status, raw.Body ?? string.Empty);
            case "aborted":
                // Caller cancellation and our own timeout share one AbortController in JS — the
                // token is the only thing that can tell them apart, and callers respond very
                // differently (SidecarMediaProbe re-throws the former, swallows the latter).
                ct.ThrowIfCancellationRequested();
                throw new SidecarTransportException($"Sidecar request timed out after {timeout}.");
            default:
                throw new SidecarTransportException(
                    string.IsNullOrWhiteSpace(raw.Body) ? "Sidecar request failed." : raw.Body);
        }
    }

    /// <summary>
    /// Issues one GET and returns the response body as bytes. Only for payloads the caller
    /// genuinely needs in the WASM heap (a rendered segment, which gets written to MEMFS as a
    /// <c>byte[]</c> regardless) — everything else has a dedicated JS path that keeps the bytes
    /// out of C# entirely.
    /// </summary>
    public async Task<byte[]> GetBytesAsync(
        string url, string token, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        try
        {
            return await InvokeAsync<byte[]>(
                ct, timeout,
                (module, id, ms) => module.InvokeAsync<byte[]>("sendRequestForBytes", id, url, token, ms));
        }
        catch (JSException ex)
        {
            // sendRequestForBytes throws rather than returning an outcome object (see its comment
            // — a bare Uint8Array is what gets the efficient binary transfer), so the outcome
            // arrives as a message prefix instead of a field.
            ct.ThrowIfCancellationRequested();
            throw new SidecarTransportException(ex.Message);
        }
    }

    private async Task<T> InvokeAsync<T>(
        CancellationToken ct, TimeSpan? timeout,
        Func<IJSObjectReference, string, double, ValueTask<T>> invoke)
    {
        ct.ThrowIfCancellationRequested();

        var module = await EnsureModuleAsync();
        var requestId = Guid.NewGuid().ToString("N");
        var timeoutMs = timeout is { } t ? t.TotalMilliseconds : 0;

        // The token cancels the C# await; this cancels the fetch behind it. Without the
        // registration a cancelled export would leave its result download running to completion in
        // the background, holding the connection the next attempt wants.
        await using var registration = ct.Register(() =>
        {
            try { _ = module.InvokeVoidAsync("abortRequest", requestId); }
            catch { /* circuit/runtime already gone — the fetch dies with the page anyway */ }
        });

        // Deliberately NOT passing ct into the interop call: that would throw
        // TaskCanceledException here and skip the outcome inspection the callers depend on. The
        // registration above is what actually stops the work.
        return await invoke(module, requestId, timeoutMs);
    }

    private async Task<IJSObjectReference> EnsureModuleAsync()
    {
        if (_module is not null) return _module;

        await _moduleGate.WaitAsync();
        try
        {
            // Cached for the lifetime of the service rather than imported per call: the poll loops
            // issue a request every 250-500ms, and a dynamic import each time is pure overhead
            // (the browser caches the module, but the interop round trip is not free).
            return _module ??= await js.InvokeAsync<IJSObjectReference>("benImportEditorModule", ModulePath);
        }
        finally
        {
            _moduleGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _moduleGate.Dispose();
        if (_module is not null)
        {
            try { await _module.DisposeAsync(); } catch { /* JS runtime may already be torn down */ }
            _module = null;
        }
    }

    /// <summary>Mirrors the <c>{ status, body, outcome }</c> shape <c>sendRequest</c> returns.
    /// Blazor's interop serializer is Web-defaults (camelCase, case-insensitive), so the JS field
    /// names map onto these directly.</summary>
    private sealed record RawResponse(int Status, string? Body, string? Outcome);
}

/// <summary>
/// A sidecar request never reached a usable answer — nothing listening on the port, the connection
/// dropped mid-request, a timeout, or a non-2xx status where the caller demanded success. Callers
/// treat this the way they used to treat <see cref="HttpRequestException"/>: as evidence this
/// connection is not usable right now, not as a bug.
/// </summary>
public sealed class SidecarTransportException(string message) : Exception(message);
