using System.Net;
using Ben.Data.WebApi.Client.External;

namespace Ben.Data.WebApi.Client.Tests;

/// <summary>Giving a Microsoft identity an account here, or attaching it to one that exists.</summary>
public sealed class EntraAccountClientTests
{
    private static EntraAccountClient Build(StubHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") });

    [Fact]
    public async Task A_created_account_succeeds()
    {
        var result = await Build(StubHandler.Always(HttpStatusCode.OK,
            """{"userId":"6b64ab13-fb6a-4e83-2eef-08df0f397c22","email":"a@contoso.com"}"""))
            .RegisterAsync("Ada Lovelace");

        Assert.True(result.Succeeded);
    }

    /// <summary>
    /// Only the display name is sent. The identity comes from the token.
    /// </summary>
    /// <remarks>
    /// The server reads the Microsoft object id and address off the validated token and ignores
    /// anything in the body, because trusting a caller-supplied identifier there once let an
    /// account be claimed by somebody who did not hold it. Sending one anyway would imply it
    /// mattered.
    /// </remarks>
    [Fact]
    public async Task Only_the_display_name_is_sent()
    {
        var handler = StubHandler.Always(HttpStatusCode.OK, "{}");

        await Build(handler).RegisterAsync("Ada Lovelace");

        Assert.Contains("Ada Lovelace", handler.LastBody);
        Assert.DoesNotContain("oid", handler.LastBody);
        Assert.DoesNotContain("email", handler.LastBody);
    }

    /// <summary>
    /// An address that already has an account needs the other door, not an error.
    /// </summary>
    /// <remarks>
    /// Reporting a 409 as "couldn't create your account" leaves somebody in front of the wrong form
    /// with no idea that the right one exists.
    /// </remarks>
    [Fact]
    public async Task An_existing_address_is_pointed_at_linking()
    {
        var result = await Build(StubHandler.Always(HttpStatusCode.Conflict,
            """{"message":"An account with this email already exists. Please use the 'Link existing account' option."}"""))
            .RegisterAsync("Ada");

        Assert.False(result.Succeeded);
        Assert.True(result.ShouldLinkInstead);
        Assert.Contains("already exists", result.Reason);
    }

    /// <summary>
    /// The server answers refusals in two shapes and both carry the useful sentence.
    /// </summary>
    /// <remarks>
    /// Some branches answer <c>{ "message": ... }</c> and others <c>{ "errors": [...] }</c>.
    /// Reading only one means half of them arrive as a bare status code.
    /// </remarks>
    [Fact]
    public async Task A_validation_refusal_is_read_from_the_errors_array()
    {
        var result = await Build(StubHandler.Always(HttpStatusCode.BadRequest,
            """{"errors":["Display name is required."]}"""))
            .RegisterAsync("");

        Assert.False(result.Succeeded);
        Assert.Contains("Display name is required", result.Reason);
    }

    [Fact]
    public async Task A_successful_link_succeeds()
    {
        var result = await Build(StubHandler.Always(HttpStatusCode.OK,
            """{"message":"Microsoft account linked successfully."}"""))
            .LinkAsync("a@b.test", "pw");

        Assert.True(result.Succeeded);
    }

    /// <summary>
    /// The password is what proves the existing account is theirs.
    /// </summary>
    /// <remarks>
    /// A valid Microsoft token is not enough on its own, and the server says so plainly: without
    /// the password check anybody with any Microsoft account could attach themselves to any address
    /// they could name.
    /// </remarks>
    [Fact]
    public async Task A_wrong_password_refuses_the_link()
    {
        var result = await Build(StubHandler.Always(HttpStatusCode.Unauthorized,
            """{"status":401,"detail":"Failed"}"""))
            .LinkAsync("a@b.test", "wrong");

        Assert.False(result.Succeeded);
        Assert.False(result.ShouldLinkInstead);
        Assert.False(result.RequiresTwoFactor);
        Assert.Contains("don't match an account", result.Reason);
    }

    [Fact]
    public async Task An_identity_already_linked_elsewhere_says_so()
    {
        var result = await Build(StubHandler.Always(HttpStatusCode.Conflict,
            """{"message":"This Microsoft account is already linked to a different local account."}"""))
            .LinkAsync("a@b.test", "pw");

        Assert.False(result.Succeeded);
        Assert.Contains("already linked", result.Reason);
    }

    [Fact]
    public async Task An_unreachable_server_creates_nothing_and_says_so()
    {
        var result = await Build(new StubHandler().ThenUnreachable()).RegisterAsync("Ada");

        Assert.False(result.Succeeded);
        Assert.Contains("Nothing was created", result.Reason);
    }

    /// <summary>
    /// The four refusals a 401 can mean arrive in /login's shape and are told apart the same way.
    /// </summary>
    /// <remarks>
    /// This endpoint used to answer in a shape of its own, and this client mapped the same four
    /// refusals a second time. Now one word, one mapping, every door.
    /// </remarks>
    [Theory]
    [InlineData("RequiresTwoFactor", true,  "two-step")]
    [InlineData("NotAllowed",        false, "hasn't been confirmed")]
    [InlineData("LockedOut",         false, "locked")]
    [InlineData("Failed",            false, "don't match")]
    public async Task Each_link_refusal_is_named_for_what_it_is(string detail, bool needsCode, string fragment)
    {
        var result = await Build(StubHandler.Always(HttpStatusCode.Unauthorized,
            $$"""{"status":401,"detail":"{{detail}}"}"""))
            .LinkAsync("a@b.test", "pw");

        Assert.False(result.Succeeded);
        Assert.Equal(needsCode, result.RequiresTwoFactor);
        Assert.Contains(fragment, result.Reason!, StringComparison.OrdinalIgnoreCase);
    }
}
