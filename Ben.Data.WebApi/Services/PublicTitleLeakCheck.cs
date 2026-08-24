using System.Text.RegularExpressions;

namespace Ben.Data.WebApi.Services;

/// <summary>
/// Whether a case title would leak the client's identity onto public pages (item 176).
/// </summary>
/// <remarks>
/// <para>The pseudonym machinery replaces the client's NAME on every public surface, and
/// addresses are generalized — but the TITLE is free text the org wrote, and several real
/// cases are titled with the client's surname ("Park, Nashville TN"). Publishing such a case
/// carries the name straight past the pseudonym. Found by item 166 W4's anonymous-path audit.</para>
///
/// <para><b>Warn, never block</b>: "Park" is also a word, a street, and a legitimate place
/// name — only the org knows whether their title says the place or the person. The check
/// matches whole words, case-insensitively, and skips name tokens shorter than three
/// characters (initials would flag half the alphabet).</para>
/// </remarks>
public static class PublicTitleLeakCheck
{
    /// <summary>
    /// Warning sentences for this title and pseudonym, or empty when nothing matches.
    /// </summary>
    /// <param name="title">The case title as it would publish.</param>
    /// <param name="pseudonym">The public pseudonym — checked against the real name only,
    /// because a pseudonym built from the real surname ("The Park Family" for the Parks)
    /// defeats itself. The dev seed shipped exactly this mistake.</param>
    /// <param name="clientNames">The client's names — first, last, display; nulls tolerated.</param>
    /// <param name="streetAddress">The case's street line; the house-number+street match.</param>
    public static IReadOnlyList<string> Check(
        string? title, string? pseudonym, IEnumerable<string?> clientNames, string? streetAddress)
    {
        var warnings = new List<string>();

        var nameTokens = clientNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .SelectMany(n => n!.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(t => t.Length >= 3)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var token in nameTokens)
        {
            if (ContainsWord(title, token))
            {
                warnings.Add(
                    $"The title contains \"{token}\", which matches the client's name. The pseudonym "
                    + "hides their name everywhere else — a title carries it straight onto the public "
                    + "page. Consider naming the place instead.");
            }
            if (ContainsWord(pseudonym, token))
            {
                warnings.Add(
                    $"The pseudonym contains \"{token}\" — the client's real name. A pseudonym "
                    + "built from the name it exists to hide protects nothing. Pick an unrelated one.");
            }
        }

        if (!string.IsNullOrWhiteSpace(streetAddress))
        {
            // The street line minus its house number: "1428 Elm Street" → "Elm Street". The
            // number alone is meaningless; the named street in a title is the leak.
            var street = Regex.Replace(streetAddress.Trim(), @"^\s*\d+[\s\-]*", "").Trim();
            if (street.Length >= 4 && ContainsWord(title, street))
            {
                warnings.Add(
                    $"The title contains the street (\"{street}\"). Public pages show the area, "
                    + "never the address — except where the title says it. Consider the neighborhood "
                    + "or a landmark instead.");
            }
        }

        return warnings;
    }

    private static bool ContainsWord(string? text, string word)
        => !string.IsNullOrWhiteSpace(text)
        && Regex.IsMatch(text, $@"\b{Regex.Escape(word)}\b", RegexOptions.IgnoreCase);
}
