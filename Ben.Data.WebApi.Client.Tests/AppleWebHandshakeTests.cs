using Ben.Data.WebApi.Client.External;

namespace Ben.Data.WebApi.Client.Tests;

/// <summary>
/// The website's half of Sign in with Apple: the URL the browser is sent to, and the form Apple
/// posts back.
/// </summary>
/// <remarks>
/// These matter more than the usual handshake tests. Apple refuses a localhost redirect, so the
/// round trip cannot be run on a development machine at all — if it is not covered here it is not
/// covered anywhere until it is live in front of somebody.
/// </remarks>
public sealed class AppleWebHandshakeTests
{
    private static readonly AppleWebOptions Options =
        new("com.ishaunted.web", "https://ishaunted.com/auth/apple-callback");

    private static Dictionary<string, string?> Form(params (string Key, string? Value)[] fields) =>
        fields.ToDictionary(f => f.Key, f => f.Value, StringComparer.Ordinal);

    // ── The authorize URL ─────────────────────────────────────────────────────

    /// <summary>
    /// Asking for an id_token is what makes a client secret unnecessary.
    /// </summary>
    /// <remarks>
    /// With it, Apple returns the signed identity token in the form post, and that token is all the
    /// API needs. Without it there is only a code, and redeeming a code means holding a secret that
    /// is itself a JWT signed with a downloaded key and expires within six months.
    /// </remarks>
    [Fact]
    public void The_authorize_url_asks_for_an_identity_token_directly()
    {
        var query = System.Web.HttpUtility.ParseQueryString(
            AppleWebAuthorizeRequest.Build(Options, "st", "no").Query);

        Assert.Contains("id_token", query["response_type"]);
    }

    /// <summary>
    /// Apple REQUIRES form_post once a scope is asked for, and for an id_token.
    /// </summary>
    /// <remarks>
    /// Anything else is refused before the person ever sees a sign-in box, so getting this wrong
    /// looks like the button being broken rather than a parameter being wrong.
    /// </remarks>
    [Fact]
    public void The_authorize_url_asks_for_a_form_post()
        => Assert.Equal("form_post", System.Web.HttpUtility.ParseQueryString(
            AppleWebAuthorizeRequest.Build(Options, "st", "no").Query)["response_mode"]);

    /// <summary>
    /// The name has to be asked for, or it never arrives.
    /// </summary>
    /// <remarks>
    /// Apple hands it over on a first authorization only, and only when the scope requested it.
    /// Not asking means every account created this way is named whatever the client invented.
    /// </remarks>
    [Fact]
    public void The_authorize_url_asks_for_the_name()
        => Assert.Contains("name", System.Web.HttpUtility.ParseQueryString(
            AppleWebAuthorizeRequest.Build(Options, "st", "no").Query)["scope"]);

    [Fact]
    public void The_authorize_url_carries_the_services_id_and_redirect()
    {
        var url = AppleWebAuthorizeRequest.Build(Options, "st", "no");
        var query = System.Web.HttpUtility.ParseQueryString(url.Query);

        Assert.StartsWith("https://appleid.apple.com/auth/authorize", url.ToString());
        Assert.Equal(Options.ServicesId, query["client_id"]);
        Assert.Equal(Options.RedirectUri, query["redirect_uri"]);
        Assert.Equal("st", query["state"]);
        Assert.Equal("no", query["nonce"]);
    }

    [Fact]
    public void Every_attempt_gets_its_own_state_and_nonce()
    {
        var secrets = Enumerable.Range(0, 50).Select(_ => AppleWebAuthorizeRequest.NewSecret()).ToHashSet();

        Assert.Equal(50, secrets.Count);
    }

    // ── Configuration ─────────────────────────────────────────────────────────

    /// <summary>
    /// An https redirect, or nothing.
    /// </summary>
    /// <remarks>
    /// Apple refuses plain http and refuses localhost. A site configured with either would render a
    /// button that sends somebody to an error page on Apple's domain, which is worse than no button.
    /// </remarks>
    [Theory]
    [InlineData("com.ishaunted.web", "https://ishaunted.com/auth/apple-callback", true)]
    [InlineData("", "https://ishaunted.com/auth/apple-callback", false)]
    [InlineData("com.ishaunted.web", "", false)]
    [InlineData("com.ishaunted.web", "http://ishaunted.com/auth/apple-callback", false)]
    [InlineData("com.ishaunted.web", "http://localhost:5078/auth/apple-callback", false)]
    [InlineData("com.ishaunted.web", "not-a-url", false)]
    public void Availability_needs_a_services_id_and_an_https_redirect(string id, string redirect, bool ok)
        => Assert.Equal(ok, new AppleWebOptions(id, redirect).IsConfigured);

    // ── Reading Apple's form post ─────────────────────────────────────────────

    [Fact]
    public void A_matching_state_yields_the_identity_token()
    {
        var callback = AppleWebAuthorizeRequest.ReadCallback(
            Form(("state", "abc"), ("id_token", "the.id.token")), "abc");

        Assert.True(callback.Succeeded);
        Assert.Equal("the.id.token", callback.IdentityToken);
        Assert.Null(callback.Code);
    }

