namespace Ben.Data.Common.Mail;

/// <summary>
/// The site's Signal colors, for email, which cannot read the site's stylesheet.
/// </summary>
/// <remarks>
/// <para>Ben, 10/02/2026: <i>"Make sure the generated emails are designed to look like the site or
/// with the site in mind for styling."</i> Until then every letter was the green of the old theme
/// (<c>#2e6b34</c>) on Tailwind grays, written into five files separately, a week after the site
/// itself had become violet and cyan.</para>
///
/// <para>These are the LIGHT values of <c>themes/signal-tokens.css</c>, copied by hand because a
/// letter is rendered by somebody else's program. Light only, and the letter says so with
/// <c>color-scheme: light</c>: a mail client that inverts a letter for dark mode does it badly and
/// differently in each client, and a letter that stays light everywhere at least reads everywhere.</para>
///
/// <para>The gradient is a bonus, never the only color: Outlook draws mail with Word's engine,
/// which ignores <c>background-image</c>, so everything that carries <see cref="Gradient"/> also
/// carries <see cref="Accent"/> as its plain background. In Outlook a button is violet; elsewhere
/// it is the site's violet-to-cyan.</para>
/// </remarks>
public static class MailPalette
{
    /// <summary>Headings and the strongest words — the site's ink.</summary>
    public const string Ink = "#0E1726";

    /// <summary>Body text. A step lighter than ink, as the site's long text reads.</summary>
    public const string Body = "#2B3546";

    /// <summary>Secondary text — the site's ink-muted.</summary>
    public const string Muted = "#5A6679";

    /// <summary>Small print — the site's ink-faint.</summary>
    public const string Faint = "#8A94A6";

    /// <summary>Hairlines and card borders.</summary>
    public const string Line = "#E3E8F1";

    /// <summary>The card a letter sits on.</summary>
    public const string Paper = "#FFFFFF";

    /// <summary>Behind the card — the site's sunken background.</summary>
    public const string Page = "#F4F7FC";

    /// <summary>The site's violet: buttons, links, the top edge. 6.1:1 on white.</summary>
    public const string Accent = "#5B3DF5";

    /// <summary>The site's cyan, the far end of the gradient.</summary>
    public const string Accent2 = "#0891A6";

    /// <summary>A tint of the accent, for a panel that wants to stand out without shouting.</summary>
    public const string AccentSoft = "#EFEBFF";

    /// <summary>The site's gradient, for clients that draw one. Always paired with <see cref="Accent"/>.</summary>
    public const string Gradient = "linear-gradient(120deg,#5B3DF5,#0891A6)";

    /// <summary>The font stack: the site's UI face where the reader has it, Arial where they do not.</summary>
    public const string Font = "-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Arial,Helvetica,sans-serif";

    /// <summary>
    /// The site's primary button, as email: a pill, violet everywhere, the gradient where it can be.
    /// </summary>
    /// <remarks>
    /// The color is on the cell as well as the link (<c>bgcolor</c>), so Outlook, which pads a link
    /// unpredictably, still shows a solid button the size of the cell.
    /// </remarks>
    public static string Button(string encodedText, string encodedUrl, string margin = "8px 0 16px 0", bool centered = false)
        => $"""<table role="presentation" cellpadding="0" cellspacing="0" border="0" {(centered ? "align=\"center\" " : "")}style="margin:{margin};"><tr><td align="center" bgcolor="{Accent}" style="border-radius:999px;background-color:{Accent};background-image:{Gradient};"><a href="{encodedUrl}" style="display:inline-block;background-color:{Accent};background-image:{Gradient};color:#FFFFFF;font-family:{Font};font-size:16px;font-weight:bold;text-decoration:none;padding:13px 30px;border-radius:999px;">{encodedText}</a></td></tr></table>""";
}
