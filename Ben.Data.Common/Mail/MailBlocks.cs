namespace Ben.Data.Common.Mail;

/// <summary>
/// Pieces of a letter, written the way email HTML has to be written (item 246).
/// </summary>
/// <remarks>
/// <para><b>Starting points, not a document model.</b> Each of these returns a lump of HTML an
/// author drops into the body and then edits. The alternative — a structured layout the editor
/// owns — would be tidier right up to the first letter somebody wanted laid out a way it did not
/// allow, and Ben's ask was explicit: <i>"It just needs to be flexible."</i></para>
///
/// <para><b>Written like it is 2003, on purpose</b>, the same rule <c>BenEmailLayout</c> already
/// follows. Tables for layout, every style written onto the element, fixed pixel widths, no
/// stylesheet and no flexbox: Outlook draws mail with Word's engine and Gmail strips
/// <c>&lt;style&gt;</c> blocks in enough places that anything else falls apart somewhere. A card
/// copied from the site's own CSS would look right in the preview and wrong in the inbox, which
/// is the worst of both.</para>
///
/// <para><b>Two columns are a table row, not a float.</b> Outlook ignores <c>float</c> and
/// <c>display:inline-block</c> on block elements often enough that the only thing that survives
/// everywhere is two <c>&lt;td&gt;</c>s — which is why "logo on one side, details on the other" is
/// offered as a block at all rather than left to the author.</para>
/// </remarks>
public static class MailBlocks
{
    /// <summary>The palette, matching <c>BenEmailLayout</c> so a block does not look bolted on.</summary>
    private const string Ink = "#111827";
    private const string Muted = "#6b7280";
    private const string Line = "#e5e7eb";
    private const string Green = "#2e6b34";
    private const string Paper = "#ffffff";
    private const string Font = "Arial,Helvetica,sans-serif";

    /// <summary>One thing an author can drop into a letter.</summary>
    public sealed record Block(string Key, string Title, string What, string Html);

    /// <summary>A heading, the size a letter's own section headings are.</summary>
    public static string Heading(string text = "A heading")
        => $"""
            <div style="font-family:{Font};font-size:18px;font-weight:bold;color:{Ink};
                        padding:16px 0 8px 0;">{text}</div>
            """;

    /// <summary>Ordinary words.</summary>
    public static string Paragraph(string text = "Something worth saying.")
        => $"""
            <div style="font-family:{Font};font-size:15px;line-height:1.6;color:#374151;
                        padding:0 0 12px 0;">{text}</div>
            """;

    /// <summary>
    /// A button.
    /// </summary>
    /// <remarks>
    /// An anchor styled as a block, not a <c>&lt;button&gt;</c>: a real button in an email does
    /// nothing, because there is no form and no script to run.
    /// </remarks>
    public static string Button(string text = "Open it", string url = "{SiteUrl}")
        => $"""
            <table role="presentation" cellpadding="0" cellspacing="0" border="0"
                   style="margin:8px 0 16px 0;">
              <tr><td align="center" bgcolor="{Green}" style="border-radius:6px;">
                <a href="{url}"
                   style="display:inline-block;background-color:{Green};color:#ffffff;
                          font-family:{Font};font-size:16px;font-weight:bold;
                          text-decoration:none;padding:12px 28px;border-radius:6px;">{text}</a>
              </td></tr>
            </table>
            """;

    /// <summary>A bordered box, the way the site's cards read.</summary>
    public static string Card(string title = "A card", string body = "What the card is about.")
        => $"""
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0"
                   style="border:1px solid {Line};border-radius:8px;background-color:{Paper};
                          margin:0 0 16px 0;">
              <tr><td style="padding:16px 18px;">
                <div style="font-family:{Font};font-size:16px;font-weight:bold;color:{Ink};
                            padding-bottom:6px;">{title}</div>
                <div style="font-family:{Font};font-size:15px;line-height:1.6;color:#374151;">
                  {body}
                </div>
              </td></tr>
            </table>
            """;

