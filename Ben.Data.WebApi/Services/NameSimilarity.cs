namespace Ben.Data.WebApi.Services;

/// <summary>
/// Spots a name that is probably a typo of one already in the shared catalog.
/// </summary>
/// <remarks>
/// <para>Ben's case: somebody types <b>Sansung</b> meaning <b>Samsung</b>. The unique index does not
/// help — the names genuinely differ — so the catalog quietly gains a second manufacturer that
/// nobody meant to create, and everyone after them has two to choose between.</para>
///
/// <para><b>Catching it at the moment of typing is the only cheap moment.</b> Afterwards it needs a
/// SuperAdmin to notice, and merging is more work than asking one question. The pattern already
/// exists here for places, where <c>FindPlaceCandidatesAsync</c> says "did you mean this?" before a
/// duplicate is created rather than after.</para>
///
/// <para>Levenshtein, not something cleverer. Brand and model names are short, mostly ASCII, and the
/// mistakes are transpositions and single wrong letters — the thing edit distance is actually good
/// at. A phonetic match would also catch "Sansung", and would additionally catch "Cannon" for
/// "Canon", but it would flag far more names that are legitimately distinct.</para>
/// </remarks>
public static class NameSimilarity
{
    /// <summary>
    /// Whether <paramref name="candidate"/> looks like a mistyping of <paramref name="existing"/>.
    /// </summary>
    /// <remarks>
    /// The tolerance scales with length because one wrong letter means something different in a
    /// four-letter word than in a twelve-letter one: "Sony" and "Sonu" are probably the same
    /// mistake, while "Ring" and "Ping" are two real companies. Identical names are not "probable
    /// typos" — they are handled by the exact-match path before this is ever asked.
    /// </remarks>
    public static bool IsProbableTypo(string? candidate, string? existing)
    {
        var a = candidate?.Trim();
        var b = existing?.Trim();

        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        if (a.Equals(b, StringComparison.OrdinalIgnoreCase)) return false;

        // Wildly different lengths are different names, and the distance calculation on a long pair
        // is wasted work.
        if (Math.Abs(a.Length - b.Length) > 2) return false;

        // Short names are left alone entirely. One letter is the whole difference between Ring and
        // Ping, or Zoom and Boom — real companies both — so flagging at that length would train
        // people to click past the warning, which is worse than not having one. The threshold
        // widens with length because one wrong letter in twelve is far more likely to be a slip.
        var allowed = Math.Max(a.Length, b.Length) switch
        {
            <= 4 => 0,
            <= 8 => 1,
            _    => 2,
        };

        return allowed > 0 && Distance(a, b) <= allowed;
    }

    /// <summary>
    /// Damerau-Levenshtein edit distance, case-insensitive.
    /// </summary>
    /// <remarks>
    /// <b>Damerau, not plain Levenshtein</b>, because a transposition is the commonest typo there
    /// is and plain edit distance charges two for it. "Olympsu" for "Olympus" is one slipped
    /// keystroke and scored two, which put it past the threshold — found by a test asking whether a
    /// real typo was caught, not by one asking whether a real name was spared.
    /// </remarks>
    public static int Distance(string a, string b)
    {
        a = a.ToLowerInvariant();
        b = b.ToLowerInvariant();

        if (a == b) return 0;
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;

        // A full matrix rather than two rows: the transposition rule needs the row before last,
        // and these names are a dozen characters at most.
        var d = new int[a.Length + 1, b.Length + 1];

        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;

                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);

                // Two adjacent characters swapped: one mistake, not two.
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                    d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
            }
        }

        return d[a.Length, b.Length];
    }
}
