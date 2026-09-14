using System.Text.RegularExpressions;

namespace Ben.Data.Common.Helpers;

/// <summary>The web links a post or message contains — one rule for the server that makes their cards and the page that shows them.</summary>
public static partial class PostLinks
{
    /// <summary>
    /// Distinct http(s) links from <paramref name="text"/> (plain, as the feed segments it) and from the link targets
    /// in <paramref name="html"/>, in the order they appear, at most <paramref name="max"/>.
    /// </summary>
    public static IReadOnlyList<string> In(string? text, string? html, int max = 3)
    {
        var found = new List<string>();
        foreach (var segment in FeedTextSegmenter.Segment(text))
            if (segment.Kind == FeedSegmentKind.Url) found.Add(segment.Value);
        if (!string.IsNullOrEmpty(html))
            foreach (Match m in Href().Matches(html))
                found.Add(System.Net.WebUtility.HtmlDecode(m.Groups[1].Value));

        return found
            .Where(u => Uri.TryCreate(u, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .ToList();
    }

    /// <summary>
    /// The first link in a draft that is finished: made with an editor's link button, or followed by a space or a new
    /// line. Null while the only link is still being typed.
    /// </summary>
    /// <remarks>
    /// A link still being typed is a different address at every keystroke; asking for a card for each would read pages
    /// that do not exist. Used by the composers to decide when to ask (2026-09-14).
    /// </remarks>
    public static string? FirstFinished(string? text, string? html)
    {
        if (In(null, html, max: 1) is [var made]) return made;

        var words = text ?? Ben.Data.Common.Text.PlainTextHtml.ToText(html);
        var segments = FeedTextSegmenter.Segment(words);
        for (var i = 0; i < segments.Count; i++)
        {
            if (segments[i].Kind != FeedSegmentKind.Url) continue;
            var next = i + 1 < segments.Count ? segments[i + 1].Text : null;
            if (next is { Length: > 0 } && char.IsWhiteSpace(next[0])) return segments[i].Value;
        }
        return null;
    }

    [GeneratedRegex(@"<a\b[^>]*\bhref\s*=\s*""(https?://[^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex Href();
}
