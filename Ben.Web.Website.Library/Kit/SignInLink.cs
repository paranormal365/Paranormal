using Microsoft.AspNetCore.Components;

namespace Ben.Web.Website.Library.Kit;

/// <summary>The address of the sign-in page that brings a person back to the page they are on.</summary>
/// <remarks>
/// UI test pass 6.1 and 6.8, 2026-09-14. The header's Sign In and the pages that send a signed-out visitor to sign in went
/// to a bare <c>/login</c>, so signing in from a group's calendar landed on the home page. Three other links carried the
/// page's full address (<c>http://…</c>), which <c>Login.razor</c>'s open-redirect guard refuses — it takes only a path
/// starting with a single slash — so they landed on the home page too. This builds the one form the guard accepts.
/// </remarks>
public static class SignInLink
{
    /// <summary>Pages that are themselves about signing in, where coming back would be going round in a circle.</summary>
    private static readonly string[] NotReturnedTo = ["login", "logout", "signup", "sign-up", "forgot-password", "reset-password", "confirm-email"];

    public static string For(NavigationManager nav) => For(nav.ToBaseRelativePath(nav.Uri));

    /// <summary>
    /// Where a page sends somebody it will not show itself to: a signed-out visitor to sign in (and back), anybody else home.
    /// </summary>
    /// <remarks>
    /// The SuperAdmin pages sent everybody they refused to the home page — right for a member, wrong for the SuperAdmin whose
    /// session had simply ended, who then had to find the page again after signing in (client test pass, 2026-09-14).
    /// </remarks>
    public static string ForRefusal(NavigationManager nav, Ben.Web.Services.IBenUserState user)
        => user.IsAuthenticated ? "/" : For(nav);

    /// <summary>For a base-relative path such as <c>organizations/x/calendar?view=month</c>.</summary>
    public static string For(string relativePath)
    {
        var path = relativePath.TrimStart('/');
        var first = path.Split('/', '?', '#')[0];
        if (path.Length == 0 || NotReturnedTo.Contains(first, StringComparer.OrdinalIgnoreCase)) return "/login";
        return $"/login?returnUrl={Uri.EscapeDataString("/" + path)}";
    }
}
