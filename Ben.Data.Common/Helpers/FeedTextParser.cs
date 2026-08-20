using System.Text;
using System.Text.RegularExpressions;

namespace Ben.Data.Common.Helpers;

/// <summary>
/// Finds the <c>@names</c> and <c>#tags</c> in a feed post.
/// </summary>
/// <remarks>
/// <para>Lives in Common because two sides need the same answer from the same text: the WebApi
/// extracts tokens when a post is written, to fill the mention and hashtag tables; the website
/// turns the same tokens into links when a post is read. Two parsers would drift, and the way they
/// would drift is a post whose visible links do not match the notifications it sent.</para>
///
/// <para><b>This finds candidates. It does not resolve them.</b> Whether <c>@sarahmitchell</c> is
/// anybody is a database question, answered by the caller — and answered conservatively: see
/// the WebApi's feed controller, which creates a mention only when exactly one account matches.
/// </para>
/// </remarks>
public static class FeedTextParser
{
    /// <summary>Longest tag we will store. Matches the column.</summary>
    public const int MaxTagLength = 64;

    /// <summary>Longest name token we will treat as a possible mention.</summary>
    public const int MaxMentionLength = 64;

    /// <summary>
    /// A mention: an <c>@</c> that starts a word, followed by name characters.
    /// </summary>
    /// <remarks>
    /// <para><c>(?&lt;![\w@.])</c> is the part that earns its keep. Without it, the <c>@</c> in
    /// <c>ben@example.com</c> matches and the post silently mentions whoever is called "example" —
    /// so an address written in a post becomes a notification to a stranger. Requiring the
    /// character before the <c>@</c> to be neither a word character, nor a dot, nor another
    /// <c>@</c> means a mention has to start a word.</para>
    ///
    /// <para>Dots and hyphens are allowed inside a name but a trailing one is dropped by
    /// <c>[\w-]</c> on the final character, so <c>@sarah.</c> at the end of a sentence mentions
    /// "sarah" rather than "sarah.".</para>
    /// </remarks>
    private static readonly Regex MentionPattern = new(
        @"(?<![\w@.])@([A-Za-z0-9][A-Za-z0-9._-]{0,62}[A-Za-z0-9]|[A-Za-z0-9])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// A hashtag: a <c>#</c> that starts a word, followed by letters, digits or underscores.
    /// </summary>
    /// <remarks>
    /// No leading digit. <c>#1</c> and <c>#2026</c> are almost always a numbered list or a year
    /// rather than a topic, and a tag page full of them helps nobody. A tag containing digits is
    /// fine — <c>#evp2026</c> works.
    /// </remarks>
    private static readonly Regex HashtagPattern = new(
        @"(?<![\w#])#([A-Za-z_][A-Za-z0-9_]{0,63})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// The distinct name tokens a post mentions, in the order they appear, as written.
    /// </summary>
    /// <remarks>
    /// Case is preserved because the caller may want to echo what the author typed. Comparison is
    /// the caller's business — use <see cref="NormalizeName"/> for that.
    /// </remarks>
    public static IReadOnlyList<string> FindMentions(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();

        foreach (Match match in MentionPattern.Matches(text))
        {
            var name = match.Groups[1].Value;
            if (name.Length > MaxMentionLength) continue;
            if (seen.Add(name)) result.Add(name);
        }

        return result;
    }

    /// <summary>
    /// The distinct tags a post uses, lower-cased and without their <c>#</c>.
    /// </summary>
    /// <remarks>
    /// Lower-cased here rather than at the database, so <c>#EVP</c> and <c>#evp</c> are one tag and
    /// the column can be looked up with a seek rather than a scan over <c>LOWER(Tag)</c>.
    /// </remarks>
    public static IReadOnlyList<string> FindHashtags(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();

        foreach (Match match in HashtagPattern.Matches(text))
        {
            var tag = match.Groups[1].Value.ToLowerInvariant();
            if (tag.Length > MaxTagLength) continue;
            if (seen.Add(tag)) result.Add(tag);
        }

        return result;
    }

    /// <summary>
    /// A display name reduced to what a mention can be compared against: letters and digits only,
    /// lower-cased.
    /// </summary>
    /// <remarks>
    /// <para>"Sarah Mitchell", "sarah mitchell" and "Sarah-Mitchell" all become
    /// <c>sarahmitchell</c>, which is what somebody types after an <c>@</c>. Names in this product
    /// contain spaces, and a mention token cannot, so the comparison has to happen somewhere —
    /// doing it in one function means both sides do it the same way.</para>
    ///
    /// <para>This is lossy on purpose, and the caller must treat a collision as a refusal rather
    /// than a coin toss: two people whose names normalise alike cannot be told apart here, and
    /// notifying the wrong one is worse than notifying neither.</para>
    /// </remarks>
    public static string NormalizeName(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return string.Empty;

        var builder = new StringBuilder(displayName.Length);
        foreach (var c in displayName)
        {
            if (char.IsLetterOrDigit(c)) builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }
}
