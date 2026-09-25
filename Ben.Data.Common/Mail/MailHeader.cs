using System.Net;

namespace Ben.Data.Common.Mail;

/// <summary>
/// The one header every email the site sends carries, and the rule for whether a body has it yet.
/// </summary>
/// <remarks>
/// <para><b>The design is Ben's, taken from the templates he wrote</b> (2026-09-20). All sixteen
/// published templates open with a byte-identical block: the wordmark on the left at 35px, and the
/// dark app icon on the right at 64x64 with rounded corners. This is that block, so a letter
/// written in code and a letter written in the template editor open the same way.</para>
///
/// <para><b>The wordmark is literal markup, not a token, and that is deliberate.</b> "Is" at 70%
/// opacity, "Haunted" in bold, ".com" at 40% cannot come from a <c>{SiteName}</c> substitution -
/// the three weights are the design. Ben asked for it kept as written (2026-09-20). The
/// consequence to know: renaming the site would not change the wordmark here.</para>
///
/// <para><b>Email HTML is written like it is 2003, on purpose</b> - tables, inline styles, absolute
/// image URLs. Outlook renders with Word's engine and Gmail strips <c>&lt;style&gt;</c> blocks, so
/// anything modern falls apart somewhere. The icon carries alt text because many clients block
/// remote images until the reader opts in, and the letter has to survive that too.</para>
/// </remarks>
public static class MailHeader
{
    /// <summary>
    /// The file name of the icon in the header. Also the marker <see cref="IsAlreadyHeaded"/> looks
    /// for, which is why it is a constant rather than three spellings in three places.
    /// </summary>
    public const string IconFileName = "apple-touch-icon.png";

    /// <summary>
    /// The header block, with <paramref name="iconUrl"/> already absolute.
    /// </summary>
    /// <param name="iconUrl">Absolute URL of <see cref="IconFileName"/>. A relative one works in
    /// almost no mail client, which is why the caller resolves it rather than this.</param>
    /// <param name="siteName">Alt text for the icon, for a reader whose client blocks images.</param>
    public static string Html(string iconUrl, string siteName) =>
        $"""
        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="margin:0 0 16px 0;">
        <tr>
        <td valign="top" style="font-family:Arial,Helvetica,sans-serif;font-size:35px;line-height:1.6; color:#374151;padding:0 12px 0 0;"><em style="opacity:70%;">Is</em><strong>Haunted</strong><span style="opacity:40%;">.com</span></td>
        <td valign="top" width="80" align="right" style="width:80px;padding:0;"><img src="{WebUtility.HtmlEncode(iconUrl)}" width="64" height="64" alt="{WebUtility.HtmlEncode(siteName)}" style="display:block;border:0;border-radius:10px;" /></td>
        </tr>
        </table>
        """;

    /// <summary>
    /// Whether this body already opens with a header, and so must not be given another.
    /// </summary>
    /// <remarks>
    /// <para>Two ways a body can already have one, and both have to be recognised or somebody gets
    /// two headers stacked on top of each other:</para>
    /// <list type="bullet">
    ///   <item>A template written in the editor pastes the block in as markup.</item>
    ///   <item>A letter built in code was wrapped in the branded shell before being queued.</item>
    /// </list>
    /// <para>The icon's file name is the marker for both, because both reference the same file and
    /// nothing else in an email body does. Matching on the wordmark instead would miss a template
    /// an author edited the text of; matching on <c>&lt;table&gt;</c> would match half the letters
    /// that exist.</para>
    /// </remarks>
    public static bool IsAlreadyHeaded(string? bodyHtml) =>
        bodyHtml is not null
        && bodyHtml.Contains(IconFileName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether this body is a whole HTML document rather than a fragment.
    /// </summary>
    /// <remarks>
    /// A document has its own <c>&lt;head&gt;</c> and charset and cannot be dropped inside another
    /// one - wrapping it would produce a document nested in a document, which clients render in
    /// entertainingly different ways.
    /// </remarks>
    public static bool IsWholeDocument(string? bodyHtml) =>
        bodyHtml is not null
        && (bodyHtml.TrimStart().StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase)
            || bodyHtml.Contains("<html", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The small footnote for a letter whose times are not in the reader's own clock.
    /// </summary>
    /// <param name="zoneLabel">What the times in the letter are labelled with - "UTC", "CDT". Null
    /// or empty when the times are already the reader's own, which needs no note.</param>
    /// <remarks>
    /// Ben, 2026-09-20: say so when the letter only reports UTC. It matters because the site cannot
    /// know a mail reader's timezone the way a browser can, so an event with no zone of its own is
    /// written out in UTC - and a time with no zone beside it is read as local by everybody, which
    /// is how somebody arrives hours late. UTC gets the plain warning; a real zone gets a quieter
    /// note naming it, because at least it says which clock it means.
    /// </remarks>
    public static string? TimeZoneNote(string? zoneLabel)
    {
        if (string.IsNullOrWhiteSpace(zoneLabel)) return null;

        return zoneLabel.Trim().Equals("UTC", StringComparison.OrdinalIgnoreCase)
            ? "Times above are UTC, not your local time."
            : $"Times above are {WebUtility.HtmlEncode(zoneLabel.Trim())}, which may not be your local time.";
    }
}
