using Ben.Data.Common.Interfaces;
using Ben.Data.WebApi.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The sanitized derivative is what a serve path is supposed to return — and until 2026-08-24
/// exactly one path (equipment photos) actually did, so a public case photo handed an anonymous
/// visitor its EXIF while the map beside it showed a deliberately vague city pin.
/// </summary>
/// <remarks>
/// These pin <see cref="MediaIngestService.ServingPathFor"/>, the choice every serve path now
/// routes through: the stripped copy when it exists, the original when it does not — so wiring a
/// route through it can never break a file that was never sanitized.
/// </remarks>
public sealed class SanitizedCopyIsServedTests
{
    private static MediaIngestService Ingest(params string[] existingPaths)
    {
        var storage = new Mock<IFileStorageService>();
        storage.Setup(s => s.Exists(It.IsAny<string>()))
               .Returns<string>(p => existingPaths.Contains(p, StringComparer.Ordinal));

        return new MediaIngestService(
            storage.Object,
            new FileMetadataExtractorService(),
            new MediaSanitizationService(),
            NullLogger<MediaIngestService>.Instance);
    }

    [Fact]
    public void A_file_with_a_sanitized_copy_serves_the_copy_not_the_original()
    {
        const string original = "cases/abc/photo.jpg";
        var serving = Ingest($"{original}.clean.jpg").ServingPathFor(original);

        Assert.Equal($"{original}.clean.jpg", serving);
        Assert.NotEqual(original, serving);
    }

    [Fact]
    public void A_file_with_no_sanitized_copy_still_serves_the_original()
    {
        // The safety property that makes routing every path through this harmless: video, audio,
        // SVG and everything uploaded before the sanitizer existed keep working exactly as before.
        const string original = "cases/abc/clip.mp4";
        Assert.Equal(original, Ingest().ServingPathFor(original));
    }

    [Fact]
    public void The_sanitizer_covers_raster_images_and_declines_what_it_cannot_strip()
    {
        var sanitizer = new MediaSanitizationService();

        Assert.True(sanitizer.CanSanitize("image/jpeg"));
        Assert.True(sanitizer.CanSanitize("image/png"));

        // Video and audio need an ffmpeg remux that this process cannot do yet — they pass
        // through WITH their metadata, which is the open half of the gap (item 179).
        Assert.False(sanitizer.CanSanitize("video/mp4"));
        Assert.False(sanitizer.CanSanitize("audio/wav"));
        Assert.False(sanitizer.CanSanitize("image/svg+xml"));
    }

    [Fact]
    public void Re_encoding_an_image_drops_its_metadata_rather_than_enumerating_tags_to_remove()
    {
        // A 2x2 JPEG with an EXIF APP1 segment. The point is the mechanism: Skia decodes pixels
        // and re-encodes, so nothing that was not pixels survives — no tag list to keep current.
        var withExif = MinimalJpegWithExif();
        Assert.True(withExif.ContainsSequence("Exif"u8.ToArray()), "the fixture should carry an EXIF marker");

        var cleaned = new MediaSanitizationService().Sanitize(withExif);

        Assert.False(cleaned.ContainsSequence("Exif"u8.ToArray()),
            "re-encoding must not carry the EXIF segment into the served copy");
    }

    /// <summary>A tiny valid JPEG carrying an EXIF APP1 marker.</summary>
    private static byte[] MinimalJpegWithExif()
    {
        // Encode a real 2x2 bitmap, then splice an APP1/Exif segment in after SOI.
        using var bitmap = new SkiaSharp.SKBitmap(2, 2);
        bitmap.SetPixel(0, 0, SkiaSharp.SKColors.Red);
        bitmap.SetPixel(1, 1, SkiaSharp.SKColors.Blue);
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 90);
        var jpeg = data.ToArray();

        var payload = "Exif\0\0MAKE=TestCam;GPS=36.10,-86.79"u8.ToArray();
        var segment = new List<byte> { 0xFF, 0xE1 };
        var length = payload.Length + 2;
        segment.Add((byte)(length >> 8));
        segment.Add((byte)(length & 0xFF));
        segment.AddRange(payload);

        return [.. jpeg[..2], .. segment, .. jpeg[2..]];
    }
}

internal static class ByteSearch
{
    /// <summary>Whether <paramref name="haystack"/> contains <paramref name="needle"/>.</summary>
    public static bool ContainsSequence(this byte[] haystack, byte[] needle)
    {
        for (var i = 0; i + needle.Length <= haystack.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length && match; j++)
                if (haystack[i + j] != needle[j]) match = false;
            if (match) return true;
        }
        return false;
    }
}
