using System.Net;
using System.Text;
using Ben.Wasm.Video.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Ben.Wasm.Video.Tests;

/// <summary>
/// Arriving at the standalone editor from a link the site handed out.
/// </summary>
/// <remarks>
/// Somebody already signed in on the site used to land here signed out, at a second password door
/// (phase 12). What crosses is a one-minute, one-use code in the URL fragment — never the site's
/// tokens, which stay on the site's server.
/// </remarks>
public sealed class EditorHandoffServiceTests
{
    private const string Base = "https://editor.test/";

    // ── Reading the link ──────────────────────────────────────────────────────

    [Fact]
    public void A_link_with_nothing_in_it_carries_no_handoff()
    {
        Assert.False(EditorHandoff.Parse(Base).IsPresent);
        Assert.False(EditorHandoff.Parse(null).IsPresent);
        Assert.False(EditorHandoff.Parse("").IsPresent);
    }

    [Fact]
    public void A_code_is_read_out_of_the_fragment() =>
        Assert.Equal("abc123", EditorHandoff.Parse($"{Base}#handoff=abc123").Code);

    [Fact]
    public void A_bare_fragment_is_read_the_same_way() =>
        Assert.Equal("abc123", EditorHandoff.Parse("#handoff=abc123").Code);

    [Fact]
    public void A_project_is_read_out_of_the_fragment()
    {
        var id = Guid.NewGuid();

        Assert.Equal(id, EditorHandoff.Parse($"{Base}#project={id}").ProjectId);
    }

    [Fact]
    public void A_link_can_carry_both()
    {
        var id = Guid.NewGuid();

        var handoff = EditorHandoff.Parse($"{Base}#handoff=abc123&project={id}");

        Assert.Equal("abc123", handoff.Code);
        Assert.Equal(id, handoff.ProjectId);
    }

    /// <summary>
    /// Signing somebody in and opening nothing beats refusing to do either.
    /// </summary>
    [Fact]
    public void A_project_id_that_is_not_an_id_does_not_lose_the_sign_in()
    {
        var handoff = EditorHandoff.Parse($"{Base}#handoff=abc123&project=the-good-one");

        Assert.Equal("abc123", handoff.Code);
        Assert.Null(handoff.ProjectId);
    }

    [Fact]
    public void An_escaped_code_is_unescaped() =>
        Assert.Equal("a b+c", EditorHandoff.Parse($"{Base}#handoff=a%20b%2Bc").Code);

    /// <summary>
    /// A fragment from a mangled paste is not a handoff, and is not a crash either.
    /// </summary>
    [Theory]
    [InlineData("#handoff")]
    [InlineData("#handoff=")]
    [InlineData("#=abc123")]
    [InlineData("#&&&")]
    public void Nonsense_in_the_fragment_carries_no_code(string fragment) =>
        Assert.Null(EditorHandoff.Parse(Base + fragment).Code);

    /// <summary>
    /// A half-escaped paste is carried through as typed rather than dropped or thrown on. It is
    /// not a code, and the server refuses it like any other wrong one.
    /// </summary>
    [Fact]
    public void A_mangled_escape_travels_on_to_be_refused() =>
        Assert.Equal("%zz", EditorHandoff.Parse($"{Base}#handoff=%zz").Code);

    /// <summary>
    /// A query string is not a fragment. The whole point of the fragment is that it never reaches
    /// a server, so a code that arrived in the query has already been logged somewhere and is not
    /// treated as one.
    /// </summary>
    [Fact]
    public void A_code_in_the_query_string_is_not_a_handoff() =>
        Assert.False(EditorHandoff.Parse($"{Base}?handoff=abc123").IsPresent);

    // ── Exchanging ────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_good_code_becomes_a_signed_in_session()
    {
        var tokens  = new TokenStore(new NoJs());
        var handler = new StubHandler(HttpStatusCode.OK,
            """{"accessToken":"at","refreshToken":"rt","expiresIn":3600}""");

        var signedIn = await Create(handler, tokens).ExchangeAsync("abc123");

        Assert.True(signedIn);
        Assert.Equal("at", await tokens.GetAccessTokenAsync());
        Assert.True(tokens.IsAuthenticated);
    }

