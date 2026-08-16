using System.Globalization;

namespace Ben.Video.Editor.Effects;

/// <summary>
/// Conversion helpers for RGBA colours stored as packed <c>double</c> values
/// in <see cref="AppliedEffect.Parameters"/>.
///
/// <b>Storage format:</b> The 32-bit ARGB integer is cast to <c>double</c>.
/// All 32-bit unsigned integer values fit exactly in a <c>double</c> (53-bit mantissa).
/// <c>packed = (A &lt;&lt; 24) | (R &lt;&lt; 16) | (G &lt;&lt; 8) | B</c>
///
/// <b>Common defaults:</b>
/// <list type="bullet">
///   <item>Opaque black  — <c>4278190080.0</c>  (#000000FF)</item>
///   <item>Opaque white  — <c>4294967295.0</c>  (#FFFFFFFF)</item>
///   <item>Transparent   — <c>0.0</c>            (#00000000)</item>
/// </list>
/// </summary>
public static class ColorHelper
{
    // ── Preset packed values ──────────────────────────────────────────────────

    /// <summary>Opaque black (#000000FF) as a packed double.</summary>
    public const double OpaqueBlack = 4_278_190_080.0;  // 0xFF000000

    /// <summary>Opaque white (#FFFFFFFF) as a packed double.</summary>
    public const double OpaqueWhite = 4_294_967_295.0;  // 0xFFFFFFFF

    /// <summary>Opaque red (#FF0000FF) as a packed double.</summary>
    public const double OpaqueRed   = 4_278_190_335.0;  // 0xFF0000FF

    // ── Pack / Unpack ─────────────────────────────────────────────────────────

    /// <summary>Pack RGBA byte components into a double.</summary>
    public static double Pack(byte r, byte g, byte b, byte a = 255)
        => (double)(((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | b);

    /// <summary>Unpack to RGBA byte components.</summary>
    public static (byte R, byte G, byte B, byte A) Unpack(double packed)
    {
        var p = (uint)(long)packed;
        return ((byte)(p >> 16), (byte)(p >> 8), (byte)p, (byte)(p >> 24));
    }

    // ── CSS hex string ────────────────────────────────────────────────────────

    /// <summary>
    /// Convert a CSS hex colour string to a packed double.
    /// Accepts: <c>#RGB</c>, <c>#RRGGBB</c>, <c>#RRGGBBAA</c>.
    /// Returns <see cref="OpaqueBlack"/> on parse failure.
    /// </summary>
    public static double FromHex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return OpaqueBlack;
        var h = hex.TrimStart('#');
        try
        {
            return h.Length switch
            {
                3  => Pack(
                    (byte)(Convert.ToByte(h[0..1], 16) * 17),
                    (byte)(Convert.ToByte(h[1..2], 16) * 17),
                    (byte)(Convert.ToByte(h[2..3], 16) * 17)),
                6  => Pack(
                    Convert.ToByte(h[0..2], 16),
                    Convert.ToByte(h[2..4], 16),
                    Convert.ToByte(h[4..6], 16)),
                8  => Pack(
                    Convert.ToByte(h[0..2], 16),
                    Convert.ToByte(h[2..4], 16),
                    Convert.ToByte(h[4..6], 16),
                    Convert.ToByte(h[6..8], 16)),
                _  => OpaqueBlack,
            };
        }
        catch { return OpaqueBlack; }
    }

    /// <summary>
    /// Convert a packed double to a CSS hex string in <c>#RRGGBBAA</c> format,
    /// suitable for <c>TelerikColorGradient Value</c>.
    /// </summary>
    public static string ToHex(double packed)
    {
        var (r, g, b, a) = Unpack(packed);
        return $"#{r:X2}{g:X2}{b:X2}{a:X2}";
    }

    // ── ffmpeg colour string ──────────────────────────────────────────────────

    /// <summary>
    /// Convert a packed double to an ffmpeg colour string.
    /// When <paramref name="includeAlpha"/> is <c>true</c>, returns <c>0xRRGGBBAA</c> —
    /// ffmpeg's colour parser takes alpha LAST (see ffmpeg-utils "Color"), not the
    /// CSS-style <c>0xAARRGGBB</c> this method originally produced. Alpha-first put the
    /// real alpha byte into the red channel and left alpha at the blue byte's value —
    /// for a default #FFFF00B4 callout fill that meant alpha 0x00, an invisible box.
    /// Otherwise returns <c>0xRRGGBB</c>.
    /// </summary>
    public static string ToFfmpegColor(double packed, bool includeAlpha = false)
    {
        var (r, g, b, a) = Unpack(packed);
        return includeAlpha
            ? $"0x{r:X2}{g:X2}{b:X2}{a:X2}"
            : $"0x{r:X2}{g:X2}{b:X2}";
    }

    // ── ffmpeg drawtext colour string ─────────────────────────────────────────

    /// <summary>
    /// Convert a packed double to an ffmpeg <c>drawtext</c> colour string
    /// (<c>RRGGBB@A.AAA</c> — the same syntax as <c>fontcolor</c>/<c>boxcolor</c>).
    /// Note this differs from <see cref="ToFfmpegColor"/>, which produces the
    /// <c>0xRRGGBBAA</c> syntax used by filters like <c>drawbox</c>.
    /// </summary>
    public static string ToDrawtextColor(double packed)
    {
        var ic = CultureInfo.InvariantCulture;
        var (r, g, b, a) = Unpack(packed);
        return $"{r:X2}{g:X2}{b:X2}@{(a / 255.0).ToString("F3", ic)}";
    }

    // ── rgba() string for CSS ─────────────────────────────────────────────────

    /// <summary>
    /// Convert a packed double to a CSS <c>rgba()</c> string.
    /// Useful for rendering a colour preview in Razor markup.
    /// </summary>
    public static string ToRgbaCss(double packed)
    {
        var ic = CultureInfo.InvariantCulture;
        var (r, g, b, a) = Unpack(packed);
        return $"rgba({r},{g},{b},{(a / 255.0).ToString("F3", ic)})";
    }
}
