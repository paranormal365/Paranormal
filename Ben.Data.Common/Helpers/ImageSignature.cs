namespace Ben.Data.Common.Helpers;

/// <summary>
/// Whether a file's leading bytes are one of the raster formats a browser can draw.
/// </summary>
/// <remarks>
/// <para><b>Why signatures and not a decoder.</b> An upload's file name and its content type are
/// both guesses — the browser derives the second from the first — so neither says what the bytes
/// actually are. The obvious alternative, "try to decode it", is stricter and worse: the decoder
/// in use here refuses some images browsers render perfectly well (a valid 8x8 RGBA PNG among
/// them, found while testing this), so a decode gate rejects files that would have displayed.</para>
///
/// <para>The question worth asking is narrower and answerable from the first twelve bytes: is
/// this one of the formats an <c>&lt;img&gt;</c> can show? A file that fails here cannot be
/// displayed by anything, whatever it is named.</para>
///
/// <para><b>HEIC is the case that motivated this</b> (Ben, 2026-08-27). iPhones shoot HEIC, a
/// copied photo keeps those bytes while picking up a <c>.JPG</c> name, and every name-based check
/// waves it through. It is stored, served back as <c>image/jpeg</c>, and renders nowhere — an
/// upload that reports success and produces a broken picture, which is worse than a refusal.
/// HEIC is deliberately NOT in the accepted list: no major browser decodes it in an
/// <c>&lt;img&gt;</c>, so accepting it would only move the failure later.</para>
/// </remarks>
public static class ImageSignature
{
    /// <summary>How many leading bytes are needed to decide. WebP needs all twelve.</summary>
    public const int BytesNeeded = 12;

    /// <summary>
    /// True when <paramref name="head"/> begins with a raster format browsers can display.
    /// </summary>
    /// <param name="head">The file's leading bytes — at least <see cref="BytesNeeded"/> of them.</param>
    public static bool IsBrowserDisplayable(ReadOnlySpan<byte> head)
    {
        // JPEG — every variant starts FF D8 FF.
        if (Starts(head, [0xFF, 0xD8, 0xFF])) return true;

        // PNG — the eight-byte signature, including the CR/LF pair that catches naive transfers.
        if (Starts(head, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])) return true;

        // GIF — "GIF87a" and "GIF89a" share the first four bytes.
        if (Starts(head, "GIF8"u8)) return true;

        // BMP — rare from a camera, trivially displayable, and cheap to allow.
        if (Starts(head, "BM"u8)) return true;

        // WebP is a RIFF container: "RIFF" then four size bytes then "WEBP". The size sits in the
        // middle, which is why this needs twelve bytes rather than four.
        if (head.Length >= 12 && Starts(head, "RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8))
            return true;

        return false;
    }

    private static bool Starts(ReadOnlySpan<byte> head, ReadOnlySpan<byte> prefix)
        => head.Length >= prefix.Length && head[..prefix.Length].SequenceEqual(prefix);
}
