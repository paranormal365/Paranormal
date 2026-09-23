namespace Ben.Data.Common;

/// <summary>
/// What this site is called, and where it lives.
/// </summary>
/// <remarks>
/// <para>Ben, 2026-08-17: <i>"Right now, it is IsHaunted.com. I assume it will be that when we are
/// ready to buy the site, but it may not be available then. This is why I do not specify the base
/// URL name when talking about the website."</i></para>
///
/// <para>He is right to be wary. The name was hardcoded in the footer, the home page's title, the
/// invite page, and three email bodies — so renaming would have meant finding every one of them, and
/// the ones that get missed are always the emails, because nobody rereads an email template until a
/// customer forwards it back.</para>
///
/// <para>One place now. <see cref="Name"/> is what people read; <see cref="BaseUrl"/> is what goes
/// into a link somebody shares and into the link previews that carry it. Both are configuration, so
/// the day the domain is decided is a settings change rather than an archaeology exercise.</para>
/// </remarks>
public sealed class SiteIdentity
{
    /// <summary>
    /// The name shown to people — in the footer, in emails, and as the site name on a shared link.
    /// </summary>
    public string Name { get; set; } = "IsHaunted.com";

    /// <summary>
    /// The public origin, no trailing slash — <c>https://ishaunted.com</c>.
    /// </summary>
    /// <remarks>
    /// Needed because a link preview has to carry an absolute URL: the crawler that renders a card
    /// in a chat window is not on our domain and cannot resolve a relative one.
    /// </remarks>
    public string BaseUrl { get; set; } = "";

    /// <summary>One line saying what the site is, for a link preview with nothing better to show.</summary>
    public string Tagline { get; set; } = "Find paranormal investigators near you.";

    /// <summary>
    /// The API's own public origin, no trailing slash — <c>https://ishaunted.com/webapi</c>.
    /// </summary>
    /// <remarks>
    /// For the few links in a letter that the API itself answers — an entry pass drawn as a PNG.
    /// The site origin cannot serve those: the website does not forward <c>/api</c>, and the API
    /// lives under <c>/webapi</c>. Until 2026-09-23 the pass link was built on the site origin
    /// and could not have opened.
    /// </remarks>
    public string ApiBaseUrl { get; set; } = "";

    /// <summary>An absolute URL for a path, or the path unchanged when no origin is configured.</summary>
    public string AbsoluteUrl(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(BaseUrl)) return relativePath;
        return $"{BaseUrl.TrimEnd('/')}/{relativePath.TrimStart('/')}";
    }

    /// <summary>
    /// An absolute URL for a path the API answers, or the path unchanged when the API's origin is
    /// not configured — never the site origin, which would be a link that cannot open.
    /// </summary>
    public string ApiAbsoluteUrl(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(ApiBaseUrl)) return relativePath;
        return $"{ApiBaseUrl.TrimEnd('/')}/{relativePath.TrimStart('/')}";
    }

    /// <summary>
    /// Fills an unset <see cref="BaseUrl"/> from the older <c>AppBaseUrl</c> setting.
    /// </summary>
    /// <remarks>
    /// Both deploy paths set the API's <c>AppBaseUrl</c> and, until 2026-09-23, never its
    /// <c>SiteIdentity:BaseUrl</c> — so every link the API built with <see cref="AbsoluteUrl"/>
    /// (the seat pick, the /attending and /helping links, reset and handover, the logo in every
    /// letter) was relative, which a mail client cannot open. The confirmation letter alone
    /// worked, because it fell back to AppBaseUrl by hand. The fallback now lives here, once.
    /// </remarks>
    public static void UseAppBaseUrlWhenUnset(SiteIdentity site, string? appBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(site.BaseUrl) && !string.IsNullOrWhiteSpace(appBaseUrl))
            site.BaseUrl = appBaseUrl.TrimEnd('/');
    }
}
