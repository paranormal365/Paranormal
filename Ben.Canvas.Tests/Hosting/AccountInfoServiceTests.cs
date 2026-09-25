using System.Net;
using Ben.Canvas.Tests.Support;
using Ben.Wasm.Canvas.Services;

namespace Ben.Canvas.Tests.Hosting;

/// <summary>
/// Asking the API who is signed in.
/// </summary>
/// <remarks>
/// The editor has no claims to read, so /api/me is the only source of the email it shows as the
/// signed-in name. Every failure must forget that email rather than keep showing a stale one.
/// </remarks>
public sealed class AccountInfoServiceTests
{
    private const string Me = """{"userId":"00000000-0000-0000-0000-000000000001","email":"sarah.mitchell@benco.dev","isSuperAdmin":false,"isAdmin":false}""";

    private static async Task<TokenStore> SignedInAsync()
    {
        var tokens = new TokenStore(new NoJs());
        await tokens.SetAsync("at", "rt", 3600);
        return tokens;
    }

    [Fact]
    public async Task Me_keeps_the_webapi_base_path()
    {
        var handler = new StubHandler(HttpStatusCode.OK, Me);
        var service = new AccountInfoService(new SingleClientFactory(handler), await SignedInAsync(), "https://ishaunted.test/webapi");

        await service.IsAdministratorAsync();

        Assert.Equal("https://ishaunted.test/webapi/api/me", handler.LastUrl);
    }

    [Fact]
    public async Task The_email_from_me_is_remembered()
    {
        var service = new AccountInfoService(new SingleClientFactory(new StubHandler(HttpStatusCode.OK, Me)), await SignedInAsync(), "https://ishaunted.test/webapi");

        Assert.False(await service.IsAdministratorAsync());
        Assert.Equal("sarah.mitchell@benco.dev", service.Email);
    }

    [Fact]
    public async Task A_failed_me_call_forgets_the_email()
    {
        var handler = new StubHandler(HttpStatusCode.OK, Me).Then(HttpStatusCode.InternalServerError, "");
        var service = new AccountInfoService(new SingleClientFactory(handler), await SignedInAsync(), "https://ishaunted.test/webapi");

        await service.IsAdministratorAsync();
        await service.IsAdministratorAsync();

        Assert.Null(service.Email);
    }

    [Fact]
    public async Task No_token_means_no_call_and_no_email()
    {
        var handler = new StubHandler(HttpStatusCode.OK, Me);
        var service = new AccountInfoService(new SingleClientFactory(handler), new TokenStore(new NoJs()), "https://ishaunted.test/webapi");

        Assert.False(await service.IsAdministratorAsync());
        Assert.Empty(handler.Requests);
        Assert.Null(service.Email);
    }

    [Fact]
    public async Task An_administrator_is_recognised()
    {
        var service = new AccountInfoService(
            new SingleClientFactory(new StubHandler(HttpStatusCode.OK, """{"email":"a@b.test","isSuperAdmin":true,"isAdmin":false}""")),
            await SignedInAsync(), "https://ishaunted.test/webapi");

        Assert.True(await service.IsAdministratorAsync());
    }
}
