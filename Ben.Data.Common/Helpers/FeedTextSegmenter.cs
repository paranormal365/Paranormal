namespace Ben.Data.Common.Helpers;

/// <summary>What one run of a feed post's body is.</summary>
public enum FeedSegmentKind
{
    /// <summary>Ordinary text. Rendered as-is, and never as markup.</summary>
    Text = 0,

    /// <summary>An <c>@name</c>. Carries the handle, lower-cased, without the <c>@</c>.</summary>
    Mention = 1,

    /// <summary>A <c>#tag</c>. Carries the tag, lower-cased, without the <c>#</c>.</summary>
    Hashtag = 2,

    /// <summary>
    /// A web address. Carries the whole URL, with any trailing sentence punctuation left out.
    /// </summary>
    /// <remarks>
    /// Only <c>http://</c> and <c>https://</c>, spelled out. Guessing that "example.com" is a link
    /// turns every sentence with a full stop in it into a minefield, and a scheme is what the
    /// author typed when they meant a link.
    /// </remarks>
    Url = 3,
}

/// <summary>
/// One run of a post's body.
/// </summary>
/// <param name="Kind">What this run is.</param>
/// <param name="Text">The text exactly as the author typed it, including any <c>@</c> or <c>#</c>.</param>
/// <param name="Value">
/// The normalised handle or tag, for a mention or hashtag; empty for plain text. This is what a
/// caller looks a mention up by, or builds a tag URL from — never <paramref name="Text"/>, which is
/// what the author happened to type.
/// </param>
public readonly record struct FeedSegment(FeedSegmentKind Kind, string Text, string Value = "");

/// <summary>
/// Splits a feed post into runs of plain text, mentions and tags, in order.
/// </summary>
/// <remarks>
/// <para>Separate from <see cref="FeedTextParser"/>, which answers "which names and tags does this
/// post contain" — the question the server asks when filling its tables. This answers "where are
/// they", which is what rendering needs, and it is a genuinely different job: the parser returns
/// each token <i>once</i>, so a name used twice appears in its list once, and a renderer driven by
/// that list would silently skip the second occurrence.</para>
///
/// <para>Both use the same rules, because both call the same parser to decide what a token is.
/// That includes the rule that earns its keep most: <b>an email address is not a mention</b>. A
/// naive scan for <c>@</c> would linkify "example" in <c>ben@example.com</c>.</para>
///
/// <para>Lives in Common, and is pure, so it can be tested directly rather than through a
/// component. It was written inside a Razor file first, which made the interesting cases —
/// adjacent tokens, a token at the very start, punctuation — awkward to reach.</para>
/// </remarks>
public static class FeedTextSegmenter
{
    /// <summary>The runs of <paramref name="body"/>, in order. Empty for an empty body.</summary>
    public static IReadOnlyList<FeedSegment> Segment(string? body)
    {
        if (string.IsNullOrEmpty(body)) return [];

        var segments = new List<FeedSegment>();
        var position = 0;

        while (position < body.Length)
        {
            var found = NextToken(body, position);
            if (found is null)
            {
                segments.Add(new FeedSegment(FeedSegmentKind.Text, body[position..]));
                break;
            }

            var (start, length, kind, value) = found.Value;

            if (start > position)
                segments.Add(new FeedSegment(FeedSegmentKind.Text, body[position..start]));

            segments.Add(new FeedSegment(kind, body.Substring(start, length), value));
            position = start + length;
        }

        return segments;
    }

    /// <summary>The next token at or after <paramref name="from"/>, or null when there is none.</summary>
    private static (int Start, int Length, FeedSegmentKind Kind, string Value)? NextToken(string body, int from)
    {
        for (var i = from; i < body.Length; i++)
        {
            var marker = body[i];

            // A URL, at a word boundary so "see:https://x" and "ahttps://x" are not links.
            if ((marker is 'h' or 'H') && AtBoundary(body, i) && UrlLengthAt(body, i) is { } urlLength)
                return (i, urlLength, FeedSegmentKind.Url, body.Substring(i, urlLength));

            if (marker is not ('@' or '#')) continue;

            // The parser decides what counts, so the rules live in exactly one place. It is given
            // the character *before* the marker as well, because its word-boundary rule needs it:
            // slicing at the '@' would put it at the start of a string, where it always looks like
            // a boundary, and ben@example.com would mention "example".
            var guard = i == 0 ? string.Empty : body[i - 1].ToString();
            var candidate = guard + body[i..];

            var value = marker == '@'
                ? FeedTextParser.FindMentions(candidate).FirstOrDefault()
                : FeedTextParser.FindHashtags(candidate).FirstOrDefault();

            if (value is null) continue;

            // FindMentions preserves the author's case; FindHashtags lower-cases. Compare against
            // the body case-insensitively, then normalise the value the caller gets.
            var token = marker + value;
            if (!body.AsSpan(i).StartsWith(token, StringComparison.OrdinalIgnoreCase)) continue;

            var normalised = marker == '@' ? UserHandle.Normalize(value) : value.ToLowerInvariant();
            var kind = marker == '@' ? FeedSegmentKind.Mention : FeedSegmentKind.Hashtag;

            return (i, token.Length, kind, normalised);
        }

        return null;
    }

    /// <summary>
    /// Whether position <paramref name="i"/> starts a word.
    /// </summary>
    /// <remarks>
    /// Quotes count as openers as well as brackets: a body that says <c>"https://example.com"</c>
    /// is quoting a link, not writing a word that happens to begin with h.
    /// </remarks>
    private static bool AtBoundary(string body, int i)
        => i == 0
        || char.IsWhiteSpace(body[i - 1])
        || body[i - 1] is '(' or '[' or '<' or '"' or '\'';

    /// <summary>
    /// How long the URL starting at <paramref name="i"/> is, or null when there is not one.
    /// </summary>
    /// <remarks>
    /// <para>Runs to the first whitespace, then gives back the punctuation a sentence put there:
    /// somebody writing "look at https://x.example/a." means the link to stop before the full
    /// stop. Closing brackets go back the same way, but only when the URL does not contain the
    /// matching opener — a Wikipedia address ending in "(film)" is one people actually paste.</para>
    ///
    /// <para>The scheme must be followed by something: "https://" alone is not an address, and
    /// linkifying it produces a link to nowhere.</para>
    /// </remarks>
    private static int? UrlLengthAt(string body, int i)
    {
        var rest = body.AsSpan(i);

        var scheme = rest.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? 8
                   : rest.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? 7
                   : 0;
        if (scheme == 0) return null;

        var end = i + scheme;
        while (end < body.Length && !char.IsWhiteSpace(body[end])) end++;
        if (end == i + scheme) return null;   // the scheme and nothing after it

        while (end > i + scheme)
        {
            var last = body[end - 1];

            if (last is '.' or ',' or ';' or ':' or '!' or '?' or '"' or '\'')
            {
                end--;
                continue;
            }

            if (last is ')' or ']' && !body.AsSpan(i, end - i - 1).Contains(last == ')' ? '(' : '['))
            {
                end--;
                continue;
            }

            break;
        }

        return end - i;
    }
}
