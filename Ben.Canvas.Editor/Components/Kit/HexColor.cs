namespace Ben.Canvas.Editor.Components.Kit;

// Copied from Ben.Web.Website.Library/Kit/HexColor.cs, because Kit cannot be referenced from
// WebAssembly. Behaviour identical.

/// <summary>
/// Normalises a colour string into the <c>#rrggbb</c> form that <c>&lt;input type="color"&gt;</c>
/// requires.
/// </summary>
/// <remarks>
/// The native colour input shows anything it cannot parse as black, which reads as "the colour was
/// reset", so the stored value gets overwritten the moment the user touches anything else.
/// </remarks>
public static class HexColor
{
    /// <summary>
    /// Returns <paramref name="value"/> as <c>#rrggbb</c>, or <paramref name="fallback"/> when it is
    /// not a colour the native input can display.
    /// </summary>
    public static string Normalize(string? value, string fallback = "#000000")
    {
        var parsed = TryNormalize(value);
        if (parsed is not null) return parsed;

        return TryNormalize(fallback) ?? "#000000";
    }

    /// <summary>Returns the <c>#rrggbb</c> form, or null when the value is not usable.</summary>
    public static string? TryNormalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var text = value.Trim();
        if (text.StartsWith('#')) text = text[1..];

        if (text.Length == 3 && IsHex(text))
            return $"#{text[0]}{text[0]}{text[1]}{text[1]}{text[2]}{text[2]}".ToLowerInvariant();

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

    /// <summary>True when two colour strings mean the same colour, whatever form each is written in.</summary>
    public static bool AreSame(string? left, string? right)
        => string.Equals(TryNormalize(left), TryNormalize(right), StringComparison.OrdinalIgnoreCase);
}
