using System.Web;
using Ben.Data.WebApi.Client.External;

namespace Ben.Data.WebApi.Client.Tests;

/// <summary>
/// The parts of a Microsoft sign-in that happen either side of the browser.
/// </summary>
/// <remarks>
/// All pure, and that is the point of the design: the only step needing a person, a browser and a
/// tenant is "open this URL and give me back the redirect". Everything that can be got wrong
/// silently — the proof key encoding, the state check, the scopes — is testable here.
/// </remarks>
public sealed class EntraHandshakeTests
{
    private static readonly EntraOptions Options = new(
        ClientId: "3e37e6d7-13ea-4b94-b271-618267256d8b",
        Scope: "api://3e37e6d7-13ea-4b94-b271-618267256d8b/access_as_user",
        RedirectUri: "msauth.com.ishaunted.desktop://auth");

    // ── The proof key ─────────────────────────────────────────────────────────

    /// <summary>
    /// The challenge is the SHA-256 of the verifier, base64url, unpadded.
    /// </summary>
    /// <remarks>
    /// Checked against a vector computed independently rather than against the implementation's own
    /// output, which would agree with any encoding bug. Ordinary base64 would emit '+', '/' and
    /// '=' here, and every sign-in would be refused as an invalid grant with nothing saying why.
    /// </remarks>
    [Fact]
    public void The_challenge_is_the_sha256_of_the_verifier()
    {
        var pkce = EntraPkce.Create();

        var expected = Convert.ToBase64String(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.ASCII.GetBytes(pkce.CodeVerifier)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        Assert.Equal(expected, pkce.CodeChallenge);
        Assert.DoesNotContain('=', pkce.CodeChallenge);
        Assert.DoesNotContain('+', pkce.CodeChallenge);
        Assert.DoesNotContain('/', pkce.CodeChallenge);
    }

    /// <summary>RFC 7636 puts the verifier between 43 and 128 characters.</summary>
    [Fact]
    public void The_verifier_is_a_legal_length()
    {
        var pkce = EntraPkce.Create();

        Assert.InRange(pkce.CodeVerifier.Length, 43, 128);
    }

    /// <summary>A predictable verifier is no proof at all.</summary>
    [Fact]
    public void Every_attempt_gets_its_own_verifier()
    {
        var verifiers = Enumerable.Range(0, 50).Select(_ => EntraPkce.Create().CodeVerifier).ToHashSet();

        Assert.Equal(50, verifiers.Count);
    }

    // ── The authorize URL ─────────────────────────────────────────────────────

    [Fact]
    public void The_authorize_url_carries_the_challenge_and_not_the_verifier()
    {
        var pkce = EntraPkce.Create();
        var url = EntraAuthorizeRequest.Build(Options, pkce, "some-state");
        var query = HttpUtility.ParseQueryString(url.Query);

        Assert.Equal(pkce.CodeChallenge, query["code_challenge"]);
        Assert.Equal("S256", query["code_challenge_method"]);

        // The verifier is the secret. If it ever travelled in the browser the proof would be
        // worthless, because whoever intercepted the code would have both halves.
        Assert.DoesNotContain(pkce.CodeVerifier, url.ToString());
    }

    /// <summary>
    /// offline_access is what makes a refresh token come back.
    /// </summary>
    /// <remarks>
    /// Without it everything works for about an hour and then quietly sends somebody through a
    /// browser again, which reads as the app forgetting them rather than as a missing scope.
    /// </remarks>
    [Fact]
    public void The_authorize_url_asks_for_a_refresh_token()
    {
        var url = EntraAuthorizeRequest.Build(Options, EntraPkce.Create(), "s");
        var scope = HttpUtility.ParseQueryString(url.Query)["scope"];

        Assert.Contains("offline_access", scope);
        Assert.Contains(Options.Scope, scope);
    }

    [Fact]
    public void The_authorize_url_asks_for_a_code_at_the_registered_redirect()
    {
        var url = EntraAuthorizeRequest.Build(Options, EntraPkce.Create(), "s");
        var query = HttpUtility.ParseQueryString(url.Query);

        Assert.Equal("code", query["response_type"]);
        Assert.Equal(Options.ClientId, query["client_id"]);
        Assert.Equal(Options.RedirectUri, query["redirect_uri"]);
        Assert.StartsWith("https://login.microsoftonline.com/common/v2.0/authorize", url.ToString());
    }

    // ── Reading the redirect ──────────────────────────────────────────────────

    [Fact]
    public void A_matching_state_yields_the_code()
    {
        var callback = EntraAuthorizeRequest.ReadCallback(
            new Uri("msauth.com.ishaunted.desktop://auth?code=the-code&state=abc123"), "abc123");

        Assert.True(callback.Succeeded);
        Assert.Equal("the-code", callback.Code);
    }

    /// <summary>
    /// A redirect this app did not start is refused, code or no code.
    /// </summary>
    /// <remarks>
    /// Any app on the machine can register the same URL scheme, and a link can be sent to somebody.
    /// Without this check either of those is accepted as the answer to a sign-in that never began,
    /// and the app adopts a session somebody else chose.
    /// </remarks>
    [Theory]
    [InlineData("msauth.com.ishaunted.desktop://auth?code=planted&state=someone-elses")]
    [InlineData("msauth.com.ishaunted.desktop://auth?code=planted")]
    public void A_redirect_with_the_wrong_state_is_refused(string redirect)
    {
        var callback = EntraAuthorizeRequest.ReadCallback(new Uri(redirect), "abc123");

        Assert.False(callback.Succeeded);
        Assert.Equal("state_mismatch", callback.Error);
        Assert.Null(callback.Code);
    }

    /// <summary>Closing the window is a decision, not a failure to report as one.</summary>
    [Fact]
    public void Cancelling_is_told_apart_from_failing()
    {
        var callback = EntraAuthorizeRequest.ReadCallback(
            new Uri("msauth.com.ishaunted.desktop://auth?error=access_denied&error_description=The+user+cancelled&state=abc"),
            "abc");

        Assert.False(callback.Succeeded);
        Assert.True(callback.WasCancelled);
    }

    [Fact]
    public void A_refusal_carries_the_description()
    {
        var callback = EntraAuthorizeRequest.ReadCallback(
            new Uri("msauth.com.ishaunted.desktop://auth?error=invalid_scope&error_description=Bad+scope&state=abc"),
            "abc");

        Assert.False(callback.Succeeded);
        Assert.False(callback.WasCancelled);
        Assert.Equal("Bad scope", callback.ErrorDescription);
    }

    /// <summary>An answer in the fragment is still an answer.</summary>
    [Fact]
    public void A_fragment_response_is_read_too()
    {
        var callback = EntraAuthorizeRequest.ReadCallback(
            new Uri("msauth.com.ishaunted.desktop://auth#code=frag-code&state=abc"), "abc");

        Assert.True(callback.Succeeded);
        Assert.Equal("frag-code", callback.Code);
    }

    [Fact]
    public void Every_attempt_gets_its_own_state()
    {
        var states = Enumerable.Range(0, 50).Select(_ => EntraAuthorizeRequest.NewState()).ToHashSet();

        Assert.Equal(50, states.Count);
    }

    // ── Configuration ─────────────────────────────────────────────────────────

    /// <summary>
    /// An unconfigured build must not offer the button.
    /// </summary>
    /// <remarks>
    /// The API only enables its Microsoft scheme when it is configured for one. A button that
    /// cannot work is worse than no button — it looks like a broken feature rather than an absent
    /// one.
    /// </remarks>
    [Theory]
    [InlineData("", "scope", "redirect", false)]
    [InlineData("client", "", "redirect", false)]
    [InlineData("client", "scope", "", false)]
    [InlineData("client", "scope", "redirect", true)]
    public void Availability_needs_all_three_settings(string id, string scope, string redirect, bool configured)
        => Assert.Equal(configured, new EntraOptions(id, scope, redirect).IsConfigured);

    [Theory]
    [InlineData("msauth.com.ishaunted.desktop://auth", "msauth.com.ishaunted.desktop")]
    [InlineData("http://localhost:1234/", "http")]
    public void The_callback_scheme_comes_from_the_redirect(string redirect, string scheme)
        => Assert.Equal(scheme, EntraSignInService.CallbackScheme(redirect));
}
