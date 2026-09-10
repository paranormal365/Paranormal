using System.Net;
using Ben.Data.WebApi.Client.Auth;
using Ben.Data.WebApi.Client.External;
using Ben.Web.Services.WebApi;

namespace Ben.Data.WebApi.Client.Tests;

/// <summary>Everything after Apple's sheet closes, on any front end.</summary>
public sealed class AppleSignInClientTests
{
    /// <summary>Records what the client tried to sign in with. Each front end has its own real one.</summary>
    private sealed class FakeAdopter : IExternalSignInAdopter
    {
        public WebApiTokenResponse? Adopted { get; private set; }

        public Task AdoptExternalSignInAsync(WebApiTokenResponse response, CancellationToken token = default)
        {
            Adopted = response;
            return Task.CompletedTask;
        }
    }

    private static (AppleSignInClient client, FakeAdopter adopter) Build(StubHandler handler)
    {
        var adopter = new FakeAdopter();
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        return (new AppleSignInClient(http, adopter), adopter);
    }

    /// <summary>
    /// The endpoint answers with one of our own sessions, so an Apple sign-in is an ordinary one.
    /// </summary>
    [Fact]
    public async Task An_accepted_token_is_adopted_as_an_ordinary_session()
    {
        var (client, adopter) = Build(StubHandler.Always(HttpStatusCode.OK, Fixture.Read("login-200.json")));

        var outcome = await client.SignInAsync("an-apple-identity-token");

        Assert.True(outcome.Succeeded);
        Assert.NotNull(adopter.Adopted);
        Assert.Equal("REDACTED-ACCESS-TOKEN", adopter.Adopted!.AccessToken);
    }

    /// <summary>A 409 is not a failure: Apple vouched for somebody the site has never seen.</summary>
    [Fact]
    public async Task A_new_person_is_asked_for_a_profile_rather_than_refused()
    {
        var (client, adopter) = Build(StubHandler.Always(HttpStatusCode.Conflict, """
            {"needsProfile":true,"suggestedDisplayName":"Ada Lovelace","email":"ada@example.test","isPrivateEmail":false,"handleProblem":null}
            """));

        var outcome = await client.SignInAsync("token", displayName: "Ada Lovelace");

        Assert.False(outcome.Succeeded);
        Assert.True(outcome.RequiresProfile);
        Assert.Equal("Ada Lovelace", outcome.NeedsProfile!.SuggestedDisplayName);
        Assert.Null(outcome.Reason);
        Assert.Null(adopter.Adopted);
    }

    [Fact]
    public async Task A_private_relay_address_is_flagged()
    {
        var (client, _) = Build(StubHandler.Always(HttpStatusCode.Conflict, """
            {"needsProfile":true,"suggestedDisplayName":null,"email":"abc123@privaterelay.appleid.com","isPrivateEmail":true,"handleProblem":null}
            """));

        var outcome = await client.SignInAsync("token");

        Assert.True(outcome.NeedsProfile!.IsPrivateEmail);
    }

    [Fact]
    public async Task A_rejected_handle_says_why()
    {
        var (client, _) = Build(StubHandler.Always(HttpStatusCode.Conflict, """
            {"needsProfile":true,"suggestedDisplayName":"Ada","email":"a@b.test","isPrivateEmail":false,"handleProblem":"That name is taken."}
            """));

        var outcome = await client.SignInAsync("token", "Ada", "ada");

        Assert.Equal("That name is taken.", outcome.NeedsProfile!.HandleProblem);
    }

