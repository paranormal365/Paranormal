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
}
