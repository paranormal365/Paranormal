using Ben.Data.Common.Interfaces;
using SkiaSharp;

namespace Ben.Data.WebApi.Services;

/// <summary>
/// Separates a media file's <i>metadata</i> from the bytes that get served.
/// </summary>
/// <remarks>
/// <para>Ben's rule (2026-08-17): metadata for images, video and audio is linked to the record and
/// removed from the media. Nobody but an org Administrator or SuperAdmin can read it, and it is
/// never carried on a serve path.</para>
///
/// <para><b>The original is never destroyed.</b> Uploads keep the bytes exactly as they arrived;
/// what changes is which file gets served. A sanitized derivative is written alongside, and every
/// public read goes to that. This is an investigation platform — a re-encode is irreversible, and a
/// case photo has to stay re-examinable at full fidelity. The extra storage is the price of not
/// destroying evidence.</para>
///
/// <para><b>Images only, for now.</b> Stripping video and audio needs an ffmpeg remux
/// (<c>-map_metadata -1</c>), and ffmpeg is currently reachable only from the sidecar, not from this
/// process — a hosting decision rather than a code change. Non-image uploads therefore pass through
/// untouched here and are served as-is; their metadata is still extracted and stored, so the Admin
/// view is complete from day one and only the stripping half waits.</para>
/// </remarks>
public interface IMediaSanitizationService
{
    /// <summary>True when this content type is one we can currently sanitize (images).</summary>
    bool CanSanitize(string contentType);

    /// <summary>
    /// Produces the sanitized bytes for an image — decoded and re-encoded, which drops EXIF, GPS
    /// and every other embedded tag by construction rather than by trying to enumerate them.
    /// </summary>
    /// <returns>JPEG bytes, resized so the long edge is at most <paramref name="maxLongEdge"/>.</returns>
    /// <exception cref="UnreadableImageException">The bytes are not a decodable image.</exception>
    byte[] Sanitize(byte[] originalBytes, int maxLongEdge = FullSizeLongEdge);

    /// <summary>The conventional path of the sanitized copy that sits beside an original.</summary>
    string SanitizedPathFor(string storagePath);

    /// <summary>The conventional path of the thumbnail that sits beside an original.</summary>
    string ThumbnailPathFor(string storagePath);

    /// <summary>Long edge, in pixels, of the served copy.</summary>
    const int FullSizeLongEdge = 2048;

    /// <summary>Long edge, in pixels, of the thumbnail.</summary>
    const int ThumbnailLongEdge = 400;
}

/// <summary>Thrown when bytes offered as an image cannot be decoded.</summary>
public sealed class UnreadableImageException(string message) : Exception(message);

/// <inheritdoc />
public sealed class MediaSanitizationService : IMediaSanitizationService
{
    // SkiaSharp rather than ImageSharp: MIT, so there is no license-applicability question to keep
    // revisiting as this project's circumstances change. Everything goes through the interface, so
    // swapping the implementation is one file.
    private const int JpegQuality = 85;

    public bool CanSanitize(string contentType)
        => !string.IsNullOrWhiteSpace(contentType)
           && contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
           // SVG is markup, not raster — Skia will not decode it, and it carries no EXIF anyway.
           && !contentType.Contains("svg", StringComparison.OrdinalIgnoreCase);

    public byte[] Sanitize(byte[] originalBytes, int maxLongEdge = IMediaSanitizationService.FullSizeLongEdge)
    {
        // Skia does not politely return null on rubbish input — it throws from inside the decoder
        // (ArgumentNullException on a null codec, among others). Translate anything it throws into
        // the typed exception, so an upload of a text file renamed .jpg becomes a 400 rather than
        // a 500 from deep in a native call.
        SKBitmap? decoded;
        try
        {
            decoded = SKBitmap.Decode(originalBytes);
        }
        catch (Exception ex)
        {
            throw new UnreadableImageException($"The file could not be read as an image: {ex.Message}");
        }

        using var original = decoded
            ?? throw new UnreadableImageException("The file could not be read as an image.");

        var longEdge = Math.Max(original.Width, original.Height);
        using var toEncode = longEdge <= maxLongEdge
            ? original
            : Resize(original, maxLongEdge, longEdge);

        using var image = SKImage.FromBitmap(toEncode);
        // Encoding from decoded pixels writes only pixels: no EXIF block travels across, so this
        // strips by construction rather than by hunting for tags we know the names of.
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, JpegQuality)
            ?? throw new UnreadableImageException("The image could not be re-encoded.");

        return data.ToArray();
    }

    private static SKBitmap Resize(SKBitmap source, int maxLongEdge, int longEdge)
    {
        var scale = (double)maxLongEdge / longEdge;
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));

        var resized = source.Resize(new SKImageInfo(width, height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        return resized ?? throw new UnreadableImageException("The image could not be resized.");
    }

    public string SanitizedPathFor(string storagePath) => storagePath + ".clean.jpg";

    public string ThumbnailPathFor(string storagePath) => storagePath + ".thumb.jpg";
}