    /// <summary>Apple posts the authorization code beside the identity token; it rides along for revocation later (item 229).</summary>
    [Fact]
    public void The_authorization_code_rides_along_when_apple_posts_one()
    {
        var callback = AppleWebAuthorizeRequest.ReadCallback(
            Form(("state", "abc"), ("id_token", "the.id.token"), ("code", "c.abc")), "abc");

        Assert.True(callback.Succeeded);
        Assert.Equal("c.abc", callback.Code);
    }

    /// <summary>
    /// A post this site did not ask for is refused, token or no token.
    /// </summary>
    /// <remarks>
    /// This endpoint is a public POST with antiforgery deliberately off, because Apple cannot send
    /// a token of ours. The state cookie is the only thing distinguishing Apple's answer from
    /// anybody else's, so without this check the site would adopt a session somebody else chose.
    /// </remarks>
    [Theory]
    [InlineData("someone-elses")]
    [InlineData(null)]
    [InlineData("")]
    public void A_post_with_the_wrong_state_is_refused(string? state)
    {
        var callback = AppleWebAuthorizeRequest.ReadCallback(
            Form(("state", state), ("id_token", "planted")), "abc");

        Assert.False(callback.Succeeded);
        Assert.Equal("state_mismatch", callback.Error);
        Assert.Null(callback.IdentityToken);
    }

    /// <summary>And refused just as hard when this browser never started a sign-in at all.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void A_post_with_no_expected_state_is_refused(string? expected)
    {
        var callback = AppleWebAuthorizeRequest.ReadCallback(
            Form(("state", "anything"), ("id_token", "planted")), expected);

        Assert.False(callback.Succeeded);
        Assert.Equal("state_mismatch", callback.Error);
    }

    /// <summary>Closing Apple's page is a decision, and is told apart from a failure.</summary>
    [Fact]
    public void Cancelling_is_told_apart_from_failing()
    {
        var callback = AppleWebAuthorizeRequest.ReadCallback(
            Form(("state", "abc"), ("error", "user_cancelled_authorize")), "abc");

        Assert.False(callback.Succeeded);
        Assert.True(callback.WasCancelled);
    }

    [Fact]
    public void Another_error_is_not_a_cancellation()
    {
        var callback = AppleWebAuthorizeRequest.ReadCallback(
            Form(("state", "abc"), ("error", "invalid_client")), "abc");

        Assert.False(callback.Succeeded);
        Assert.False(callback.WasCancelled);
    }

    // ── Apple's one-shot name ─────────────────────────────────────────────────

    /// <summary>
    /// The name arrives as JSON in a "user" field, on a first authorization only.
    /// </summary>
    /// <remarks>
    /// Apple never sends it again. Not on a later sign-in, not after the account is deleted and
    /// remade. Failing to read it here means the account is named whatever we invented, for good.
    /// </remarks>
    [Fact]
    public void The_name_is_read_from_apples_user_field()
    {
        var callback = AppleWebAuthorizeRequest.ReadCallback(
            Form(("state", "abc"), ("id_token", "t"),
                 ("user", """{"name":{"firstName":"Ada","lastName":"Lovelace"},"email":"a@b.test"}""")),
            "abc");

        Assert.Equal("Ada Lovelace", callback.DisplayName);
    }

    /// <summary>Both parts are joined, so somebody with only one recorded is not left nameless.</summary>
    [Theory]
    [InlineData("""{"name":{"firstName":"Ada","lastName":null}}""", "Ada")]
    [InlineData("""{"name":{"firstName":null,"lastName":"Lovelace"}}""", "Lovelace")]
    [InlineData("""{"name":{"firstName":"","lastName":""}}""", null)]
    [InlineData("""{"email":"a@b.test"}""", null)]
    public void A_partial_name_still_produces_what_there_is(string userJson, string? expected)
        => Assert.Equal(expected, AppleWebAuthorizeRequest.ReadDisplayName(userJson));

    /// <summary>
    /// An unreadable name costs nothing.
    /// </summary>
    /// <remarks>
    /// A sign-in must not fail because a field we merely hoped for was malformed. Answering null
    /// puts the person on the profile form, which is where they were going anyway.
    /// </remarks>
    [Theory]
    [InlineData("not json at all")]
    [InlineData("{")]
    [InlineData("")]
    [InlineData(null)]
    public void An_unreadable_name_does_not_cost_the_sign_in(string? userJson)
        => Assert.Null(AppleWebAuthorizeRequest.ReadDisplayName(userJson));

    /// <summary>A later authorization simply has no name, and that is normal.</summary>
    [Fact]
    public void A_later_authorization_carries_no_name_and_still_succeeds()
    {
        var callback = AppleWebAuthorizeRequest.ReadCallback(
            Form(("state", "abc"), ("id_token", "t")), "abc");

        Assert.True(callback.Succeeded);
        Assert.Null(callback.DisplayName);
    }
}
