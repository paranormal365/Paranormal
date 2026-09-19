using Ben.Data.Source.Entities;
using System.Text;

namespace Ben.Data.WebApi.Services.Places;

/// <summary>
/// Whether two places are probably the same place.
/// </summary>
/// <remarks>
/// <para><b>The rule: the same address, and less than a tenth of a mile apart.</b> Both, not
/// either.</para>
///
/// <para>The conjunction is what makes it safe to act on. A hotel or an apartment block is one
/// address with many units, so the address text alone cannot separate them; and proximity alone
/// would happily match next door. Requiring the address to agree <i>and</i> the map to agree means
/// the rule only speaks up when both do.</para>
///
/// <para>A tenth of a mile rather than something tighter because geocoders routinely disagree by a
/// building or two, and a stricter radius would miss matches that are obviously the same place to
/// a human. Nothing here merges anything — it only finds candidates to offer.</para>
/// </remarks>
public static class PlaceMatcher
{
    /// <summary>How far apart two records of the same place may plausibly sit.</summary>
    public const double MatchRadiusMiles = 0.1;

    /// <summary>
    /// Whether <paramref name="existing"/> is plausibly the place being described.
    /// </summary>
    /// <remarks>
    /// Two ways to qualify, and both require the proximity check:
    /// a matching street address, or — for a landmark with no street address — a matching name.
    /// </remarks>
    public static bool IsProbableMatch(
        Place existing,
        string? street, string? city, string? state, string? zip, string? name,
        decimal? latitude, decimal? longitude)
    {
        if (!WithinRadius(existing, latitude, longitude)) return false;

        var addressGiven = !string.IsNullOrWhiteSpace(street);
        if (addressGiven && AddressMatches(existing, street, city, state, zip)) return true;

        // Landmarks often have a name and coordinates and no street address at all, so the name is
        // the only text there is to compare.
        return !string.IsNullOrWhiteSpace(name) && NameMatches(existing, name);
    }

    /// <summary>
    /// Within a tenth of a mile — or unknown, when either side has no coordinates.
    /// </summary>
    /// <remarks>
    /// Unknown counts as "close enough to offer" rather than "not a match": a place nobody could
    /// geocode is exactly the kind that gets typed in twice, and the person is being shown the
    /// candidate, not having it applied to them.
    /// </remarks>
    private static bool WithinRadius(Place existing, decimal? latitude, decimal? longitude)
    {
        if (existing.Latitude is null || existing.Longitude is null) return true;
        if (latitude is null || longitude is null) return true;

        return DistanceMiles(
            (double)existing.Latitude.Value, (double)existing.Longitude.Value,
            (double)latitude.Value, (double)longitude.Value) < MatchRadiusMiles;
    }

    private static bool AddressMatches(Place existing, string? street, string? city, string? state, string? zip)
    {
        if (Normalise(existing.StreetAddress1) != Normalise(street)) return false;

        // City, state and postcode only have to agree when both sides state them. Half the rows in
        // this database came from a backfill and are missing one or another, and demanding a full
        // match would find nothing.
        return AgreesIfBothPresent(existing.City, city)
            && AgreesIfBothPresent(existing.State, state)
            && AgreesIfBothPresent(existing.ZipCode, zip);
    }

