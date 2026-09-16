using System.Net;
using Ben.Canvas.Tests.Support;
using Ben.Wasm.Canvas.Services;
using Microsoft.AspNetCore.Components;

namespace Ben.Canvas.Tests.Hosting;

/// <summary>
/// Arriving at the standalone canvas editor from a link the site handed out.
/// </summary>
/// <remarks>
/// Copied from Ben.Wasm.Video.Tests/EditorHandoffServiceTests.cs with the canvas's doc, case and org
/// keys, plus the fact that pins the /webapi base path on the exchange. What crosses is a one-minute,
/// one-use code in the URL fragment - never the site's tokens.
/// </remarks>
public sealed class CanvasHandoffServiceTests
{
    private const string Base = "https://editor.test/";
    private const string Tokens = """{"accessToken":"at","refreshToken":"rt","expiresIn":3600}""";

    // ── Reading the link ──────────────────────────────────────────────────────

    [Fact]
    public void A_link_with_nothing_in_it_carries_no_handoff()
    {
        Assert.False(CanvasHandoff.Parse(Base).IsPresent);
        Assert.False(CanvasHandoff.Parse(null).IsPresent);
        Assert.False(CanvasHandoff.Parse("").IsPresent);
    }

    [Fact]
    public void A_code_is_read_out_of_the_fragment() =>
        Assert.Equal("abc123", CanvasHandoff.Parse($"{Base}#handoff=abc123").Code);

    [Fact]
    public void A_bare_fragment_is_read_the_same_way() =>
        Assert.Equal("abc123", CanvasHandoff.Parse("#handoff=abc123").Code);

    [Fact]
    public void A_doc_case_and_org_in_the_fragment_are_all_read()
    {
        Guid doc = Guid.NewGuid(), @case = Guid.NewGuid(), org = Guid.NewGuid();

        var handoff = CanvasHandoff.Parse($"{Base}#handoff=abc&doc={doc}&case={@case}&org={org}");

        Assert.Equal("abc", handoff.Code);
        Assert.Equal(doc, handoff.DocumentId);
        Assert.Equal(@case, handoff.CaseId);
        Assert.Equal(org, handoff.OrganizationId);
    }

    /// <summary>The video editor's key means nothing here; a canvas link names a doc.</summary>
    [Fact]
    public void A_project_key_is_not_a_canvas_handoff()
    {
        var handoff = CanvasHandoff.Parse($"{Base}#handoff=abc&project={Guid.NewGuid()}");

        Assert.Equal("abc", handoff.Code);
        Assert.Null(handoff.DocumentId);
    }

    /// <summary>Signing somebody in and opening nothing beats refusing to do either.</summary>
    [Fact]
    public void A_doc_id_that_is_not_an_id_does_not_lose_the_sign_in()
    {
        var handoff = CanvasHandoff.Parse($"{Base}#handoff=abc123&doc=the-good-one");

        Assert.Equal("abc123", handoff.Code);
        Assert.Null(handoff.DocumentId);
    }

    [Fact]
    public void An_escaped_code_is_unescaped() =>
        Assert.Equal("a b+c", CanvasHandoff.Parse($"{Base}#handoff=a%20b%2Bc").Code);

    [Theory]
    [InlineData("#handoff")]
    [InlineData("#handoff=")]
    [InlineData("#=abc123")]
    [InlineData("#&&&")]
    public void Nonsense_in_the_fragment_carries_no_code(string fragment) =>
        Assert.Null(CanvasHandoff.Parse(Base + fragment).Code);

    [Fact]
    public void A_mangled_escape_travels_on_to_be_refused() =>
        Assert.Equal("%zz", CanvasHandoff.Parse($"{Base}#handoff=%zz").Code);

    /// <summary>
    /// The whole point of the fragment is that it never reaches a server, so a code that arrived in
    /// the query string has already been logged somewhere and is not treated as one.
    /// </summary>
    [Fact]
    public void A_code_in_the_query_string_is_not_a_handoff() =>
        Assert.False(CanvasHandoff.Parse($"{Base}?handoff=abc123").IsPresent);

    // ── Exchanging ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Exchange_keeps_the_webapi_base_path()
    {
        var handler = new StubHandler(HttpStatusCode.OK, Tokens);

        await Create(handler, new TokenStore(new NoJs()), baseAddress: "https://ishaunted.test/webapi/").ExchangeAsync("abc123");

        Assert.Equal("https://ishaunted.test/webapi/api/auth/editor-handoff/exchange", handler.LastUrl);
    }

    [Fact]
    public async Task A_good_code_becomes_a_signed_in_session()
    {
        var tokens = new TokenStore(new NoJs());

        var signedIn = await Create(new StubHandler(HttpStatusCode.OK, Tokens), tokens).ExchangeAsync("abc123");

        Assert.True(signedIn);
        Assert.Equal("at", await tokens.GetAccessTokenAsync());
        Assert.True(tokens.IsAuthenticated);
    }

