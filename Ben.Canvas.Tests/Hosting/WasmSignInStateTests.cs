using System.Net;
using Ben.Canvas.Tests.Support;
using Ben.Wasm.Canvas.Services;

namespace Ben.Canvas.Tests.Hosting;

/// <summary>
/// What the editor is told about the signed-in person.
/// </summary>
/// <remarks>
/// The API returns no display name, so the email /api/me gave is what the editor shows. Signing out
/// must clear it at once, not on the next /api/me call.
/// </remarks>
public sealed class WasmSignInStateTests
{
    private const string Me = """{"email":"sarah.mitchell@benco.dev","isSuperAdmin":false,"isAdmin":false}""";

    [Fact]
    public void Signed_out_has_no_display_name()
    {
        var tokens = new TokenStore(new NoJs());
        var state = new WasmSignInState(tokens, new AccountInfoService(new SingleClientFactory(new StubHandler(HttpStatusCode.OK, Me)), tokens, "https://x.test/webapi"));

        Assert.False(state.IsSignedIn);
        Assert.Null(state.DisplayName);
    }

    [Fact]
    public async Task Signed_in_shows_the_email_me_returned()
    {
        var tokens = new TokenStore(new NoJs());
        await tokens.SetAsync("at", "rt", 3600);
        var account = new AccountInfoService(new SingleClientFactory(new StubHandler(HttpStatusCode.OK, Me)), tokens, "https://x.test/webapi");
        await account.IsAdministratorAsync();

        var state = new WasmSignInState(tokens, account);

        Assert.True(state.IsSignedIn);
        Assert.Equal("sarah.mitchell@benco.dev", state.DisplayName);
    }

    [Fact]
    public async Task Signing_out_clears_the_display_name()
    {
        var tokens = new TokenStore(new NoJs());
        await tokens.SetAsync("at", "rt", 3600);
        var account = new AccountInfoService(new SingleClientFactory(new StubHandler(HttpStatusCode.OK, Me)), tokens, "https://x.test/webapi");
        await account.IsAdministratorAsync();
        var state = new WasmSignInState(tokens, account);

        await tokens.ClearAsync();

        Assert.False(state.IsSignedIn);
        Assert.Null(state.DisplayName);
    }

    /// <summary>
    /// After a reload the sign-in is read from the tab's storage on first use. Anything drawn before that (the
    /// header chip said "Sign in" to a signed-in person) must hear that the answer changed.
    /// </summary>
    [Fact]
    public async Task A_sign_in_found_in_the_tabs_storage_is_announced_once()
    {
        var stored = System.Text.Json.JsonSerializer.Serialize(new { AccessToken = "at", RefreshToken = "rt", ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1) });
        var tokens = new TokenStore(new RecordingJs().Returns("sessionStorage.getItem", stored));
        var state = new WasmSignInState(tokens, new AccountInfoService(new SingleClientFactory(new StubHandler(HttpStatusCode.OK, Me)), tokens, "https://x.test/webapi"));
        var changes = 0;
        state.Changed += () => changes++;

        Assert.False(state.IsSignedIn);
        await tokens.GetAccessTokenAsync();
        await tokens.GetAccessTokenAsync();

        Assert.True(state.IsSignedIn);
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task Nothing_in_the_tabs_storage_announces_nothing()
    {
        var tokens = new TokenStore(new NoJs());
        var changes = 0;
        tokens.Changed += () => changes++;

        await tokens.GetAccessTokenAsync();

        Assert.Equal(0, changes);
    }
}
