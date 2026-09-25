using System.Security.Cryptography;
using Ben.Web.Services;
using Microsoft.AspNetCore.WebUtilities;

namespace Ben.Web.Website.Services;

/// <summary>
/// The browser's cart cookie, <c>ben.cart</c> (storefront S3.4): read on every server-rendered
/// page, made on the first one while the store is open.
/// </summary>
/// <remarks>
/// <para><b>HttpOnly</b>, so no script on the page can read it; <b>SameSite=Lax</b>, so another
/// site cannot spend it; <b>Secure</b> whenever the request itself came over HTTPS (the
/// SameAsRequest rule the rest of the site's cookies follow — development is plain http); a year
/// long, because a cart is worth keeping between visits.</para>
///
/// <para><b>Made only for pages, and only with the store open.</b> Pictures, scripts and the
/// circuit's own connection never need one, and a visitor to a site whose shop is still dark gets
/// no cookie at all. A malformed value — anything but 43 URL-safe characters — is replaced.</para>
///
/// <para>The API keeps only the token's hash; this side keeps the token, because it has to send it.</para>
/// </remarks>
public static class StoreCartCookie
{
    public const string Name = "ben.cart";

    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(365);

    /// <summary>Reads or makes the cookie and hands the token and the visitor's address to the holders.</summary>
    public static void Apply(HttpContext context, StoreCartTokenHolder cart, VisitorAddressHolder visitor, bool storeIsOn)
    {
        visitor.Address = context.Connection.RemoteIpAddress?.ToString();

        var existing = context.Request.Cookies[Name];
        if (IsWellFormed(existing))
        {
            cart.Token = existing;
            return;
        }
        if (!storeIsOn || !IsAPage(context.Request)) return;

        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        context.Response.Cookies.Append(Name, token, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = context.Request.IsHttps,
            IsEssential = true,
            Path = "/",
            Expires = DateTimeOffset.UtcNow.Add(Lifetime),
        });
        cart.Token = token;
    }

    public static bool IsWellFormed(string? token)
        => token is { Length: Ben.Service.Models.Store.StoreCartRules.TokenLength }
           && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    /// <summary>A document request — not a file, a script or the circuit's own traffic.</summary>
    internal static bool IsAPage(HttpRequest request)
    {
        if (!HttpMethods.IsGet(request.Method)) return false;
        var path = request.Path.Value ?? "/";
        if (path.StartsWith("/_", StringComparison.Ordinal)                    // _blazor, _framework, _content
            || path.StartsWith("/media/", StringComparison.OrdinalIgnoreCase)
            || Path.HasExtension(path)) return false;
        var accept = request.Headers.Accept.ToString();
        return accept.Length == 0 || accept.Contains("text/html", StringComparison.OrdinalIgnoreCase);
    }
}
