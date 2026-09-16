using System.Net;
using System.Text;

namespace Ben.Canvas.Core.Paste;

/// <summary>
/// The HTML a message box may hold, and the normaliser that enforces it.
/// </summary>
/// <remarks>
/// <para><b>Why this runs on every read, not only on paste.</b> A message box renders its HTML as markup.
/// If only the paste path cleaned it, a board file edited by hand, a board imported from someone else,
/// a board reopened from the server or a copy from the clipboard could each carry an <c>onerror</c>
/// handler straight into the page - and the site's sign-in cookie is sent cross-site. So the document
/// serializer calls <see cref="Normalize"/> on every message as it reads a file, whatever the source.</para>
///
/// <para><b>Why a hand-written tokenizer.</b> Core carries no packages, and the browser's own parser is
/// not available to C#. The rules here are strict enough that a simple tokenizer is sufficient: anything
/// it does not recognise is dropped, never passed through. Text is decoded and re-encoded, so no markup
/// can survive inside it.</para>
///
/// <para>No <c>img</c>, <c>figure</c> or <c>figcaption</c>: a remote picture inside pasted HTML would load
/// from a stranger's server every time the board opened. Pictures become image or link blocks instead.
/// No <c>class</c> or <c>id</c> either: foreign class names would collide with the site's.</para>
/// </remarks>
public static class PasteHtmlAllowList
{
    public static readonly IReadOnlySet<string> Tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "div", "span", "p", "br", "hr", "h1", "h2", "h3", "h4", "h5", "h6", "ul", "ol", "li", "dl", "dt", "dd",
        "strong", "em", "b", "i", "u", "s", "small", "mark", "sub", "sup", "blockquote", "pre", "code", "a",
        "table", "thead", "tbody", "tfoot", "tr", "th", "td", "caption",
    };

    public static readonly IReadOnlySet<string> Attributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "href", "title", "colspan", "rowspan", "role",
    };

    public const string AriaPrefix = "aria-";

    public static readonly IReadOnlySet<string> Schemes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "http", "https" };

    /// <summary>Elements removed together with everything inside them.</summary>
    public static readonly IReadOnlySet<string> NeverTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "iframe", "object", "embed", "form", "input", "select", "textarea", "button", "label",
        "fieldset", "legend", "output", "svg", "math", "template", "noscript", "title", "head", "video", "audio",
        "canvas", "frame", "frameset", "applet", "base", "link", "meta",
    };

    private static readonly HashSet<string> Void = new(StringComparer.OrdinalIgnoreCase) { "br", "hr" };

    /// <summary>Elements whose content is raw text, so a "&lt;" inside them does not start a tag.</summary>
    private static readonly HashSet<string> RawText = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "textarea", "title", "noscript", "xmp", "iframe", "noembed", "noframes",
    };

    private static readonly HashSet<string> VoidNever = new(StringComparer.OrdinalIgnoreCase)
    {
        "input", "embed", "base", "link", "meta", "frame",
    };

    /// <summary>The lists in the shape the browser-side sanitiser reads them, so the two cannot drift.</summary>
    public static object ToInteropArgument() => new
    {
        tags = Tags.OrderBy(t => t, StringComparer.Ordinal).ToArray(),
        attributes = Attributes.OrderBy(a => a, StringComparer.Ordinal).ToArray(),
        ariaPrefix = AriaPrefix,
        schemes = Schemes.OrderBy(s => s, StringComparer.Ordinal).ToArray(),
        neverTags = NeverTags.OrderBy(t => t, StringComparer.Ordinal).ToArray(),
    };

    /// <summary>Returns only allowed markup, well formed, with every tag closed.</summary>
    public static string Normalize(string? html)
    {
        if (string.IsNullOrEmpty(html)) return "";

        var output = new StringBuilder(html.Length);
        var open = new List<string>();
        var i = 0;

        while (i < html.Length)
        {
            var lt = html.IndexOf('<', i);
            if (lt < 0)
            {
                AppendText(output, html[i..]);
                break;
            }

            if (lt > i) AppendText(output, html[i..lt]);

            if (html.AsSpan(lt).StartsWith("<!--"))
            {
                var end = html.IndexOf("-->", lt + 4, StringComparison.Ordinal);
                i = end < 0 ? html.Length : end + 3;
                continue;
            }

            if (lt + 1 < html.Length && html[lt + 1] is '!' or '?')
            {
                var end = html.IndexOf('>', lt + 2);
                i = end < 0 ? html.Length : end + 1;
                continue;
            }

            var closing = lt + 1 < html.Length && html[lt + 1] == '/';
            var nameStart = lt + (closing ? 2 : 1);
            var nameEnd = nameStart;
            while (nameEnd < html.Length && (char.IsAsciiLetterOrDigit(html[nameEnd]) || html[nameEnd] == '-')) nameEnd++;

            if (nameEnd == nameStart || !char.IsAsciiLetter(html[nameStart]))
            {
                // "a < b" is text, not a tag.
                output.Append("&lt;");
                i = lt + 1;
                continue;
            }

            var name = html[nameStart..nameEnd].ToLowerInvariant();
            var (attributes, tagEnd, selfClosing) = ReadAttributes(html, nameEnd);
            i = tagEnd;

            if (closing)
            {
                var index = open.LastIndexOf(name);
                if (index < 0) continue;
                for (var k = open.Count - 1; k >= index; k--) output.Append("</").Append(open[k]).Append('>');
                open.RemoveRange(index, open.Count - index);
                continue;
            }

            if (NeverTags.Contains(name))
            {
                if (!selfClosing && !VoidNever.Contains(name)) i = SkipElement(html, name, i);
                continue;
            }

            if (!Tags.Contains(name)) continue;

            output.Append('<').Append(name);
            foreach (var (attrName, attrValue) in attributes)
            {
                var clean = CleanAttribute(name, attrName, attrValue);
                if (clean is null) continue;
                output.Append(' ').Append(attrName).Append("=\"").Append(WebUtility.HtmlEncode(clean)).Append('"');
            }

            if (name == "a") output.Append(" target=\"_blank\" rel=\"noopener noreferrer nofollow\"");
            output.Append('>');

            if (!Void.Contains(name) && !selfClosing) open.Add(name);
            else if (!Void.Contains(name)) output.Append("</").Append(name).Append('>');
        }

        for (var k = open.Count - 1; k >= 0; k--) output.Append("</").Append(open[k]).Append('>');
        return output.ToString();
    }

    /// <summary>The visible text of a fragment, for deciding what a paste really is.</summary>
    public static string VisibleText(string? html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        var normalised = Normalize(html);
        var text = new StringBuilder();
        var inTag = false;
        foreach (var c in normalised)
        {
            if (c == '<') { inTag = true; text.Append(' '); continue; }
            if (c == '>') { inTag = false; continue; }
            if (!inTag) text.Append(c);
        }

        return string.Join(' ', WebUtility.HtmlDecode(text.ToString()).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string? CleanAttribute(string tag, string name, string value)
    {
        var lower = name.ToLowerInvariant();
        var allowed = Attributes.Contains(lower) || (lower.StartsWith(AriaPrefix, StringComparison.Ordinal) && lower.Length > AriaPrefix.Length);
        if (!allowed) return null;

        if (lower == "href")
        {
            if (tag != "a") return null;
            return SafeHttpUrl.Normalize(value);
        }

        if (lower is "colspan" or "rowspan")
            return int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) && n is > 0 and <= 100
                ? n.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : null;

        return value;
    }

    private static void AppendText(StringBuilder output, string raw) =>
        output.Append(WebUtility.HtmlEncode(WebUtility.HtmlDecode(raw)));

    private static (List<(string Name, string Value)> Attributes, int End, bool SelfClosing) ReadAttributes(string html, int start)
    {
        var list = new List<(string, string)>();
        var i = start;
        var selfClosing = false;

        while (i < html.Length)
        {
            while (i < html.Length && (char.IsWhiteSpace(html[i]) || html[i] == '/'))
            {
                if (html[i] == '/') selfClosing = true;
                i++;
            }

            if (i >= html.Length) break;
            if (html[i] == '>') return (list, i + 1, selfClosing);
            selfClosing = false;

            var nameStart = i;
            while (i < html.Length && !char.IsWhiteSpace(html[i]) && html[i] is not ('=' or '>' or '/')) i++;
            var attrName = html[nameStart..i];
            while (i < html.Length && char.IsWhiteSpace(html[i])) i++;

            var value = "";
            if (i < html.Length && html[i] == '=')
            {
                i++;
                while (i < html.Length && char.IsWhiteSpace(html[i])) i++;
                if (i < html.Length && html[i] is '"' or '\'')
                {
                    var quote = html[i];
                    var close = html.IndexOf(quote, i + 1);
                    if (close < 0) close = html.Length;
                    value = html[(i + 1)..close];
                    i = Math.Min(html.Length, close + 1);
                }
                else
                {
                    var valueStart = i;
                    while (i < html.Length && !char.IsWhiteSpace(html[i]) && html[i] != '>') i++;
                    value = html[valueStart..i];
                }
            }

            if (attrName.Length > 0) list.Add((attrName, WebUtility.HtmlDecode(value)));
        }

        return (list, html.Length, selfClosing);
    }

    private static int SkipElement(string html, string name, int from)
    {
        if (RawText.Contains(name))
        {
            var close = html.IndexOf("</" + name, from, StringComparison.OrdinalIgnoreCase);
            if (close < 0) return html.Length;
            var gt = html.IndexOf('>', close);
            return gt < 0 ? html.Length : gt + 1;
        }

        // Nested elements of the same name (svg inside svg) are counted so the skip ends at the right close.
        var depth = 1;
        var i = from;
        while (i < html.Length && depth > 0)
        {
            var lt = html.IndexOf('<', i);
            if (lt < 0) return html.Length;
            var closing = lt + 1 < html.Length && html[lt + 1] == '/';
            var ns = lt + (closing ? 2 : 1);
            var ne = ns;
            while (ne < html.Length && (char.IsAsciiLetterOrDigit(html[ne]) || html[ne] == '-')) ne++;
            var gt = html.IndexOf('>', ne);
            var end = gt < 0 ? html.Length : gt + 1;

            if (ne > ns && string.Equals(html[ns..ne], name, StringComparison.OrdinalIgnoreCase))
            {
                var selfClosing = gt > 0 && html[gt - 1] == '/';
                if (closing) depth--;
                else if (!selfClosing) depth++;
            }

            i = end;
        }

        return i;
    }
}

/// <summary>The one place a link address is judged safe to put in an href.</summary>
public static class SafeHttpUrl
{
    /// <summary>The absolute http or https address, or null for anything else (javascript:, data:, relative).</summary>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        if (trimmed.Length > 2048) return null;
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;
        if (string.IsNullOrEmpty(uri.Host)) return null;
        return uri.AbsoluteUri;
    }

    /// <summary>True for an absolute https address only, as required for pictures shown through the proxy.</summary>
    public static bool IsHttps(string? value) =>
        Normalize(value) is { } normalised && normalised.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
}