    [Fact]
    public async Task The_code_is_what_gets_posted()
    {
        var handler = new StubHandler(HttpStatusCode.OK,
            """{"accessToken":"at","refreshToken":"rt","expiresIn":3600}""");

        await Create(handler, new TokenStore(new NoJs())).ExchangeAsync("abc123");

        Assert.Contains("abc123", handler.LastBody);
        Assert.Contains("editor-handoff/exchange", handler.LastUrl);
    }

    [Fact]
    public async Task A_refused_code_signs_nobody_in()
    {
        var tokens = new TokenStore(new NoJs());

        var signedIn = await Create(new StubHandler(HttpStatusCode.Unauthorized, "\"expired\""), tokens)
            .ExchangeAsync("stale");

        Assert.False(signedIn);
        Assert.False(tokens.IsAuthenticated);
    }

    /// <summary>
    /// A handoff is a convenience on top of a sign-in page that still works, so nothing here may
    /// throw: an unreachable API has to end with the editor open and a sign-in link visible.
    /// </summary>
    [Fact]
    public async Task An_unreachable_server_is_not_a_crash()
    {
        var tokens = new TokenStore(new NoJs());

        Assert.False(await Create(new ThrowingHandler(), tokens).ExchangeAsync("abc123"));
        Assert.False(tokens.IsAuthenticated);
    }

    [Fact]
    public async Task An_answer_missing_its_tokens_signs_nobody_in()
    {
        var tokens = new TokenStore(new NoJs());

        var signedIn = await Create(new StubHandler(HttpStatusCode.OK, """{"expiresIn":3600}"""), tokens)
            .ExchangeAsync("abc123");

        Assert.False(signedIn);
        Assert.False(tokens.IsAuthenticated);
    }

    // ── Applying the whole link ───────────────────────────────────────────────

    [Fact]
    public async Task Following_the_link_signs_in_and_names_the_project()
    {
        var id      = Guid.NewGuid();
        var tokens  = new TokenStore(new NoJs());
        var nav     = new FakeNavigation($"{Base}#handoff=abc123&project={id}");
        var handler = new StubHandler(HttpStatusCode.OK,
            """{"accessToken":"at","refreshToken":"rt","expiresIn":3600}""");

        var project = await Create(handler, tokens, nav).ApplyAsync();

        Assert.Equal(id, project);
        Assert.True(tokens.IsAuthenticated);
    }

    /// <summary>
    /// The code comes out of the address bar whatever happened, so a reload does not replay it and
    /// a copied URL carries nothing.
    /// </summary>
    [Fact]
    public async Task The_code_is_taken_out_of_the_address_bar()
    {
        var nav = new FakeNavigation($"{Base}#handoff=abc123");
        var js  = new RecordingJs();
        var handler = new StubHandler(HttpStatusCode.OK,
            """{"accessToken":"at","refreshToken":"rt","expiresIn":3600}""");

        await Create(handler, new TokenStore(new NoJs()), nav, js).ApplyAsync();

        Assert.NotNull(js.RewrittenTo);
        Assert.DoesNotContain("handoff", js.RewrittenTo);
        Assert.DoesNotContain("abc123", js.RewrittenTo);
    }

    [Fact]
    public async Task The_code_is_taken_out_even_when_the_exchange_failed()
    {
        var nav = new FakeNavigation($"{Base}#handoff=stale");
        var js  = new RecordingJs();

        await Create(new StubHandler(HttpStatusCode.Unauthorized, "\"no\""),
            new TokenStore(new NoJs()), nav, js).ApplyAsync();

        Assert.DoesNotContain("stale", js.RewrittenTo);
    }

