using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Ben.Data.WebApi.Services;

/// <summary>
/// Turns a human title into the readable part of a URL.
/// </summary>
/// <remarks>
/// <para>Ben's reason, and it is the whole point: <i>"we use the GUID for many of the IDs. That is
/// not human readable."</i> A URL is the part of the product people paste into a message to a
/// friend, and <c>…/e/3f2a9c81-…</c> is a link nobody clicks. Organizations already have a
/// <c>UrlName</c>; this is the same idea for the things inside them.</para>
///
/// <para><b>A slug is a promise.</b> Once something is public, its slug should not change — a
/// renamed page or event with a rewritten URL breaks every link anybody has shared. Generate once,
/// store it, and treat later title edits as leaving the URL alone. Callers own that; this only
/// makes the string.</para>
///
/// <para><b>De-duplication is a suffix, not a renumbering.</b> The second "Ghost Walk" on the same
/// day becomes <c>-2</c>, and it keeps that forever. Recomputing positions on insert would silently
/// rewrite the URLs of things that already existed.</para>
/// </remarks>
public static class UrlSlug
{
    /// <summary>Longest slug we will produce, before any de-duplication suffix.</summary>
    private const int MaxLength = 80;

    private static readonly Regex NotSlugSafe = new("[^a-z0-9]+", RegexOptions.Compiled);

    /// <summary>
    /// A lowercase, hyphenated slug, or null when there was nothing usable to make one from.
    /// </summary>
    /// <remarks>
    /// Accented characters are folded to their base letters rather than stripped, so "Café" becomes
    /// "cafe" rather than "caf" — a URL that silently drops letters reads as a typo.
    /// </remarks>
    public static string? From(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var folded = new StringBuilder();
        foreach (var ch in text.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                folded.Append(ch);

        var slug = NotSlugSafe.Replace(folded.ToString().ToLowerInvariant(), "-").Trim('-');

        if (slug.Length > MaxLength)
            slug = slug[..MaxLength].TrimEnd('-');

        return slug.Length == 0 ? null : slug;
    }

    /// <summary>
    /// A slug prefixed with a date, for things where "which one?" is usually answered by when.
    /// </summary>
    /// <remarks>
    /// Date first so a list of them sorts chronologically by name alone, and so the URL says
    /// something useful even when the title does not. A bare date would be walkable — anybody could
    /// step through the calendar to enumerate what a group has been doing — which is why the title
    /// is part of it rather than a fallback.
    /// </remarks>
    public static string? FromDateAndTitle(DateTime date, string? title)
    {
        var titlePart = From(title);
        var datePart  = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        return titlePart is null ? datePart : $"{datePart}-{titlePart}";
    }

    /// <summary>
    /// Whether this text reads like a street address.
    /// </summary>
    /// <remarks>
    /// <para>Used where a slug describes something at somebody's home. A slug is public text that
    /// ends up in browser histories, referrer headers and pasted links, and
    /// <c>/cases/42-elm-street-hauntings</c> would hand back everything the coordinate redaction was
    /// built to protect.</para>
    ///
    /// <para>A leading number followed by a street word is the shape worth catching — "42 Elm
    /// Street", "1600 Pennsylvania Ave". Deliberately narrow: this refuses a title an organization
    /// typed, so a rule that fired on "The 1892 Mill House" would be a nuisance that teaches people
    /// to work around it. It is a guard against the obvious mistake, not a claim to catch every
    /// address a person could write.</para>
    /// </remarks>
    public static bool LooksLikeAStreetAddress(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        return Regex.IsMatch(
            text,
            @"\b\d{1,6}\s+([A-Za-z'\-]+\s+){0,3}(st|street|rd|road|ave|avenue|ln|lane|dr|drive|blvd|boulevard|ct|court|way|close|terrace|place|pl)\b",
            RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Makes <paramref name="candidate"/> unique against slugs already taken, by suffixing.
    /// </summary>
    /// <param name="candidate">The slug to use if nothing has taken it.</param>
    /// <param name="isTaken">Asked whether a given slug is already in use in the relevant scope.</param>
    public static async Task<string> MakeUniqueAsync(
        string candidate, Func<string, Task<bool>> isTaken)
    {
        if (!await isTaken(candidate)) return candidate;

        // Bounded rather than a while(true): a scope with thousands of identical titles is a sign
        // of something wrong, and spinning forever in a request is a worse answer than a long slug.
        for (var suffix = 2; suffix <= 500; suffix++)
        {
            var attempt = $"{candidate}-{suffix}";
            if (!await isTaken(attempt)) return attempt;
        }

        return $"{candidate}-{Guid.NewGuid().ToString("N")[..6]}";
    }
}
