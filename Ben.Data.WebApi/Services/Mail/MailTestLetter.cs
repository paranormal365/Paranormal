using System.Net;
using Ben.Data.Common;
using Ben.Data.Common.Mail;
using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Mail;

/// <summary>
/// The letter Outgoing Mail sends when somebody presses "Send test".
/// </summary>
/// <remarks>
/// <para>Ben, 10/13/2026: the test said it was sent, nothing arrived, and nothing appeared under
/// Recent letters. He asked for the test to be recorded there, to say plainly that it is a test,
/// and to let him pick one of the site's letters and have it filled in with made-up details.</para>
///
/// <para><b>Which words a picked letter uses.</b> The template somebody published, when there is
/// one, because that is what people receive. Otherwise the ready-made starter for that letter from
/// Email templates: the site writes the rest of its letters in code, one mailer at a time, and
/// there is no way to ask a mailer for its words without making it send something real.</para>
///
/// <para><b>Made-up details, never real ones.</b> The same invented rows the template preview
/// uses, so a test can never put another person's name, booking or working link in somebody's
/// inbox.</para>
/// </remarks>
public static class MailTestLetter
{
    /// <summary>What the outbox records a test as, with the letter's own key after it.</summary>
    public const string KindPrefix = "test";

    /// <summary>A test letter, ready to send.</summary>
    /// <param name="UsedPublished">Whether the words came from a template somebody published.</param>
    public sealed record Letter(string Subject, string Html, string Kind, bool UsedPublished);

    /// <summary>
    /// Writes the test letter: the plain one when <paramref name="kind"/> is null, otherwise that
    /// letter filled in with made-up details.
    /// </summary>
    public static async Task<Letter> ComposeAsync(
        BenDataContext db, SiteIdentity site, MailKindInfo? kind, DateTime nowUtc, CancellationToken ct)
    {
        var zone = SiteZone();

        if (kind is null)
        {
            var when = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone);
            var body = Banner(site, null)
                     + MailBlocks.Heading("Test message")
                     + MailBlocks.Paragraph(
                         $"If you can read this, email from {WebUtility.HtmlEncode(site.Name)} reaches this address.")
                     + MailBlocks.Paragraph(
                         $"Sent {when:MM/dd/yyyy h:mm tt} ({WebUtility.HtmlEncode(zone.IsDaylightSavingTime(when) ? zone.DaylightName : zone.StandardName)}).");

            return new Letter($"[Test] A test message from {site.Name}", body, KindPrefix, false);
        }

        var published = await db.EmailTemplates.AsNoTracking()
            .Where(t => t.Kind == kind.Key && t.PublishedUtc != null
                     && t.Subject != null && t.BodyHtml != null)
            .Select(t => new { t.Subject, t.BodyHtml })
            .FirstOrDefaultAsync(ct);

        var (subject, html) = published is not null
            ? (published.Subject!, published.BodyHtml!)
            : Starter(kind);

        var tables = MailTemplateSchema.For(kind, db);
        var context = new MailTokens.Context(
            MailSampleRows.For(tables), zone, nowUtc,
            site.Name, site.AbsoluteUrl("/"),
            MailSampleRows.SuppliedFor(kind, site));

        return new Letter(
            "[Test] " + MailTokens.Render(subject, context),
            UnderTheHeader(MailTokens.Render(html, context), Banner(site, kind)),
            $"{KindPrefix}-{kind.Key}",
            published is not null);
    }

    /// <summary>
    /// The ready-made letter for this kind: one written for it when there is one, otherwise any
    /// starter it can use, otherwise its title and description with every value it carries.
    /// </summary>
    internal static (string Subject, string Html) Starter(MailKindInfo kind)
    {
        var fits = MailStarters.For(kind);
        var starter = fits.FirstOrDefault(s => s.Suits.Contains(kind.Key)) ?? fits.FirstOrDefault();
        if (starter is not null) return (starter.Subject, starter.BodyHtml);

        // No starter fits, which happens for a letter that needs a value the plain starter lacks.
        // Every value the letter carries is shown, so the test still has its link or button in it.
        // No header here: the outbox puts the site's own on a body that has none.
        var supplied = kind.Supplied.Select(s => s.IsHtml ? "{" + s.Name + "}" : MailBlocks.Paragraph("{" + s.Name + "}"));
        return (kind.Title,
                string.Join("\n", [MailBlocks.Heading(WebUtility.HtmlEncode(kind.Title)),
                                   MailBlocks.Paragraph(WebUtility.HtmlEncode(kind.Description)),
                                   ..supplied]));
    }

    /// <summary>
    /// Puts the test box just under the letter's header, or at the very top of one without.
    /// </summary>
    /// <remarks>
    /// The header is the table holding the site's icon, so the box goes after the first table that
    /// closes past the icon. A letter without one is given the header by the outbox, outside the
    /// box, so there the box simply goes first.
    /// </remarks>
    internal static string UnderTheHeader(string html, string banner)
    {
        var icon = html.IndexOf(MailHeader.IconFileName, StringComparison.OrdinalIgnoreCase);
        if (icon < 0) return banner + html;

        var close = html.IndexOf("</table>", icon, StringComparison.OrdinalIgnoreCase);
        if (close < 0) return banner + html;

        var at = close + "</table>".Length;
        return html[..at] + "\n" + banner + html[at..];
    }

    /// <summary>The yellow box at the top that says this is a test.</summary>
    internal static string Banner(SiteIdentity site, MailKindInfo? kind)
    {
        var what = kind is null
            ? ""
            : $" It shows the “{WebUtility.HtmlEncode(kind.Title)}” letter filled in with made-up "
            + "names, places and links, so nothing in it is real and its buttons won't work.";

        return $"""
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="margin:0 0 16px 0;" data-mail-test="true">
            <tr><td style="padding:10px 14px;background:#fff7d6;border:1px solid #e0b400;border-radius:6px;font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:1.45;color:#4a3b00;">
            <strong>This is a test.</strong> It was sent from the Outgoing Mail page on {WebUtility.HtmlEncode(site.Name)} to check that email gets through.{what}
            </td></tr>
            </table>

            """;
    }

    private static TimeZoneInfo SiteZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(Ben.Data.Common.Constants.HouseClock.ZoneId); }
        catch (Exception) { return TimeZoneInfo.Utc; }
    }
}
