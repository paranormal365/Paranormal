namespace Ben.Web.Website.Library.Kit;

/// <summary>
/// The stand-in a tour gets on a card until it has a photograph of its own.
/// </summary>
/// <remarks>
/// <para>A tour with an empty gallery used to render as a hole — text alone in the nearby results,
/// a lantern on flat black in the home strip — and a row of them read as dead space rather than as
/// a set of walks somebody could book.</para>
///
/// <para>The answer is a plate drawn from the tour's own name, so the same walk is the same colour
/// on every surface, on every visit and on every device, and two walks sitting side by side are
/// not. Shared rather than written twice because the home strip and the nearby results show the
/// same tours to the same reader within one screen of each other, and a tour that changed colour
/// between them would look like two different tours.</para>
/// </remarks>
public static class TourPlate
{
    /// <summary>A stable hue, in degrees, for a tour with no photograph yet.</summary>
    public static int Hue(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return 262;

        // FNV-1a, written out rather than string.GetHashCode(): that one is randomised per
        // process, so the plate would change colour every time the server restarted.
        unchecked
        {
            var hash = 2166136261u;
            foreach (var c in name)
            {
                hash ^= c;
                hash *= 16777619u;
            }
            return (int)(hash % 360u);
        }
    }

    /// <summary>The CSS custom property the plate's stylesheet reads, ready for a style attribute.</summary>
    public static string HueStyle(string? name) => $"--plate-hue:{Hue(name)}deg;";

    /// <summary>Up to two initials from the tour's name, for a plate that shows them.</summary>
    public static string Monogram(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";

        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return words.Length switch
        {
            0 => "?",
            1 => words[0][..Math.Min(2, words[0].Length)].ToUpperInvariant(),
            _ => $"{char.ToUpperInvariant(words[0][0])}{char.ToUpperInvariant(words[^1][0])}",
        };
    }
}
