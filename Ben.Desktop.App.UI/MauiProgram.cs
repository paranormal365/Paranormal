using Ben.Data.WebApi.Client.Auth;
using Ben.Desktop.App.Library.External;
using Ben.Desktop.App.Library.Storage;
using Ben.Desktop.App.Library.ViewModels;
using Ben.Data.WebApi.Client.External;
using Ben.Web.Services.WebApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telerik.Maui.Controls.Compatibility;

namespace Ben.Desktop.App.UI;

/// <summary>
/// The composition root: one environment, one token session, one signed-in person.
/// </summary>
/// <remarks>
/// <para>Two HTTP clients, deliberately. The identity client must NOT carry a bearer token — it is
/// what refreshes the token, and a refresh that waits for a valid access token waits on itself.</para>
///
/// <para><c>ApiBasePathHandler</c> is in both pipelines because production is a sub-path
/// (<c>/webapi</c>) and <c>HttpClient.BaseAddress</c> silently drops a path for any request path
/// starting with a slash. The handler cannot be forgotten; remembering at every call site can.</para>
/// </remarks>
public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .UseTelerik()
            .ConfigureFonts(fonts => fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular"));

#if DEBUG
        builder.Logging.AddDebug();
#endif

        var environment = ApiEnvironment.Fallback;
        builder.Services.AddSingleton(environment);

        builder.Services.AddSingleton<ITokenStorage, SecureStorageTokenStorage>();

        // The identity client: no bearer handler, by design. See the class remarks.
        builder.Services.AddHttpClient<IWebApiIdentityClient, WebApiIdentityClient>(client =>
                client.BaseAddress = environment.BaseUrl)
            .AddHttpMessageHandler(() => new ApiBasePathHandler(environment.BaseUrl.ToString()));

        builder.Services.AddSingleton(sp => new TokenSession(
            sp.GetRequiredService<ITokenStorage>(),
            sp.GetRequiredService<IWebApiIdentityClient>()));

        builder.Services.AddSingleton<BearerTokenHandler>();

        // Everything else. Named rather than typed so AccountClient and any future client can
        // share one configured pipeline.
        builder.Services.AddHttpClient("api", client => client.BaseAddress = environment.BaseUrl)
            .AddHttpMessageHandler<BearerTokenHandler>()
            .AddHttpMessageHandler(() => new ApiBasePathHandler(environment.BaseUrl.ToString()));

        builder.Services.AddSingleton(sp =>
            new AccountClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient("api")));

        builder.Services.AddSingleton(sp =>
        {
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("api");
            return new SessionStore(
                sp.GetRequiredService<TokenSession>(),
                sp.GetRequiredService<IWebApiIdentityClient>(),
                // Roles never come from the token: Identity's are opaque, not JWTs. Ask the server.
                ct => ApiResponseMapper.ReadItemAsync<MeResponse>(
                    http, new HttpRequestMessage(HttpMethod.Get, "api/me"), ct));
        });

        // ── External sign-in ──────────────────────────────────────────────
        //
        // Microsoft leaves the client holding a token MICROSOFT issued, which our API validates
        // under a second scheme — so that session renews at Microsoft, not at our /refresh. Apple
        // is the opposite: its endpoint hands back our own Identity tokens, so an Apple sign-in is
        // an ordinary session with nothing special about it.
        builder.Services.AddSingleton(EntraSettings.FromConfiguration());
        builder.Services.AddSingleton<IInteractiveBrowser, WebAuthenticatorBrowser>();

        builder.Services.AddHttpClient("entra");

        builder.Services.AddSingleton(sp => new EntraTokenClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("entra"),
            sp.GetRequiredService<EntraOptions>()));

        builder.Services.AddSingleton(sp => new EntraSignInService(
            sp.GetRequiredService<EntraOptions>(),
            sp.GetRequiredService<EntraTokenClient>(),
            sp.GetRequiredService<IInteractiveBrowser>(),
            sp.GetRequiredService<TokenSession>(),
            sp.GetRequiredService<SessionStore>()));

        builder.Services.AddSingleton(sp =>
            new EntraAccountClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient("api")));

#if MACCATALYST
        builder.Services.AddSingleton<IAppleIdentityProvider, AppleIdentityProvider>();
#else
        builder.Services.AddSingleton<IAppleIdentityProvider, UnavailableAppleIdentityProvider>();
#endif

        builder.Services.AddSingleton(sp => new AppleSignInClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("api"),
            sp.GetRequiredService<SessionStore>()));

        builder.Services.AddSingleton<ExternalSignInViewModel>();

        builder.Services.AddSingleton<SessionViewModel>();
        builder.Services.AddSingleton<Pages.SignInPage>();
        builder.Services.AddSingleton<Pages.CompleteProfilePage>();
        builder.Services.AddSingleton<Pages.HomePage>();
        builder.Services.AddSingleton<AppShell>();

        return builder.Build();
    }
}
