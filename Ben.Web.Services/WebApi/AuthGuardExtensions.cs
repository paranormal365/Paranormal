namespace Ben.Web.Services.WebApi;

/// <summary>
/// <see cref="IWebApiTokenStore"/> counterpart to
/// <c>Ben.Web.Services.AuthGuardExtensions</c> — same "interactive check, then await
/// AuthReady" ordering, for the handful of WebApp-only pages that inject
/// <see cref="IWebApiTokenStore"/> directly instead of the narrower <c>IBenUserState</c>. Kept
/// separate rather than merged into the Library version since <c>Ben.Web.Library</c> has no
/// project reference to <c>Ben.Web.WebApp</c> and can't see <see cref="IWebApiTokenStore"/>.
/// </summary>
public static class AuthGuardExtensions
{
    public static async Task<bool> WaitUntilAuthReadyAsync(this IWebApiTokenStore tokenStore, bool isInteractive)
    {
        if (!isInteractive) return false;
        await tokenStore.AuthReady;
        return true;
    }
}
