using System.Net;
using Ben.Data.Common;

namespace Ben.Data.WebApi.Services;

/// <summary>
/// The one branded shell every email the site sends is wrapped in.
/// </summary>
/// <remarks>
/// <para>Until 2026-08-31 the site's emails were bare <c>&lt;p&gt;</c> fragments — functional, and
/// looking exactly like the phishing they were asking people not to fall for. A confirmation email
/// asking somebody to click a link is the one message that most needs to look like it came from
/// the site it names.</para>
///
/// <para><b>Email HTML is written like it is 2003, on purpose.</b> Tables for layout, every style
/// inline, no external stylesheet, no flexbox: Outlook renders with Word's engine and Gmail strips
/// <c>&lt;style&gt;</c> blocks in enough contexts that anything else falls apart somewhere. The
/// logo is an absolute URL to the live site's PNG — SVG is stripped by most clients — with alt
/// text and a colored fallback cell, because plenty of clients block remote images until the
/// reader opts in, and the message must survive that too.</para>
///
/// <para><b>The button is repeated as a plain link underneath</b>, deliberately: a styled button
/// in a blocked-images, plain-text-preferring, or screen-reader context can vanish or be
/// unclickable, and the raw URL is also what lets a cautious reader see where they are being sent
/// before they go — which is exactly the caution the site should be encouraging.</para>
/// </remarks>
public static class BenEmailLayout
{
    /// <summary>Wraps body HTML in the branded shell.</summary>
    /// <param name="site">Supplies the name, tagline and absolute base URL for the logo.</param>
    /// <param name="title">The heading under the logo — "Confirm your email".</param>
    /// <param name="bodyHtml">Already-safe HTML. Caller escapes anything user-supplied.</param>
    /// <param name="buttonText">Optional action button; both or neither of these two.</param>
    /// <param name="buttonUrl">Where it goes. Repeated as a visible plain link below.</param>
    public static string Wrap(SiteIdentity site, string title, string bodyHtml,
                              string? buttonText = null, string? buttonUrl = null)
    {
        var logo = site.AbsoluteUrl("/icon-192.png");
        var name = WebUtility.HtmlEncode(site.Name);

        var button = "";
        if (buttonText is not null && buttonUrl is not null)
        {
            var url = WebUtility.HtmlEncode(buttonUrl);
            button = $"""
                <tr><td align="center" style="padding:8px 32px 4px 32px;">
                  <a href="{url}"
                     style="display:inline-block;background-color:#2e6b34;color:#ffffff;
                            font-family:Arial,Helvetica,sans-serif;font-size:16px;font-weight:bold;
                            text-decoration:none;padding:12px 32px;border-radius:6px;">
                    {WebUtility.HtmlEncode(buttonText)}</a>
                </td></tr>
                <tr><td align="center" style="padding:4px 32px 8px 32px;
                        font-family:Arial,Helvetica,sans-serif;font-size:12px;color:#6b7280;">
                  Or paste this into your browser:<br/>
                  <a href="{url}" style="color:#2e6b34;word-break:break-all;">{url}</a>
                </td></tr>
                """;
        }

        return $"""
            <!DOCTYPE html>
            <html>
            <head>
              <!-- Without this the reader's client guesses the charset, and a wrong guess turns
                   the em-dash in the footer into "â€"" — seen in the first preview render. -->
              <meta charset="utf-8" />
            </head>
            <body style="margin:0;padding:0;background-color:#f3f4f6;">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0"
                     style="background-color:#f3f4f6;padding:24px 0;">
                <tr><td align="center">
                  <table role="presentation" width="560" cellpadding="0" cellspacing="0"
                         style="max-width:560px;width:100%;background-color:#ffffff;
                                border-radius:8px;overflow:hidden;">
                    <tr><td align="center" style="background-color:#1f2428;padding:24px 32px 20px 32px;">
                      <img src="{logo}" width="72" height="72" alt="{name}"
                           style="display:block;border:0;" />
                      <div style="font-family:Arial,Helvetica,sans-serif;font-size:20px;
                                  font-weight:bold;color:#ffffff;padding-top:10px;">{name}</div>
                    </td></tr>
                    <tr><td style="padding:28px 32px 8px 32px;font-family:Arial,Helvetica,sans-serif;
                                   font-size:20px;font-weight:bold;color:#111827;">
                      {WebUtility.HtmlEncode(title)}
                    </td></tr>
                    <tr><td style="padding:0 32px 16px 32px;font-family:Arial,Helvetica,sans-serif;
                                   font-size:15px;line-height:1.6;color:#374151;">
                      {bodyHtml}
                    </td></tr>
                    {button}
                    <tr><td style="padding:20px 32px 24px 32px;border-top:1px solid #e5e7eb;
                                   font-family:Arial,Helvetica,sans-serif;font-size:12px;
                                   color:#9ca3af;">
                      {name} — {WebUtility.HtmlEncode(site.Tagline)}<br/>
                      If you weren't expecting this message, you can ignore it.
                    </td></tr>
                  </table>
                </td></tr>
              </table>
            </body>
            </html>
            """;
    }
}
