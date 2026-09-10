using System.Net;
using Ben.Data.WebApi.Client.Auth;
using Ben.Web.Services.WebApi;
using Ben.Data.WebApi.Client.External;
using Ben.Web.Services.WebApi;

namespace Ben.Data.WebApi.Client.Tests;

/// <summary>Everything after Apple's sheet closes.</summary>
public sealed class AppleSignInClientTests
{
    private static (AppleSignInClient client, SessionStore store) Build(
        StubHandler handler, MeResponse? me = null)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        var identity = new WebApiIdentityClient(http);
        var session = new TokenSession(new InMemoryTokenStorage(), identity);
        var store = new SessionStore(session, identity,
            _ => Task.FromResult(ItemResult<MeResponse>.Ok(
                me ?? new MeResponse(Guid.NewGuid(), "apple@example.test", false, false))));

        return (new AppleSignInClient(http, store), store);
    }

    /// <summary>
    /// The endpoint answers with one of our own sessions, so an Apple sign-in is an ordinary one.
    /// </summary>
    /// <remarks>
    /// Deliberate on the server's part: it signs the person in under our bearer scheme and returns
    /// a body identical to what <c>/login</c> returns. Nothing downstream needs to know Apple was
    /// involved — which is why this does not use the external-session path the Microsoft flow does.
    /// </remarks>
    [Fact]
    public async Task An_accepted_token_signs_them_in_normally()
    {
        var (client, store) = Build(StubHandler.Always(HttpStatusCode.OK, Fixture.Read("login-200.json")));

        var outcome = await client.SignInAsync("an-apple-identity-token");

        Assert.True(outcome.Succeeded);
        Assert.Equal(SessionPhase.SignedIn, store.State.Phase);
    }

    /// <summary>
    /// A 409 is not a failure: Apple vouched for somebody the site has never seen.
    /// </summary>
    /// <remarks>
    /// Apple gives a real name on the FIRST authorization only. A suggestion that arrives here has
    /// to be used now, because asking Apple again returns nothing and the account ends up named
    /// whatever the client invented.
    /// </remarks>
    [Fact]
    public async Task A_new_person_is_asked_for_a_profile_rather_than_refused()
    {
        var (client, _) = Build(StubHandler.Always(HttpStatusCode.Conflict, """
            {"needsProfile":true,"suggestedDisplayName":"Ada Lovelace","email":"ada@example.test","isPrivateEmail":false,"handleProblem":null}
            """));

        var outcome = await client.SignInAsync("token", displayName: "Ada Lovelace");

        Assert.False(outcome.Succeeded);
        Assert.True(outcome.RequiresProfile);
        Assert.Equal("Ada Lovelace", outcome.NeedsProfile!.SuggestedDisplayName);
        Assert.Null(outcome.Reason);   // nothing went wrong; there is nothing to apologise for
    }

    /// <summary>A relay address is flagged, so nothing presents it as the person's own.</summary>
    [Fact]
    public async Task A_private_relay_address_is_flagged()
    {
        var (client, _) = Build(StubHandler.Always(HttpStatusCode.Conflict, """
            {"needsProfile":true,"suggestedDisplayName":null,"email":"abc123@privaterelay.appleid.com","isPrivateEmail":true,"handleProblem":null}
            """));

        var outcome = await client.SignInAsync("token");

        Assert.True(outcome.NeedsProfile!.IsPrivateEmail);
    }

    /// <summary>The handle complaint comes back so the second attempt can fix it.</summary>
    [Fact]
    public async Task A_rejected_handle_says_why()
    {
        var (client, _) = Build(StubHandler.Always(HttpStatusCode.Conflict, """
            {"needsProfile":true,"suggestedDisplayName":"Ada","email":"a@b.test","isPrivateEmail":false,"handleProblem":"That name is taken."}
            """));

        var outcome = await client.SignInAsync("token", "Ada", "ada");

        Assert.Equal("That name is taken.", outcome.NeedsProfile!.HandleProblem);
    }

    [Fact]
    public async Task The_chosen_name_and_handle_are_sent_on_the_second_attempt()
    {
        var handler = StubHandler.Always(HttpStatusCode.OK, Fixture.Read("login-200.json"));
        var (client, _) = Build(handler);

        await client.SignInAsync("token", "Ada Lovelace", "ada");

        Assert.Contains("\"displayName\":\"Ada Lovelace\"", handler.LastBody);
        Assert.Contains("\"handle\":\"ada\"", handler.LastBody);
    }

    /// <summary>
    /// Nothing optional is sent when there is nothing to send.
    /// </summary>
    /// <remarks>
    /// An empty string is a value, and the server would take it as a chosen name of nothing.
    /// </remarks>
    [Fact]
    public async Task Nothing_optional_is_sent_when_there_is_nothing_to_send()
    {
        var handler = StubHandler.Always(HttpStatusCode.OK, Fixture.Read("login-200.json"));
        var (client, _) = Build(handler);

        await client.SignInAsync("token");

        Assert.DoesNotContain("displayName", handler.LastBody);
        Assert.DoesNotContain("handle", handler.LastBody);
    }

    /// <summary>
    /// A server with no Apple audience configured is a closed door, not a bad token.
    /// </summary>
    /// <remarks>
    /// Nothing the person does changes a 503 here. Telling them to try again would have them retype
    /// a thing that cannot work.
    /// </remarks>
    [Fact]
    public async Task An_unconfigured_server_says_the_door_is_shut()
    {
        var (client, _) = Build(StubHandler.Always(HttpStatusCode.ServiceUnavailable, ""));

        var outcome = await client.SignInAsync("token");

        Assert.False(outcome.Succeeded);
        Assert.False(outcome.RequiresProfile);
        Assert.Contains("isn't switched on", outcome.Reason);
    }

    [Fact]
    public async Task A_rejected_token_is_worth_retrying()
    {
        var (client, _) = Build(StubHandler.Always(HttpStatusCode.Unauthorized, ""));

        var outcome = await client.SignInAsync("stale");

        Assert.False(outcome.Succeeded);
        Assert.Contains("couldn't be verified", outcome.Reason);
    }

    [Fact]
    public async Task An_unreachable_server_is_named_as_such()
    {
        var (client, _) = Build(new StubHandler().ThenUnreachable());

        var outcome = await client.SignInAsync("token");

        Assert.False(outcome.Succeeded);
        Assert.Contains("Couldn't reach the server", outcome.Reason);
    }

    // ── Claiming an account the provider's address could never match ──────────

    /// <summary>
    /// The link call carries the account's OWN address, not Apple's.
    /// </summary>
    /// <remarks>
    /// This is the whole point of it. Sign-in only joins an Apple identity to an existing account
    /// when Apple's verified email happens to equal one, and a Hide My Email relay address never
    /// will. Sending Apple's address here would reproduce exactly the failure the endpoint exists
    /// to fix.
    /// </remarks>
    [Fact]
    public async Task Linking_sends_the_account_address_not_apples()
    {
        var handler = StubHandler.Always(HttpStatusCode.OK, Fixture.Read("login-200.json"));
        var (client, store) = Build(handler);

        await client.LinkAsync("apple-token", "ben@ishaunted.com", "my-password");

        Assert.Contains("api/auth/apple/link", handler.LastRequest!.RequestUri!.ToString());
        Assert.Contains("ben@ishaunted.com", handler.LastBody);
        Assert.Contains("apple-token", handler.LastBody);
        Assert.DoesNotContain("privaterelay", handler.LastBody);
        Assert.Equal(SessionPhase.SignedIn, store.State.Phase);
    }

    /// <summary>
    /// A successful link signs them in outright.
    /// </summary>
    /// <remarks>
    /// Unlike the Microsoft equivalent, which can lean on the token it already holds: an Apple
    /// identity token is not a credential the API accepts on ordinary requests, so this endpoint
    /// has to answer with a real session or the person is left holding nothing.
    /// </remarks>
    [Fact]
    public async Task A_successful_link_signs_them_in()
    {
        var (client, store) = Build(StubHandler.Always(HttpStatusCode.OK, Fixture.Read("login-200.json")));

        var outcome = await client.LinkAsync("apple-token", "ben@ishaunted.com", "pw");

        Assert.True(outcome.Succeeded);
        Assert.Equal(SessionPhase.SignedIn, store.State.Phase);
    }

    [Fact]
    public async Task A_wrong_password_refuses_the_link()
    {
        var (client, store) = Build(StubHandler.Always(HttpStatusCode.Unauthorized,
            """{"status":401,"detail":"Failed"}"""));

        var outcome = await client.LinkAsync("apple-token", "ben@ishaunted.com", "wrong");

        Assert.False(outcome.Succeeded);
        Assert.Equal(LoginFailure.InvalidCredentials, outcome.Failure);
        Assert.Contains("don't match an account", outcome.Reason);
        Assert.NotEqual(SessionPhase.SignedIn, store.State.Phase);
    }

    /// <summary>An Apple identity already held elsewhere cannot be moved by whoever asks last.</summary>
    [Fact]
    public async Task An_apple_identity_already_linked_elsewhere_says_so()
    {
        var (client, _) = Build(StubHandler.Always(HttpStatusCode.Conflict,
            """{"message":"That Apple account is already linked to a different account here."}"""));

        var outcome = await client.LinkAsync("apple-token", "ben@ishaunted.com", "pw");

        Assert.False(outcome.Succeeded);
        Assert.Contains("already linked", outcome.Reason);
    }

    [Fact]
    public async Task An_unreachable_server_links_nothing_and_says_so()
    {
        var (client, _) = Build(new StubHandler().ThenUnreachable());

        var outcome = await client.LinkAsync("apple-token", "a@b.test", "pw");

        Assert.False(outcome.Succeeded);
        Assert.Contains("Nothing was linked", outcome.Reason);
    }

    // ── A second factor is not walked around by linking ───────────────────────

    /// <summary>
    /// Being asked for a code is not a failure, and the client must not present it as one.
    /// </summary>
    /// <remarks>
    /// The password was right. Reporting this as a refusal would leave somebody retyping a password
    /// that already worked, looking for a mistake they did not make.
    /// </remarks>
    [Fact]
    public async Task A_two_factor_account_asks_for_a_code_when_linking()
    {
        var (client, store) = Build(StubHandler.Always(HttpStatusCode.Unauthorized,
            """{"type":"...","title":"Unauthorized","status":401,"detail":"RequiresTwoFactor"}"""));

        var outcome = await client.LinkAsync("apple-token", "ben@ishaunted.com", "correct horse");

        Assert.False(outcome.Succeeded);
        Assert.True(outcome.RequiresTwoFactor);
        Assert.Equal(LoginFailure.RequiresTwoFactor, outcome.Failure);
        Assert.NotEqual(SessionPhase.SignedIn, store.State.Phase);
    }

    /// <summary>The code goes in the field that matches what they said they were typing.</summary>
    [Theory]
    [InlineData(false, "twoFactorCode")]
    [InlineData(true, "twoFactorRecoveryCode")]
    public async Task The_code_is_sent_in_the_right_field(bool recovery, string expectedField)
    {
        var handler = StubHandler.Always(HttpStatusCode.OK, Fixture.Read("login-200.json"));
        var (client, _) = Build(handler);

        await client.LinkAsync("apple-token", "a@b.test", "pw",
            twoFactorCode: recovery ? null : "123456",
            recoveryCode: recovery ? "123456" : null);

        Assert.Contains(expectedField, handler.LastBody);
    }

    /// <summary>
    /// Nothing is sent when there is no code, rather than an empty string.
    /// </summary>
    /// <remarks>
    /// Identity reads an empty string as an attempt with a wrong code, which spends a failure
    /// against an account that may not even have a second factor.
    /// </remarks>
    [Fact]
    public async Task No_two_factor_field_is_sent_on_the_first_link_attempt()
    {
        var handler = StubHandler.Always(HttpStatusCode.OK, Fixture.Read("login-200.json"));
        var (client, _) = Build(handler);

        await client.LinkAsync("apple-token", "a@b.test", "pw");

        Assert.DoesNotContain("twoFactorCode", handler.LastBody);
        Assert.DoesNotContain("twoFactorRecoveryCode", handler.LastBody);
    }

    /// <summary>
    /// The four refusals a 401 can mean are told apart, exactly as on the sign-in form.
    /// </summary>
    /// <remarks>
    /// Wait, enter your code, confirm your email, or fix your password are four different
    /// instructions, and three of them are useless advice if the fourth is guessed.
    /// </remarks>
    [Theory]
    [InlineData("Failed", LoginFailure.InvalidCredentials)]
    [InlineData("NotAllowed", LoginFailure.EmailNotConfirmed)]
    [InlineData("LockedOut", LoginFailure.LockedOut)]
    [InlineData("RequiresTwoFactor", LoginFailure.RequiresTwoFactor)]
    public async Task Each_refusal_is_named_for_what_it_is(string detail, LoginFailure expected)
    {
        var (client, _) = Build(StubHandler.Always(HttpStatusCode.Unauthorized,
            $$"""{"status":401,"detail":"{{detail}}"}"""));

        var outcome = await client.LinkAsync("apple-token", "a@b.test", "pw");

        Assert.Equal(expected, outcome.Failure);
        Assert.False(string.IsNullOrWhiteSpace(outcome.Reason));
    }

    /// <summary>
    /// A refusal whose reason could not be read is unknown, never "your password is wrong".
    /// </summary>
    /// <remarks>
    /// A truncated body, an aborted read, a proxy page. Reporting any of those as bad credentials
    /// sends somebody to reset a password that was always right — the mistake the whole
    /// LoginFailure vocabulary exists to prevent.
    /// </remarks>
    [Fact]
    public async Task An_unreadable_refusal_does_not_blame_the_password()
    {
        var (client, _) = Build(StubHandler.Always(HttpStatusCode.Unauthorized, "<html>a proxy page</html>"));

        var wrongPassword = Build(StubHandler.Always(HttpStatusCode.Unauthorized,
            """{"status":401,"detail":"Failed"}""")).client;

        var outcome = await client.LinkAsync("apple-token", "a@b.test", "pw");
        var credentials = await wrongPassword.LinkAsync("apple-token", "a@b.test", "pw");

        Assert.Equal(LoginFailure.UnknownRefusal, outcome.Failure);

        // Compared against what a genuine wrong password says, rather than scanning for a word:
        // the honest sentence here happens to contain "password", because it says it ISN'T one.
        Assert.NotEqual(credentials.Reason, outcome.Reason);
    }
}
