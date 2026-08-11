namespace Ben.Web.Library.Services;

/// <summary>
/// Consolidates the two-line prerequisite every authenticated page's first-render guard needs,
/// independent of what each page does once auth state is known (redirect to /login, redirect
/// elsewhere, show an inline message — that varies by page and stays in each page's own code).
/// </summary>
public static class AuthGuardExtensions
{
    /// <summary>
    /// Awaits <see cref="IBenUserState.AuthReady"/>, but only once this circuit is actually
    /// interactive — never during static SSR prerendering, when <c>AuthReady</c> can't yet have
    /// been signalled and awaiting it would hang the component's lifecycle method forever.
    /// Returns <c>false</c> during prerender so the caller can bail out immediately with
    /// <c>if (!await UserState.WaitUntilAuthReadyAsync(RendererInfo.IsInteractive)) return;</c> —
    /// the same "interactive check, then await" ordering every authenticated page needs, now in
    /// one place instead of independently re-typed on every page.
    /// </summary>
    public static async Task<bool> WaitUntilAuthReadyAsync(this IBenUserState userState, bool isInteractive)
    {
        if (!isInteractive) return false;
        await userState.AuthReady;
        return true;
    }
}