    /// <summary>A taken address routes to the link door rather than complaining about the handle.</summary>
    [Fact]
    public async Task A_taken_address_is_routed_to_linking()
    {
        var (client, _) = Build(StubHandler.Always(HttpStatusCode.Conflict, """
            {"needsProfile":true,"suggestedDisplayName":"Ada","email":"a@b.test","isPrivateEmail":false,"handleProblem":null,"shouldLinkInstead":true,"emailProblem":"That email address already has an account here."}
            """));

        var outcome = await client.SignInAsync("token", "Ada", "ada");

        Assert.True(outcome.NeedsProfile!.ShouldLinkInstead);
        Assert.Contains("already has an account", outcome.NeedsProfile.EmailProblem);
        Assert.Null(outcome.NeedsProfile.HandleProblem);
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

    /// <summary>An empty string is a value; the server would take it as a chosen name of nothing.</summary>
    /// <summary>The authorization code rides along when the client has one, and is absent when it does not (item 229).</summary>
    [Fact]
    public async Task The_authorization_code_is_sent_when_held_and_omitted_when_not()
    {
        var handler = StubHandler.Always(HttpStatusCode.OK, Fixture.Read("login-200.json"));
        var (client, _) = Build(handler);

        await client.SignInAsync("t", authorizationCode: "c-1");
        Assert.Contains("\"authorizationCode\":\"c-1\"", handler.LastBody);

        await client.SignInAsync("t");
        Assert.DoesNotContain("authorizationCode", handler.LastBody);

        await client.LinkAsync("t", "a@b.test", "pw", authorizationCode: "c-2");
        Assert.Contains("\"authorizationCode\":\"c-2\"", handler.LastBody);
    }

    [Fact]
    public async Task Nothing_optional_is_sent_when_there_is_nothing_to_send()
    {
        var handler = StubHandler.Always(HttpStatusCode.OK, Fixture.Read("login-200.json"));
        var (client, _) = Build(handler);

        await client.SignInAsync("token");

        Assert.DoesNotContain("displayName", handler.LastBody);
        Assert.DoesNotContain("handle", handler.LastBody);
    }

    /// <summary>A server with no Apple audience configured is a closed door, not a bad token.</summary>
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
    public async Task A_rejected_token_keeps_the_servers_own_sentence()
    {
        var (client, _) = Build(StubHandler.Always(HttpStatusCode.Unauthorized,
            "That Apple sign-in couldn't be verified. Try again.", "text/plain"));

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

    /// <summary>The link call carries the ACCOUNT's address, not Apple's. That is the whole point.</summary>
    [Fact]
    public async Task Linking_sends_the_account_address_not_apples()
    {
        var handler = StubHandler.Always(HttpStatusCode.OK, Fixture.Read("login-200.json"));
        var (client, adopter) = Build(handler);

        await client.LinkAsync("apple-token", "ben@ishaunted.com", "my-password");

        Assert.Contains("api/auth/apple/link", handler.LastRequest!.RequestUri!.ToString());
        Assert.Contains("ben@ishaunted.com", handler.LastBody);
        Assert.Contains("apple-token", handler.LastBody);
        Assert.DoesNotContain("privaterelay", handler.LastBody);
        Assert.NotNull(adopter.Adopted);
    }

    /// <summary>A successful link signs them in outright: an Apple token is not a credential the API accepts on ordinary requests.</summary>
    [Fact]
    public async Task A_successful_link_signs_them_in()
    {
        var (client, adopter) = Build(StubHandler.Always(HttpStatusCode.OK, Fixture.Read("login-200.json")));

        var outcome = await client.LinkAsync("apple-token", "ben@ishaunted.com", "pw");

        Assert.True(outcome.Succeeded);
        Assert.NotNull(adopter.Adopted);
    }

    [Fact]
    public async Task A_wrong_password_refuses_the_link()
    {
        var (client, adopter) = Build(StubHandler.Always(HttpStatusCode.Unauthorized,
            """{"status":401,"detail":"Failed"}"""));

        var outcome = await client.LinkAsync("apple-token", "ben@ishaunted.com", "wrong");

        Assert.False(outcome.Succeeded);
        Assert.Equal(LoginFailure.InvalidCredentials, outcome.Failure);
        Assert.Contains("don't match an account", outcome.Reason);
        Assert.Null(adopter.Adopted);
    }

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

    /// <summary>Being asked for a code is not a failure: the password was right.</summary>
    [Fact]
    public async Task A_two_factor_account_asks_for_a_code_when_linking()
    {
        var (client, adopter) = Build(StubHandler.Always(HttpStatusCode.Unauthorized,
            """{"type":"...","title":"Unauthorized","status":401,"detail":"RequiresTwoFactor"}"""));

        var outcome = await client.LinkAsync("apple-token", "ben@ishaunted.com", "correct horse");

        Assert.False(outcome.Succeeded);
        Assert.True(outcome.RequiresTwoFactor);
        Assert.Equal(LoginFailure.RequiresTwoFactor, outcome.Failure);
        Assert.Null(adopter.Adopted);
    }

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

    /// <summary>An empty string is an attempt with a wrong code, so nothing is sent until asked.</summary>
    [Fact]
    public async Task No_two_factor_field_is_sent_on_the_first_link_attempt()
    {
        var handler = StubHandler.Always(HttpStatusCode.OK, Fixture.Read("login-200.json"));
        var (client, _) = Build(handler);

        await client.LinkAsync("apple-token", "a@b.test", "pw");

        Assert.DoesNotContain("twoFactorCode", handler.LastBody);
        Assert.DoesNotContain("twoFactorRecoveryCode", handler.LastBody);
    }

    /// <summary>The four refusals a 401 can mean are told apart, exactly as on the sign-in form.</summary>
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

    /// <summary>An unreadable refusal is unknown, never "your password is wrong".</summary>
    [Fact]
    public async Task An_unreadable_refusal_does_not_blame_the_password()
    {
        var (client, _) = Build(StubHandler.Always(HttpStatusCode.Unauthorized, "<html>a proxy page</html>"));
        var wrongPassword = Build(StubHandler.Always(HttpStatusCode.Unauthorized,
            """{"status":401,"detail":"Failed"}""")).client;

        var outcome = await client.LinkAsync("apple-token", "a@b.test", "pw");
        var credentials = await wrongPassword.LinkAsync("apple-token", "a@b.test", "pw");

        Assert.Equal(LoginFailure.UnknownRefusal, outcome.Failure);
        Assert.NotEqual(credentials.Reason, outcome.Reason);
    }
}
