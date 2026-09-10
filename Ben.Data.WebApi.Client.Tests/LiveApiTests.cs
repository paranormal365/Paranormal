using System.Net.Http.Headers;
using System.Net.Http.Json;
using Ben.Data.WebApi.Client.Auth;
using Ben.Data.WebApi.Client.External;
using Ben.Web.Services.WebApi;

namespace Ben.Data.WebApi.Client.Tests;

/// <summary>
/// The client against a real Ben.Data.WebApi. Stubbed tests prove the client behaves as the
/// fixtures say; these prove the fixtures are still what the server sends — the half that rots.
/// </summary>
/// <remarks>
/// <para><b>They skip when no API is listening</b>, and skipping is the correct resting state.
/// Point them at a scratch database, never the shared one:</para>
/// <code>
/// BEN_LIVE_API=http://127.0.0.1:5252 BEN_LIVE_EMAIL=... BEN_LIVE_PASSWORD=... dotnet test
/// </code>
/// </remarks>
public sealed class LiveApiTests
{
    private static readonly string? BaseUrl = Environment.GetEnvironmentVariable("BEN_LIVE_API");
    private static readonly string? Email = Environment.GetEnvironmentVariable("BEN_LIVE_EMAIL");
    private static readonly string? Password = Environment.GetEnvironmentVariable("BEN_LIVE_PASSWORD");

    private static bool Configured =>
        !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(Email) && !string.IsNullOrWhiteSpace(Password);

    private static HttpClient Http() => new() { BaseAddress = new Uri(BaseUrl!.TrimEnd('/') + "/") };

    private sealed class RecordingAdopter : IExternalSignInAdopter
    {
        public WebApiTokenResponse? Adopted { get; private set; }
        public Task AdoptExternalSignInAsync(WebApiTokenResponse response, CancellationToken token = default)
        { Adopted = response; return Task.CompletedTask; }
    }

    [SkippableFact]
    public async Task A_real_sign_in_yields_a_token_and_real_roles()
    {
        Skip.IfNot(Configured, "No live API configured. Set BEN_LIVE_API, BEN_LIVE_EMAIL, BEN_LIVE_PASSWORD.");

        var attempt = await new WebApiIdentityClient(Http()).TryLoginAsync(Email!, Password!);
        Skip.If(attempt.RequiresTwoFactor, "That account has two-factor turned on; this test needs one that does not.");
        Assert.NotNull(attempt.Token);

        using var req = new HttpRequestMessage(HttpMethod.Get, "api/me");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", attempt.Token!.AccessToken);
        var me = await ApiResponseMapper.ReadItemAsync<MeResponse>(Http(), req);

        Assert.False(me.Failed);
        Assert.Equal(Email, me.Item!.Email, ignoreCase: true);
    }

    /// <summary>The refresh token is data-protected by the server's key ring; nothing about that is stubbable.</summary>
    [SkippableFact]
    public async Task A_real_refresh_produces_a_working_token()
    {
        Skip.IfNot(Configured, "No live API configured.");
        var identity = new WebApiIdentityClient(Http());

        var attempt = await identity.TryLoginAsync(Email!, Password!);
        Skip.If(attempt.RequiresTwoFactor, "That account has two-factor turned on.");
        Assert.NotNull(attempt.Token?.RefreshToken);

        var refreshed = await identity.RefreshAsync(attempt.Token!.RefreshToken!);

        Assert.NotNull(refreshed);
        Assert.False(string.IsNullOrWhiteSpace(refreshed!.AccessToken));
    }

    /// <summary>The drift detector: if Identity ever changes the word, every stubbed test keeps passing.</summary>
    [SkippableFact]
    public async Task A_wrong_password_still_maps_to_invalid_credentials()
    {
        Skip.IfNot(Configured, "No live API configured.");

        var attempt = await new WebApiIdentityClient(Http()).TryLoginAsync(Email!, "definitely-not-the-password");

        Assert.Null(attempt.Token);
        Assert.Equal(LoginFailure.InvalidCredentials, LoginFailureMapping.From(attempt));
    }

    [SkippableFact]
    public async Task Handle_availability_answers_from_the_real_endpoint()
    {
        Skip.IfNot(Configured, "No live API configured.");

        var result = await Http().GetFromJsonAsync<HandleAvailabilityResponse>(
            $"api/account/handle-available?handle={$"zzz_live_{Guid.NewGuid():N}"[..24]}");

        Assert.NotNull(result);
        Assert.True(result!.Available);
    }

    /// <summary>
    /// A token Apple did not sign is refused with a sentence. Proves the endpoint is reachable and
    /// that an Apple audience is configured at all (an unconfigured one answers 503, not 401).
    /// </summary>
    [SkippableFact]
    public async Task Apple_refuses_a_token_it_cannot_verify()
    {
        Skip.IfNot(Configured, "No live API configured.");
        var adopter = new RecordingAdopter();

        var outcome = await new AppleSignInClient(Http(), adopter).SignInAsync("not.a.real.token");

        Assert.False(outcome.Succeeded);
        Assert.False(outcome.RequiresProfile);
        Assert.DoesNotContain("isn't switched on", outcome.Reason);
        Assert.Null(adopter.Adopted);
    }

    /// <summary>The Apple link door exists, and a forged token gets nowhere near the password check.</summary>
    [SkippableFact]
    public async Task Apple_linking_refuses_a_token_it_cannot_verify()
    {
        Skip.IfNot(Configured, "No live API configured.");
        var adopter = new RecordingAdopter();

        var outcome = await new AppleSignInClient(Http(), adopter).LinkAsync("not.a.real.token", Email!, Password!);

        Assert.False(outcome.Succeeded);
        Assert.DoesNotContain("404", outcome.Reason);
        Assert.Null(adopter.Adopted);
    }

    /// <summary>Both Microsoft doors refuse a caller holding no Microsoft token at all.</summary>
    [SkippableFact]
    public async Task The_microsoft_endpoints_refuse_an_unauthenticated_caller()
    {
        Skip.IfNot(Configured, "No live API configured.");
        var http = Http();

        using var register = await http.PostAsJsonAsync("api/auth/entra/register", new { displayName = "Nobody At All" });
        using var link = await http.PostAsJsonAsync("api/auth/entra/link", new { email = "nobody@example.test", password = "whatever" });

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, register.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, link.StatusCode);
    }
}
