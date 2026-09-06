namespace Ben.Video.Editor.Models;

/// <summary>How a redacted region is obscured.</summary>
public enum RedactionStyle
{
    /// <summary>Blurred until unreadable.</summary>
    Blur,

    /// <summary>Reduced to large flat blocks.</summary>
    Pixelate,
}

/// <summary>
/// A rectangle of a clip that must not be shown.
/// </summary>
/// <remarks>
/// <para>The one effect this product cannot ship without. Members cut evidence reels from footage
/// shot inside people's homes, and a private engagement's own rule is that what identifies the
/// client or the address does not go out — faces, number plates, house numbers, the letters on the
/// post on the hall table. The editor had a whole-frame Blur and nothing that could obscure part of
/// a picture, so the only way to publish a clip with one identifying detail in it was not to
/// publish it (2026-09-05 audit, the completeness critic's first item).</para>
///
/// <para>Fractions of the frame rather than pixels, so a redaction drawn against a preview still
/// covers the same thing when the export runs at a different resolution. Getting that wrong would
/// move the box off what it was hiding, which is the one failure this feature cannot have.</para>
/// </remarks>
public sealed record RedactionRegion
{
    /// <summary>Left edge, as a fraction of the frame width.</summary>
    public double X { get; set; } = 0.35;

    /// <summary>Top edge, as a fraction of the frame height.</summary>
    public double Y { get; set; } = 0.35;

    /// <summary>Width, as a fraction of the frame width.</summary>
    public double Width { get; set; } = 0.3;

    /// <summary>Height, as a fraction of the frame height.</summary>
    public double Height { get; set; } = 0.3;

    /// <summary>How the region is obscured.</summary>
    public RedactionStyle Style { get; set; } = RedactionStyle.Blur;

    /// <summary>
    /// How heavily, from 1 (light) to 10 (heavy).
    /// </summary>
    /// <remarks>
    /// A scale rather than a filter parameter, because the two styles need different numbers to
    /// look equally obscured and nobody redacting a face should have to know that.
    /// </remarks>
    public double Strength { get; set; } = 6.0;
}