    /// <summary>
    /// Back must not return to the URL that still has the code in it — and routing must not be
    /// asked to do it, because a URL that differs only by its fragment is the URL it is already
    /// on, so the navigate changes nothing and the code stays in the address bar.
    /// </summary>
    [Fact]
    public async Task Clearing_the_fragment_replaces_the_history_entry()
    {
        var nav = new FakeNavigation($"{Base}#handoff=abc123");
        var js  = new RecordingJs();
        var handler = new StubHandler(HttpStatusCode.OK,
            """{"accessToken":"at","refreshToken":"rt","expiresIn":3600}""");

        await Create(handler, new TokenStore(new NoJs()), nav, js).ApplyAsync();

        Assert.Contains(js.Calls, c => c.Identifier == "history.replaceState");
        Assert.DoesNotContain(js.Calls, c => c.Identifier == "history.pushState");
        Assert.Null(nav.LastNavigatedTo);
    }

    /// <summary>
    /// Somebody whose minute ran out still lands on the project they followed a link to, once they
    /// sign in themselves.
    /// </summary>
    [Fact]
    public async Task A_failed_sign_in_still_names_the_project()
    {
        var id  = Guid.NewGuid();
        var nav = new FakeNavigation($"{Base}#handoff=stale&project={id}");

        var project = await Create(new StubHandler(HttpStatusCode.Unauthorized, "\"no\""),
            new TokenStore(new NoJs()), nav).ApplyAsync();

        Assert.Equal(id, project);
    }

    [Fact]
    public async Task An_ordinary_visit_touches_nothing()
    {
        var nav    = new FakeNavigation(Base);
        var tokens = new TokenStore(new NoJs());

        var project = await Create(new ThrowingHandler(), tokens, nav).ApplyAsync();

        Assert.Null(project);
        Assert.Null(nav.LastNavigatedTo);
        Assert.False(tokens.IsAuthenticated);
    }

    // ── Support ───────────────────────────────────────────────────────────────

    private static EditorHandoffService Create(
        HttpMessageHandler handler, TokenStore tokens, NavigationManager? nav = null,
        RecordingJs? js = null) =>
        new(new HttpClient(handler) { BaseAddress = new Uri(Base) },
            tokens,
            nav ?? new FakeNavigation(Base),
            js ?? new RecordingJs());

    /// <summary>
    /// Records what the app asked the browser to do, so the address-bar rewrite can be checked
    /// without a browser.
    /// </summary>
    private sealed class RecordingJs : IJSRuntime
    {
        public List<(string Identifier, object?[]? Args)> Calls { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            Calls.Add((identifier, args));
            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken ct, object?[]? args)
            => InvokeAsync<TValue>(identifier, args);

        /// <summary>The URL the page was left showing, or null when nothing rewrote it.</summary>
        public string? RewrittenTo => Calls
            .Where(c => c.Identifier == "history.replaceState")
            .Select(c => c.Args?.ElementAtOrDefault(2) as string)
            .LastOrDefault();
    }

    private sealed class FakeNavigation : NavigationManager
    {
        public FakeNavigation(string uri) => Initialize(Base, uri);

        public string? LastNavigatedTo { get; private set; }
        public bool    LastReplace     { get; private set; }
        public bool    LastForceLoad   { get; private set; }

        protected override void NavigateToCore(string uri, NavigationOptions options)
        {
            LastNavigatedTo = uri;
            LastReplace     = options.ReplaceHistoryEntry;
            LastForceLoad   = options.ForceLoad;
            Uri             = ToAbsoluteUri(uri).ToString();
        }
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string LastBody { get; private set; } = string.Empty;
        public string LastUrl  { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            LastUrl = request.RequestUri?.ToString() ?? string.Empty;
            if (request.Content is not null) LastBody = await request.Content.ReadAsStringAsync(ct);

            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
            => throw new HttpRequestException("no route to host");
    }

    /// <summary>The token store touches JS only when it persists; these tests never get that far.</summary>
    private sealed class NoJs : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => ValueTask.FromResult(default(TValue)!);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken ct, object?[]? args)
            => InvokeAsync<TValue>(identifier, args);
    }
}
