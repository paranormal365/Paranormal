using System.Net;
using Ben.Data.WebApi.Client.Auth;

namespace Ben.Data.WebApi.Client.Tests;

/// <summary>
/// Creating an account, and being told why you could not.
/// </summary>
/// <remarks>
/// The refusals here are captured, and capturing them is what settled a real question: this
/// endpoint answers a refusal as a JSON document rather than as a sentence, so the ordinary
/// refusal handling — which discards any body starting with a brace — would have thrown away the
/// only text that tells somebody what to change.
/// </remarks>
public sealed class AccountClientTests
{
    private static AccountClient Build(StubHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") });

    [Fact]
    public async Task A_successful_sign_up_carries_the_server_sentence()
    {
        var client = Build(StubHandler.Always(HttpStatusCode.OK, Fixture.Read("account-register-200.json")));

        var result = await client.RegisterAsync(new RegisterRequest("a@b.test", "pw", "A B", "ab"));

        Assert.True(result.Succeeded);
        Assert.Contains("confirm the address", result.Message);
    }

    /// <summary>
    /// A taken handle names the field to point at, and says so in the server's own words.
    /// </summary>
    /// <remarks>
    /// The captured body is <c>{"succeeded":false,"message":"That name is taken.","field":"Handle"}</c>
    /// with a 400. Read as ordinary prose it would be discarded for starting with a brace, and the
    /// person would be shown a paraphrase of the status code instead of the one thing they can act on.
    /// </remarks>
    [Fact]
    public async Task A_refusal_is_read_from_the_body_even_though_it_is_json()
    {
        var client = Build(StubHandler.Always(
            HttpStatusCode.BadRequest, Fixture.Read("account-register-refused.json")));

        var result = await client.RegisterAsync(new RegisterRequest("a@b.test", "pw", "A B", "taken"));

        Assert.False(result.Succeeded);
        Assert.Equal("That name is taken.", result.Message);
        Assert.Equal("Handle", result.Field);
    }

    /// <summary>An unreachable server must not read as a refusal — nothing was created either way.</summary>
    [Fact]
    public async Task An_unreachable_server_says_so_rather_than_blaming_the_form()
    {
        var client = Build(new StubHandler().ThenUnreachable());

        var result = await client.RegisterAsync(new RegisterRequest("a@b.test", "pw", "A B", "ab"));

        Assert.False(result.Succeeded);
        Assert.Contains("Couldn't reach the server", result.Message);
        Assert.Null(result.Field);
    }

    [Fact]
    public async Task A_free_handle_is_reported_available()
    {
        var client = Build(StubHandler.Always(HttpStatusCode.OK, Fixture.Read("handle-available-200.json")));

        var result = await client.CheckHandleAsync("zzz_never_taken_225");

        Assert.NotNull(result);
        Assert.True(result!.Available);
    }

    /// <summary>
    /// A check that could not be made answers null, never "available".
    /// </summary>
    /// <remarks>
    /// Handles are permanent. A green tick meaning "we could not ask" invites somebody to commit
    /// to a name that is already gone, and there is no undoing it afterwards.
    /// </remarks>
    [Fact]
    public async Task A_handle_check_that_failed_is_not_a_green_tick()
    {
        var client = Build(new StubHandler().ThenUnreachable());

        Assert.Null(await client.CheckHandleAsync("anything"));
    }

    /// <summary>
    /// A confirmation link that never reached the server is not a spent link.
    /// </summary>
    /// <remarks>
    /// The difference decides whether "try again" is useful advice or a waste of somebody's
    /// afternoon looking for an email they already have.
    /// </remarks>
    [Fact]
    public async Task An_unreachable_confirmation_says_the_link_is_still_good()
    {
        var client = Build(new StubHandler().ThenUnreachable());

        var result = await client.ConfirmEmailAsync(Guid.NewGuid(), "code");

        Assert.False(result.Succeeded);
        Assert.Contains("still good", result.Message);
    }

    /// <summary>The resend cooldown is named rather than reported as a failure.</summary>
    [Fact]
    public async Task A_resend_inside_the_cooldown_explains_the_wait()
    {
        var client = Build(StubHandler.Always(HttpStatusCode.TooManyRequests, """{"error":"..."}"""));

        var message = await client.ResendConfirmationAsync("a@b.test");

        Assert.Contains("give it a minute", message, StringComparison.OrdinalIgnoreCase);
    }
}
