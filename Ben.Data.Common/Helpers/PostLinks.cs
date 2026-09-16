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
    /// The first link in a draft that is finished: made with an editor's link button, followed by a space or a new line
    /// (Enter), or — once the person has left the box — the last thing in it.
    /// </summary>
    /// <remarks>
    /// A link still being typed is a different address at every keystroke; asking for a card for each would read pages
    /// that do not exist. Ben, 2026-09-14: "don't fetch it until they either press enter or it is an obvious finish or
    /// loses focus and is an obvious web link."
    /// </remarks>
    /// <param name="leftTheBox">True once focus has left the box: a link at the very end then counts as finished.</param>
    public static string? FirstFinished(string? text, string? html, bool leftTheBox = false)
    {
        if (In(null, html, max: 1) is [var made]) return made;

        var words = text ?? Ben.Data.Common.Text.PlainTextHtml.ToText(html);
        var segments = FeedTextSegmenter.Segment(words);
        for (var i = 0; i < segments.Count; i++)
        {
            if (segments[i].Kind != FeedSegmentKind.Url) continue;
            if (!IsObviousWebLink(segments[i].Value)) continue;
            var next = i + 1 < segments.Count ? segments[i + 1].Text : null;
            if (next is { Length: > 0 } && char.IsWhiteSpace(next[0])) return segments[i].Value;
            if (next is null && leftTheBox) return segments[i].Value;
        }
        return null;
    }

    /// <summary>An http(s) address whose host is a name with a real-looking ending, like <c>example.com</c> — not <c>https://exa</c>.</summary>
    public static bool IsObviousWebLink(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return false;
        var labels = uri.Host.Split('.');
        return labels.Length >= 2 && labels[^1].Length >= 2 && labels[^1].All(char.IsLetter) && labels.All(l => l.Length > 0);
    }

    [GeneratedRegex(@"<a\b[^>]*\bhref\s*=\s*""(https?://[^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex Href();
}