    /// <summary>
    /// The same name after normalising — or near enough that it is probably a mistyping.
    /// </summary>
    /// <remarks>
    /// <para>Exact-after-normalising already handled case, punctuation and a leading "the". What it
    /// could not handle was a slipped keystroke: "Bell Witch Cav" found nothing, and the second row
    /// got created. Letting <see cref="NameSimilarity"/> have the near-misses closes that.</para>
    ///
    /// <para><b>Safe here in a way it would not be everywhere.</b> This only ever <i>offers</i> a
    /// candidate for a human to accept or ignore, and it has already passed the proximity check — so
    /// a wrong suggestion costs a glance, while a missed one costs a duplicate place that somebody
    /// has to merge later.</para>
    /// </remarks>
    /// <summary>
    /// The looser rule a PUBLIC location's archive uses: same spot, and one name inside the other.
    /// </summary>
    /// <remarks>
    /// <para><b>Why the ordinary rule is not enough here.</b> Two people describing one landmark
    /// write "Bell Witch Cave" and "Bell Witch Cave, Adams", or "430 Keysburg Rd" and "430
    /// Keysburg Road". <see cref="IsProbableMatch"/> rejects both — the names differ by more than
    /// a typo and the street types are different strings — and the archive then splits one cave
    /// into two pages that each look like nobody has ever been there. Splitting is the failure
    /// that destroys the feature; pooling two neighbouring landmarks merely annoys.</para>
    ///
    /// <para><b>Deliberately not applied to private residences.</b> Merging two homes on one
    /// street because their names overlap would put one family's readings on another's record.
    /// The caller checks the kind; this method only answers the geometry-and-name question.</para>
    ///
    /// <para>Containment rather than similarity, because the realistic duplicate is one person
    /// adding the town, the county, or "(cave)" to a name somebody else typed plainly.</para>
    /// </remarks>
    public static bool IsProbableArchiveMatch(
        Place existing, string? name, decimal? latitude, decimal? longitude)
    {
        // Coordinates on BOTH sides, unlike the ordinary rule: "unknown counts as close enough"
        // is right when a human is being offered a candidate to confirm, and wrong when the match
        // is applied without anybody looking at it.
        if (existing.Latitude is null || existing.Longitude is null) return false;
        if (latitude is null || longitude is null) return false;
        if (DistanceMiles(
                (double)existing.Latitude.Value, (double)existing.Longitude.Value,
                (double)latitude.Value, (double)longitude.Value) >= MatchRadiusMiles)
            return false;

        var a = NormaliseName(existing.Name);
        var b = NormaliseName(name);
        if (a.Length == 0 || b.Length == 0) return false;

        return a == b || a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal);
    }

    private static bool NameMatches(Place existing, string? name)
        => NormaliseName(existing.Name) is { Length: > 0 } a
        && NormaliseName(name) is { Length: > 0 } b
        && (a == b || NameSimilarity.IsProbableTypo(b, a));

    private static bool AgreesIfBothPresent(string? a, string? b)
        => string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b) || Normalise(a) == Normalise(b);

    /// <summary>Lowercase, punctuation dropped, internal whitespace collapsed.</summary>
    public static string Normalise(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var builder = new StringBuilder(value.Length);
        var lastWasSpace = false;

        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
                lastWasSpace = false;
            }
            else if (!lastWasSpace && builder.Length > 0)
            {
                // Punctuation collapses to a single space rather than vanishing, so "4512 Belmont
                // Blvd." and "4512 Belmont Blvd" agree while "abc" and "a bc" still differ.
                builder.Append(' ');
                lastWasSpace = true;
            }
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>As <see cref="Normalise"/>, and drops a leading "the".</summary>
    /// <remarks>Enough to match "The Bell Witch Cave" against "Bell Witch Cave", which is the
    /// duplicate this codebase actually produced.</remarks>
    public static string NormaliseName(string? value)
    {
        var normalised = Normalise(value);
        return normalised.StartsWith("the ", StringComparison.Ordinal)
            ? normalised[4..]
            : normalised;
    }

    /// <summary>
    /// Distance in miles. Equirectangular approximation — accurate to well under a metre at these
    /// separations, and this only ever compares against a tenth of a mile.
    /// </summary>
    public static double DistanceMiles(double lat1, double lon1, double lat2, double lon2)
    {
        const double earthRadiusMiles = 3958.8;

        var meanLatRadians = (lat1 + lat2) / 2 * Math.PI / 180;
        var dLat = (lat2 - lat1) * Math.PI / 180;
        // Longitude degrees narrow towards the poles, so scale by the cosine of the latitude.
        var dLon = (lon2 - lon1) * Math.PI / 180 * Math.Cos(meanLatRadians);

        return Math.Sqrt(dLat * dLat + dLon * dLon) * earthRadiusMiles;
    }
}
