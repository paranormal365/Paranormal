using System.Globalization;

namespace Ben.Web.Website.Library.Kit;

/// <summary>
/// Normalises a colour string into the <c>#rrggbb</c> form that <c>&lt;input type="color"&gt;</c>
/// requires.
/// </summary>
/// <remarks>
/// The native colour input is unforgiving in a way the Telerik picker was not: anything it cannot
/// parse — an empty value, a named colour, a shorthand <c>#abc</c>, an <c>rgba()</c> — is silently
/// shown as black. That reads as "the colour was reset" rather than as an error, so the stored
/// value gets overwritten with black the moment the user touches anything else on the form.
/// Normalising here means an unrecognised value falls back to a caller-chosen default instead.
/// </remarks>
public static class HexColor
{
    /// <summary>
    /// Returns <paramref name="value"/> as <c>#rrggbb</c>, or <paramref name="fallback"/> when it
    /// is not a colour the native input can display.
    /// </summary>
    public static string Normalize(string? value, string fallback = "#000000")
    {
        var parsed = TryNormalize(value);
        if (parsed is not null) return parsed;

        // A bad fallback would put us back where we started, so it is normalised too.
        return TryNormalize(fallback) ?? "#000000";
    }

    /// <summary>Returns the <c>#rrggbb</c> form, or null when the value is not usable.</summary>
    public static string? TryNormalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var text = value.Trim();
        if (text.StartsWith('#')) text = text[1..];

        // #abc is legal CSS and means #aabbcc, but the input will not take the short form.
        if (text.Length == 3 && IsHex(text))
            return $"#{text[0]}{text[0]}{text[1]}{text[1]}{text[2]}{text[2]}".ToLowerInvariant();

        // #aabbccdd carries alpha, which the native control cannot represent; keep the colour and
        // drop the alpha rather than rejecting the value outright.
        if (text.Length == 8 && IsHex(text))
            return $"#{text[..6]}".ToLowerInvariant();

        if (text.Length == 6 && IsHex(text))
            return $"#{text}".ToLowerInvariant();

        return null;
    }

    private static bool IsHex(ReadOnlySpan<char> text)
    {
        foreach (var c in text)
            if (!Uri.IsHexDigit(c)) return false;
        return true;
    }

    /// <summary>
    /// True when two colour strings mean the same colour, whatever form each is written in. Used
    /// to avoid reporting a change when normalisation alone altered the text.
    /// </summary>
    public static bool AreSame(string? left, string? right)
        => string.Equals(TryNormalize(left), TryNormalize(right), StringComparison.OrdinalIgnoreCase);
}
