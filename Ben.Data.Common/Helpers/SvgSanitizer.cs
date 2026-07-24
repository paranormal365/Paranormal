using System.Text;
using System.Xml.Linq;

namespace Ben.Data.Common.Helpers;

/// <summary>
/// Sanitizes SVG documents by removing elements and attributes that can execute
/// JavaScript or load external resources — preventing XSS via uploaded SVG files.
/// </summary>
/// <remarks>
/// Uses <see cref="System.Xml.Linq.XDocument"/> to parse the SVG as valid XML.
/// Malformed or non-XML SVG is rejected with an <see cref="InvalidOperationException"/>.
/// </remarks>
public static class SvgSanitizer
{
    // ── Dangerous elements ────────────────────────────────────────────────────

    /// <summary>Elements that can execute code or load external content.</summary>
    private static readonly HashSet<string> _blockedElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "object", "embed", "applet", "iframe", "link", "meta",
        "base", "form", "input", "button",
    };

    // ── Dangerous attributes ─────────────────────────────────────────────────

    /// <summary>
    /// DOM event-handler attributes that run JavaScript when triggered.
    /// Covers the full set of intrinsic events defined in the HTML/SVG spec.
    /// </summary>
    private static readonly HashSet<string> _blockedAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "onabort", "onanimationend", "onanimationiteration", "onanimationstart",
        "onblur", "oncanplay", "oncanplaythrough", "onchange", "onclick",
        "onclose", "oncontextmenu", "oncopy", "oncuechange", "oncut",
        "ondblclick", "ondrag", "ondragend", "ondragenter", "ondragleave",
        "ondragover", "ondragstart", "ondrop", "ondurationchange", "onemptied",
        "onended", "onerror", "onfocus", "onfocusin", "onfocusout",
        "onformdata", "ongotpointercapture", "oninput", "oninvalid",
        "onkeydown", "onkeypress", "onkeyup", "onload", "onloadeddata",
        "onloadedmetadata", "onloadstart", "onlostpointercapture", "onmousedown",
        "onmouseenter", "onmouseleave", "onmousemove", "onmouseout",
        "onmouseover", "onmouseup", "onpaste", "onpause", "onplay",
        "onplaying", "onpointercancel", "onpointerdown", "onpointerenter",
        "onpointerleave", "onpointermove", "onpointerout", "onpointerover",
        "onpointerup", "onprogress", "onratechange", "onreset", "onresize",
        "onscroll", "onsecuritypolicyviolation", "onseeked", "onseeking",
        "onselect", "onslotchange", "onstalled", "onsubmit", "onsuspend",
        "ontimeupdate", "ontoggle", "ontransitioncancel", "ontransitionend",
        "ontransitionrun", "ontransitionstart", "onunload", "onvolumechange",
        "onwaiting", "onwebkitanimationend", "onwebkitanimationiteration",
        "onwebkitanimationstart", "onwebkittransitionend", "onwheel",
        // SVG-specific
        "onactivate", "onbegin", "onend", "onfocusin", "onfocusout",
        "onrepeat", "onzoom",
    };

    /// <summary>URL-bearing attribute names that must not use <c>javascript:</c> or <c>vbscript:</c> schemes.</summary>
    private static readonly HashSet<string> _urlAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "href", "src", "action", "data", "formaction", "poster", "xlink:href",
    };

    private static readonly string[] _dangerousSchemes = ["javascript:", "vbscript:", "data:text/html"];

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a sanitized copy of <paramref name="svgBytes"/>.
    /// All <c>&lt;script&gt;</c> elements, event-handler attributes (on*), and
    /// <c>javascript:</c>/<c>vbscript:</c> URL attribute values are removed.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the bytes cannot be parsed as well-formed XML/SVG.</exception>
    public static byte[] Sanitize(byte[] svgBytes)
    {
        string xml;
        try
        {
            xml = Encoding.UTF8.GetString(svgBytes);
        }
        catch
        {
            throw new InvalidOperationException("SVG file contains invalid UTF-8 encoding.");
        }

        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"SVG file is not well-formed XML: {ex.Message}");
        }

        // 1. Remove blocked elements (depth-first to avoid concurrent-modification issues)
        doc.Descendants()
           .Where(e => _blockedElements.Contains(e.Name.LocalName))
           .ToList()
           .ForEach(e => e.Remove());

        // 2. Strip blocked attributes and dangerous URL schemes from every remaining element
        foreach (var element in doc.Descendants())
        {
            var toRemove = element.Attributes()
                .Where(a => IsBlockedAttribute(a))
                .ToList();

            foreach (var a in toRemove)
                a.Remove();
        }

        return Encoding.UTF8.GetBytes(doc.ToString(SaveOptions.DisableFormatting));
    }

    /// <summary>Returns <c>true</c> if the attribute should be removed.</summary>
    private static bool IsBlockedAttribute(XAttribute attr)
    {
        var localName = attr.Name.LocalName;

        // Event handlers
        if (_blockedAttributes.Contains(localName))
            return true;

        // URL attributes that carry javascript:/vbscript: schemes
        if (_urlAttributes.Contains(localName))
        {
            var val = attr.Value.TrimStart();
            foreach (var scheme in _dangerousSchemes)
                if (val.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
                    return true;
        }

        return false;
    }
}
