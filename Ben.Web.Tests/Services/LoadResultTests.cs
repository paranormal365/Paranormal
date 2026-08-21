using System.Net;
using Ben.Web.Services.WebApi;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// "The server refused" and "there is nothing here" must not be the same value.
/// </summary>
/// <remarks>
/// <para>Three bugs on 2026-08-20 shared one cause: <c>GetAsync</c> answers any non-2xx with
/// <c>default</c>, the adapters follow it with <c>?? []</c>, and the page renders "No records
/// available". A member with a group handbook on the server was told the group had no files; a
/// member of a three-person group was told nobody was in it; a SuperAdmin was told the same on a
/// page that had simply rendered before the circuit was live. Item 120.</para>
///
/// <para>These tests are about the distinction surviving each hop: the type keeps it, the client
/// derives it from the status code, and — the part that matters for adoption —
/// <see cref="LoadResult{T}.Items"/> is safe to enumerate in every state, so moving a call site
/// across cannot make it worse.</para>
/// </remarks>
public sealed class LoadResultTests
{
    // ── The type ─────────────────────────────────────────────────────────────

    [Fact]
    public void An_empty_success_is_not_a_failure()
    {
        var result = LoadResult<string>.Ok([]);

        Assert.False(result.Failed);
        Assert.True(result.IsEmpty);
        Assert.Empty(result.Items);
    }

    [Fact]
    public void A_failure_is_not_empty()
    {
        // The whole point: both have no items, and only one of them means "there is nothing here".
        var result = LoadResult<string>.Failure();

        Assert.True(result.Failed);
        Assert.False(result.IsEmpty);
    }

    [Fact]
    public void A_populated_result_is_neither_failed_nor_empty()
    {
        var result = LoadResult<string>.Ok(["one", "two"]);

        Assert.False(result.Failed);
        Assert.False(result.IsEmpty);
        Assert.Equal(2, result.Items.Count);
    }

    /// <summary>
    /// Items is never null, in any state — including <c>default(LoadResult&lt;T&gt;)</c>.
    /// </summary>
    /// <remarks>
    /// Load-bearing for adoption. A component holds one of these as a field before its first
    /// fetch, and the markup enumerates it during that render; if the default threw, moving a call
    /// site across would introduce a crash where there had merely been a wrong empty state.
    /// </remarks>
    [Fact]
    public void Items_is_safe_to_enumerate_in_every_state()
    {
        Assert.Empty(default(LoadResult<string>).Items);
        Assert.Empty(LoadResult<string>.Failure().Items);
        Assert.Empty(LoadResult<string>.Ok(null).Items);
    }

    /// <summary>The default reads as an ordinary empty list, not as a failure.</summary>
    /// <remarks>
    /// So a component that has not fetched yet shows its empty state rather than an alarming
    /// "couldn't load" — it pairs with a <c>Loading</c> flag, which takes precedence.
    /// </remarks>
    [Fact]
    public void The_default_value_reads_as_empty_rather_than_failed()
    {
        Assert.False(default(LoadResult<string>).Failed);
        Assert.True(default(LoadResult<string>).IsEmpty);
    }

    // ── The client ───────────────────────────────────────────────────────────

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            });
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("connection refused");
    }

    // The real token store, as WebApiClientTests does — an empty one is a signed-out caller,
    // which is exactly the state that produced the bug on a prerender.
    private static WebApiClient Client(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://localhost") }, new WebApiTokenStore());

    [Fact]
    public async Task A_200_with_items_is_a_success()
    {
        var client = Client(new StubHandler(HttpStatusCode.OK, """["alpha","beta"]"""));

        var result = await client.GetListAsync<string>("/api/whatever");

        Assert.False(result.Failed);
        Assert.Equal(["alpha", "beta"], result.Items);
    }

    [Fact]
    public async Task A_200_with_an_empty_array_is_a_success_that_is_empty()
    {
        var client = Client(new StubHandler(HttpStatusCode.OK, "[]"));

        var result = await client.GetListAsync<string>("/api/whatever");

        Assert.False(result.Failed);
        Assert.True(result.IsEmpty);
    }

    /// <summary>
    /// A 403 is a failure, not an empty list. This is the bug, stated as a test.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Any_non_success_status_is_a_failure(HttpStatusCode status)
    {
        var client = Client(new StubHandler(status, ""));

        var result = await client.GetListAsync<string>("/api/whatever");

        Assert.True(result.Failed);
        Assert.False(result.IsEmpty);
    }

    /// <summary>An unreachable API is a failure too — emphatically not an empty group.</summary>
    [Fact]
    public async Task A_connection_failure_is_a_failure()
    {
        var client = Client(new ThrowingHandler());

        var result = await client.GetListAsync<string>("/api/whatever");

        Assert.True(result.Failed);
        Assert.Empty(result.Items);
    }

    /// <summary>A refusal written as a sentence survives, so the page can show it.</summary>
    [Fact]
    public async Task A_prose_refusal_is_carried_through()
    {
        var client = Client(new StubHandler(
            HttpStatusCode.Conflict, "This publication still has one post in it."));

        var result = await client.GetListAsync<string>("/api/whatever");

        Assert.True(result.Failed);
        Assert.Equal("This publication still has one post in it.", result.Reason);
    }

    /// <summary>
    /// A ProblemDetails blob or an HTML error page is replaced by the status, not shown raw.
    /// </summary>
    /// <remarks>
    /// <para>Showing a person a JSON envelope is worse than saying nothing. But saying nothing is
    /// worse than naming the status: a blank admin page taught nobody anything on the production
    /// deploy, whereas "the server answered 404" says the path is wrong and "403" says the path is
    /// right and the caller is not allowed (item 126).</para>
    ///
    /// <para>So the assertion is two-sided — the raw body must not leak, and the reason must still
    /// carry the status.</para>
    /// </remarks>
    [Theory]
    [InlineData("""{"type":"about:blank","status":500}""")]
    [InlineData("<html><body>500 Internal Server Error</body></html>")]
    public async Task Machine_readable_error_bodies_are_replaced_by_the_status(string body)
    {
        var client = Client(new StubHandler(HttpStatusCode.InternalServerError, body));

        var result = await client.GetListAsync<string>("/api/whatever");

        Assert.True(result.Failed);
        Assert.DoesNotContain("about:blank", result.Reason ?? "");
        Assert.DoesNotContain("<html", result.Reason ?? "");
        Assert.Contains("500", result.Reason ?? "");
    }

    /// <summary>The status reaches the reader for the two codes that matter most when deploying.</summary>
    /// <remarks>
    /// 404 and 403 are the pair that separate "the API is mounted somewhere else" from "the API is
    /// there and refused you" — the exact question a blank page could not answer.
    /// </remarks>
    [Theory]
    [InlineData(HttpStatusCode.NotFound, "404")]
    [InlineData(HttpStatusCode.Forbidden, "403")]
    public async Task The_status_code_is_named_in_the_reason(HttpStatusCode status, string expected)
    {
        var client = Client(new StubHandler(status, ""));

        var result = await client.GetListAsync<string>("/api/whatever");

        Assert.True(result.Failed);
        Assert.Contains(expected, result.Reason ?? "");
    }
}