    /// <summary>
    /// Two columns: something on one side, something on the other.
    /// </summary>
    /// <param name="logoOnTheRight">
    /// Which side the narrow column sits on. Ben's example was the logo on the right and the
    /// person's details on the left, which is the opposite of the usual arrangement — so it is a
    /// choice rather than a fixed layout.
    /// </param>
    public static string TwoColumns(
        string wide = "{AppUsers.DisplayName}<br/>{AppUsers.Email}",
        string narrow = """<img src="{SiteUrl}icon-192.png" width="64" height="64" alt="{SiteName}" style="display:block;border:0;" />""",
        bool logoOnTheRight = true)
    {
        var wideCell = $"""
              <td valign="top" style="font-family:{Font};font-size:15px;line-height:1.6;
                                      color:#374151;padding:0 12px 0 0;">{wide}</td>
            """;
        var narrowCell = $"""
              <td valign="top" width="80" align="{(logoOnTheRight ? "right" : "left")}"
                  style="width:80px;padding:0;">{narrow}</td>
            """;

        var cells = logoOnTheRight ? wideCell + narrowCell : narrowCell + wideCell;

        return $"""
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0"
                   style="margin:0 0 16px 0;">
              <tr>
            {cells}  </tr>
            </table>
            """;
    }

    /// <summary>A line across, for separating one part from the next.</summary>
    public static string Divider()
        => $"""<div style="border-top:1px solid {Line};line-height:1px;height:1px;margin:16px 0;">&nbsp;</div>""";

    /// <summary>
    /// A table of things and what they cost, with a total.
    /// </summary>
    /// <remarks>
    /// The one layout a receipt or an invoice cannot do without, and the one an author is least
    /// likely to get right by hand: the figures have to be right-aligned against a column edge
    /// that survives a client which ignores <c>text-align</c> on a div.
    /// </remarks>
    public static string LineItems()
        => $"""
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0"
                   style="margin:0 0 16px 0;font-family:{Font};font-size:15px;color:#374151;">
              <tr>
                <td style="padding:8px 0;border-bottom:1px solid {Line};">What</td>
                <td align="right" style="padding:8px 0;border-bottom:1px solid {Line};">Amount</td>
              </tr>
              <tr>
                <td style="padding:8px 0;border-bottom:1px solid {Line};">One event</td>
                <td align="right" style="padding:8px 0;border-bottom:1px solid {Line};">$99.00</td>
              </tr>
              <tr>
                <td style="padding:10px 0;font-weight:bold;color:{Ink};">Total</td>
                <td align="right" style="padding:10px 0;font-weight:bold;color:{Ink};">$99.00</td>
              </tr>
            </table>
            """;

    /// <summary>Small print at the end.</summary>
    public static string Footer()
        => $$"""
            <div style="font-family:{{Font}};font-size:12px;line-height:1.5;color:{{Muted}};
                        border-top:1px solid {{Line}};padding:16px 0 0 0;margin-top:16px;">
              Sent by {SiteName} on {FullDate}.
            </div>
            """;

    /// <summary>Everything an author can insert, in the order it is offered.</summary>
    public static IReadOnlyList<Block> All =>
    [
        new("heading",   "Heading",      "A section heading.",                         Heading()),
        new("paragraph", "Paragraph",    "Ordinary words.",                            Paragraph()),
        new("card",      "Card",         "A bordered box, like the site's cards.",     Card()),
        new("two-right", "Logo right",   "Details on the left, the logo on the right.", TwoColumns(logoOnTheRight: true)),
        new("two-left",  "Logo left",    "The logo on the left, details on the right.", TwoColumns(logoOnTheRight: false)),
        new("button",    "Button",       "Something to click.",                        Button()),
        new("items",     "Items and total", "For a receipt or an invoice.",            LineItems()),
        new("divider",   "Divider",      "A line across.",                             Divider()),
        new("footer",    "Footer",       "Small print at the end.",                    Footer()),
    ];

    /// <summary>The block with this key, or null.</summary>
    public static Block? Find(string? key)
        => key is null ? null : All.FirstOrDefault(b => b.Key == key);
}