    [Fact]
    public async Task The_code_is_what_gets_posted()
    {
        var handler = new StubHandler(HttpStatusCode.OK, Tokens);

        await Create(handler, new TokenStore(new NoJs())).ExchangeAsync("abc123");

        Assert.Contains("abc123", handler.LastBody);
        Assert.Contains("editor-handoff/exchange", handler.LastUrl);
    }

    [Fact]
    public async Task A_refused_code_signs_nobody_in()
    {
        var tokens = new TokenStore(new NoJs());

        var signedIn = await Create(new StubHandler(HttpStatusCode.Unauthorized, "\"expired\""), tokens).ExchangeAsync("stale");

        Assert.False(signedIn);
        Assert.False(tokens.IsAuthenticated);
    }

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

        var signedIn = await Create(new StubHandler(HttpStatusCode.OK, """{"expiresIn":3600}"""), tokens).ExchangeAsync("abc123");

        Assert.False(signedIn);
        Assert.False(tokens.IsAuthenticated);
    }

    // ── Applying the whole link ───────────────────────────────────────────────

    [Fact]
    public async Task Following_the_link_signs_in_and_names_the_board_case_and_org()
    {
        Guid doc = Guid.NewGuid(), @case = Guid.NewGuid(), org = Guid.NewGuid();
        var tokens = new TokenStore(new NoJs());
        var nav = new FakeNavigation(Base, $"{Base}#handoff=abc123&doc={doc}&case={@case}&org={org}");

        var handoff = await Create(new StubHandler(HttpStatusCode.OK, Tokens), tokens, nav).ApplyAsync();

        Assert.Equal(doc, handoff.DocumentId);
        Assert.Equal(@case, handoff.CaseId);
        Assert.Equal(org, handoff.OrganizationId);
        Assert.True(tokens.IsAuthenticated);
    }

    [Fact]
    public async Task The_code_is_taken_out_of_the_address_bar()
    {
        var nav = new FakeNavigation(Base, $"{Base}#handoff=abc123");
        var js = new RecordingJs();

        await Create(new StubHandler(HttpStatusCode.OK, Tokens), new TokenStore(new NoJs()), nav, js).ApplyAsync();

        Assert.NotNull(js.RewrittenTo);
        Assert.DoesNotContain("handoff", js.RewrittenTo);
        Assert.DoesNotContain("abc123", js.RewrittenTo);
    }

    [Fact]
    public async Task The_code_is_taken_out_even_when_the_exchange_failed()
    {
        var nav = new FakeNavigation(Base, $"{Base}#handoff=stale");
        var js = new RecordingJs();

        await Create(new StubHandler(HttpStatusCode.Unauthorized, "\"no\""), new TokenStore(new NoJs()), nav, js).ApplyAsync();

        Assert.DoesNotContain("stale", js.RewrittenTo);
    }

    /// <summary>
    /// Back must not return to the URL that still has the code in it, and routing must not be asked
    /// to clear it: a URL that differs only by its fragment is the URL it is already on.
    /// </summary>
    [Fact]
    public async Task The_fragment_is_cleared_by_replacing_history_without_eval()
    {
        var nav = new FakeNavigation(Base, $"{Base}#handoff=abc123");
        var js = new RecordingJs();

        await Create(new StubHandler(HttpStatusCode.OK, Tokens), new TokenStore(new NoJs()), nav, js).ApplyAsync();

        Assert.Contains(js.Calls, c => c.Identifier == "history.replaceState");
        Assert.DoesNotContain(js.Calls, c => c.Identifier == "history.pushState");
        Assert.DoesNotContain(js.Calls, c => c.Identifier.Contains("eval", StringComparison.Ordinal));
        Assert.Null(nav.LastNavigatedTo);
    }

    [Fact]
    public async Task A_failed_sign_in_still_names_the_board()
    {
        var doc = Guid.NewGuid();
        var nav = new FakeNavigation(Base, $"{Base}#handoff=stale&doc={doc}");

        var handoff = await Create(new StubHandler(HttpStatusCode.Unauthorized, "\"no\""), new TokenStore(new NoJs()), nav).ApplyAsync();

        Assert.Equal(doc, handoff.DocumentId);
    }

    [Fact]
    public async Task An_ordinary_visit_touches_nothing()
    {
        var nav = new FakeNavigation(Base);
        var tokens = new TokenStore(new NoJs());

        var handoff = await Create(new ThrowingHandler(), tokens, nav).ApplyAsync();

        Assert.False(handoff.IsPresent);
        Assert.Null(nav.LastNavigatedTo);
        Assert.False(tokens.IsAuthenticated);
    }

    private static CanvasHandoffService Create(
        HttpMessageHandler handler, TokenStore tokens, NavigationManager? nav = null,
        RecordingJs? js = null, string baseAddress = Base) =>
        new(new HttpClient(handler) { BaseAddress = new Uri(baseAddress) },
            tokens,
            nav ?? new FakeNavigation(Base),
            js ?? new RecordingJs());
}
