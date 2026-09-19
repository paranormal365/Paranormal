using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Ben.Data.Common.Text;

/// <summary>
/// The small conversions between plain text and the HTML an editor writes, used on both sides of the API.
/// </summary>
/// <remarks>
/// <para>
/// Case descriptions, case notes and messages to the client became formatted text on 2026-09-14 (beta feedback).
/// Three things follow, and each is here once so the website and the API cannot disagree about them:
/// </para>
/// <list type="bullet">
///   <item>An editor that has been emptied still writes <c>&lt;p&gt;&lt;/p&gt;</c>, so "is there anything here" is
///   asked of the text inside the tags, not of the string (<see cref="HasText"/>).</item>
///   <item>Text written before the change is plain, with line breaks; rendered as HTML it would run together
///   (<see cref="LooksLikeHtml"/>, <see cref="FromPlainText"/>).</item>
///   <item>Somewhere that can only show plain text — a notification, an excerpt — needs the words back
///   (<see cref="ToText"/>).</item>
/// </list>
/// <para>None of this cleans markup. Anything stored as HTML goes through the API's sanitizer first.</para>
/// </remarks>
public static partial class PlainTextHtml
{
    [GeneratedRegex(@"<\s*(p|br|div|ul|ol|li|strong|em|b|i|u|a|h[1-6]|span|blockquote|img)\b[^>]*>",
        RegexOptions.IgnoreCase)]
    private static partial Regex HtmlTag();

    [GeneratedRegex(@"<[^>]*>")]
    private static partial Regex AnyTag();

    [GeneratedRegex(@"<\s*img\b", RegexOptions.IgnoreCase)]
    private static partial Regex ImageTag();

    [GeneratedRegex(@"<\s*br\s*/?\s*>|<\s*/\s*(div|li)\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex LineEnd();

    [GeneratedRegex(@"<\s*/\s*(p|h[1-6]|blockquote|ul|ol)\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex ParagraphEnd();

    [GeneratedRegex(@"\n[ \t]*\n(?:[ \t]*\n)*")]
    private static partial Regex BlankLines();

    /// <summary>True when the string contains an element an editor would write, rather than plain text.</summary>
    public static bool LooksLikeHtml(string? value) => value is not null && HtmlTag().IsMatch(value);

    /// <summary>
    /// True when the HTML says something: visible characters once tags are removed, or a picture.
    /// <c>&lt;p&gt;&lt;/p&gt;</c>, <c>&lt;p&gt;&amp;nbsp;&lt;/p&gt;</c> and whitespace are all nothing.
    /// </summary>
    public static bool HasText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return false;
        if (ImageTag().IsMatch(html)) return true;
        return ToText(html).Trim().Length > 0;
    }

    /// <summary>
    /// The words of an HTML fragment, laid out as plain text: a blank line between paragraphs, headings and lists;
    /// a line break for each list item and <c>&lt;br&gt;</c>; entities decoded.
    /// </summary>
    public static string ToText(string? html)
    {
        if (string.IsNullOrEmpty(html)) return string.Empty;
        var withBreaks = LineEnd().Replace(ParagraphEnd().Replace(html, "\n\n"), "\n");
        var text = WebUtility.HtmlDecode(AnyTag().Replace(withBreaks, string.Empty))
            .Replace('\u00A0', ' ')
            .Replace("\r\n", "\n");
        var lines = text.Split('\n').Select(l => l.TrimEnd());
        return BlankLines().Replace(string.Join("\n", lines), "\n\n").Trim();
    }

    /// <summary>
    /// Plain text as HTML that reads the same: characters encoded, a blank line starts a new paragraph, a single
    /// line break stays a line break.
    /// </summary>
    public static string FromPlainText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var normalised = text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        var html = new StringBuilder();
        foreach (var paragraph in BlankLines().Split(normalised))
        {
            if (string.IsNullOrWhiteSpace(paragraph)) continue;
            var lines = paragraph.Trim('\n').Split('\n').Select(WebUtility.HtmlEncode);
            html.Append("<p>").Append(string.Join("<br>", lines)).Append("</p>");
        }
        return html.ToString();
    }
}
