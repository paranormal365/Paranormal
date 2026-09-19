using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using System.Text.RegularExpressions;

namespace Ben.Data.WebApi.Services.Redaction;

/// <summary>
/// Replaces a private case's real names with their public stand-ins at display time (item 184).
/// Stored prose is NEVER modified — the substitution happens in the API projection, so what the
/// group wrote stays theirs and what the public reads stays safe.
/// </summary>
/// <remarks>
/// <para><b>Matching</b>: whole words, case-insensitive, longest token first (so "Daniel Park"
/// entries don't leave a stray "Park" after "Daniel" was replaced by a phrase containing
/// neither). Same word-boundary rule as <c>PublicTitleLeakCheck</c>, so "Parker" is not "Park".</para>
///
/// <para><b>HTML</b>: parsed with AngleSharp and replaced in TEXT NODES only — a client surnamed
/// "Strong" must not corrupt <c>&lt;strong&gt;</c>, and names inside href slugs or attributes are
/// markup, not prose. If parsing throws, the raw string is regex-replaced instead: failing toward
/// privacy, because a mangled tag beats a leaked name. Accepted limitation, documented in tests:
/// a name split across inline tags (<c>&lt;b&gt;P&lt;/b&gt;ark</c>) is not matched.</para>
///
/// <para><b>Grammar</b>: the replacement is capitalized at a sentence start and lowercased
/// elsewhere ("The client said…" / "we met the client"). Minor artifacts remain possible and are
/// accepted — a safe clumsy sentence over a fluent leak.</para>
/// </remarks>
public static class CaseProseRedactor
{
    /// <summary>Plain text: titles, captions, place names. Null-in, null-out.</summary>
    public static string? Redact(string? text, RedactionRoster roster)
    {
        if (string.IsNullOrEmpty(text) || roster.Entries.Count == 0) return text;
        return ReplaceAll(text, roster);
    }

    /// <summary>HTML prose: descriptions, timeline bodies, investigation notes.</summary>
    public static string? RedactHtml(string? html, RedactionRoster roster)
    {
        if (string.IsNullOrEmpty(html) || roster.Entries.Count == 0) return html;

        try
        {
            var parser = new HtmlParser();
            var document = parser.ParseDocument("<body>" + html + "</body>");
            var walker = document.CreateTreeWalker(document.Body!, FilterSettings.Text);

            var changed = false;
            for (var node = walker.ToNext(); node is not null; node = walker.ToNext())
            {
                var original = node.TextContent;
                var replaced = ReplaceAll(original, roster);
                if (!ReferenceEquals(original, replaced) && original != replaced)
                {
                    node.TextContent = replaced;
                    changed = true;
                }
            }

            return changed ? document.Body!.InnerHtml : html;
        }
        catch
        {
            // Unparseable markup: fail toward privacy — replace on the raw string. A corrupted
            // tag is a rendering blemish; a leaked name is somebody's address in practice.
            return ReplaceAll(html, roster);
        }
    }

    /// <summary>Batch-lookup convenience: no roster for the case means it renders verbatim.</summary>
    public static string? RedactFor(
        IReadOnlyDictionary<Guid, RedactionRoster> rosters, Guid caseId, string? text)
        => rosters.TryGetValue(caseId, out var roster) ? Redact(text, roster) : text;

    /// <summary>Batch-lookup convenience for HTML prose.</summary>
    public static string? RedactHtmlFor(
        IReadOnlyDictionary<Guid, RedactionRoster> rosters, Guid caseId, string? html)
        => rosters.TryGetValue(caseId, out var roster) ? RedactHtml(html, roster) : html;

    private static string ReplaceAll(string text, RedactionRoster roster)
    {
        var result = text;
        foreach (var (token, replacement) in roster.Entries
                     .SelectMany(e => e.Tokens.Select(t => (t, e.Replacement)))
                     .OrderByDescending(p => p.t.Length))
        {
            // The lambda must look at the string actually being replaced — after the first
            // token's pass, indices no longer line up with the original text.
            var current = result;
            result = Regex.Replace(current, $@"\b{Regex.Escape(token)}\b",
                match => Shaped(replacement, current, at: match.Index),
                RegexOptions.IgnoreCase);
        }
        return result;

        static string Shaped(string replacement, string current, int at)
        {
            // "The Vexley house" must become "the family house", not "the the family house":
            // when the word already in front of the match is the replacement's own article,
            // the replacement sheds it.
            var precedingWord = WordBefore(current, at);
            foreach (var article in (string[])["the ", "a ", "an "])
            {
                if (replacement.StartsWith(article, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(precedingWord, article.TrimEnd(), StringComparison.OrdinalIgnoreCase))
                {
                    return replacement[article.Length..];
                }
            }

            // A label with capitals of its own past the first letter ("The Hargrove Family",
            // "Mrs H") is a proper name and keeps its shape everywhere.
            if (replacement.Skip(1).Any(char.IsUpper)) return replacement;

            // Sentence start: beginning of the text, or the first word after . ! ? or a newline.
            for (var i = at - 1; i >= 0; i--)
            {
                var ch = current[i];
                if (char.IsWhiteSpace(ch)) continue;
                if (ch is '.' or '!' or '?' or '\n' or '"' or '“') break;
                return char.ToLowerInvariant(replacement[0]) + replacement[1..];
            }
            return char.ToUpperInvariant(replacement[0]) + replacement[1..];
        }

        static string? WordBefore(string current, int at)
        {
            var end = at;
            while (end > 0 && char.IsWhiteSpace(current[end - 1])) end--;
            var start = end;
            while (start > 0 && char.IsLetter(current[start - 1])) start--;
            return start == end ? null : current[start..end];
        }
    }
}
