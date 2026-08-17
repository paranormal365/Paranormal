using Ben.Data.WebApi.Services;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using SkiaSharp;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The metadata-separation guarantee: an uploaded image's EXIF — GPS above all — must not survive
/// into the copy that gets served (item #55 phase 6a, Ben's rule that metadata is linked to the
/// record and removed from the media).
/// </summary>
/// <remarks>
/// These build a real JPEG with real EXIF and read the result back with MetadataExtractor, the same
/// library the extraction service uses. Asserting "no GPS in the output" against a genuine
/// GPS-carrying input is the only version of this test worth having — a hand-rolled fake would
/// prove the fake had no GPS.
/// </remarks>
public class MediaSanitizationServiceTests
{
    private readonly MediaSanitizationService _service = new();

    /// <summary>
    /// A real JPEG carrying a GPS EXIF block, assembled by splicing an APP1 segment into Skia's
    /// encoder output — Skia can decode EXIF but will not write it, so the fixture has to be built.
    /// </summary>
    private static byte[] JpegWithGps()
    {
        using var bitmap = new SKBitmap(64, 48);
        using (var canvas = new SKCanvas(bitmap)) canvas.Clear(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        var plain = data.ToArray();

        var exif = BuildGpsExifApp1();

        // Splice the APP1 segment immediately after SOI (the first two bytes).
        var result = new byte[plain.Length + exif.Length];
        plain.AsSpan(0, 2).CopyTo(result);
        exif.CopyTo(result, 2);
        plain.AsSpan(2).CopyTo(result.AsSpan(2 + exif.Length));
        return result;
    }

    /// <summary>Minimal TIFF/EXIF structure carrying one GPS IFD with a latitude and longitude.</summary>
    private static byte[] BuildGpsExifApp1()
    {
        using var body = new MemoryStream();
        using var w = new BinaryWriter(body);

        void U16(ushort v) => w.Write(v);
        void U32(uint v) => w.Write(v);

        w.Write("Exif\0\0"u8.ToArray());       // APP1 identifier
        var tiffStart = body.Position;
        w.Write("II"u8.ToArray());              // little-endian
        U16(42);                                // TIFF magic
        U32(8);                                 // offset to IFD0

        // IFD0: one entry pointing at the GPS IFD
        U16(1);
        U16(0x8825); U16(4); U32(1); U32(26);   // GPSInfoIFDPointer → offset 26
        U32(0);                                 // no IFD1

        // GPS IFD at offset 26: two rational triplets
        U16(2);
        U16(2); U16(2); U32(2); U32(0x004E0000);            // GPSLatitudeRef = "N"
        U16(4); U16(5); U32(3); U32(74);                    // GPSLongitude → offset 74
        U32(0);

        while (body.Position - tiffStart < 74) w.Write((byte)0);
        // 3 rationals: 51/1, 30/1, 0/1
        U32(51); U32(1); U32(30); U32(1); U32(0); U32(1);

        var tiff = body.ToArray();
        var segment = new byte[4 + tiff.Length];
        segment[0] = 0xFF; segment[1] = 0xE1;                        // APP1 marker
        var len = tiff.Length + 2;
        segment[2] = (byte)(len >> 8); segment[3] = (byte)(len & 0xFF);
        tiff.CopyTo(segment, 4);
        return segment;
    }

    private static bool HasAnyExifOrGps(byte[] jpeg)
    {
        using var stream = new MemoryStream(jpeg);
        var directories = ImageMetadataReader.ReadMetadata(stream);
        return directories.Any(d => d is GpsDirectory or ExifIfd0Directory or ExifSubIfdDirectory);
    }

    [Fact]
    public void TheFixtureItselfCarriesExif_OtherwiseTheStripTestProvesNothing()
    {
        // Guards the guard: if this ever stops being true, the strip assertion below is vacuous.
        Assert.True(HasAnyExifOrGps(JpegWithGps()));
    }

    [Fact]
    public void SanitizingAnImageRemovesItsExifAndGps()
    {
        var sanitized = _service.Sanitize(JpegWithGps());

        Assert.False(HasAnyExifOrGps(sanitized));
    }

    [Fact]
    public void SanitizingLeavesTheOriginalBytesUntouched()
    {
        // The original is the evidence; only the served copy is rewritten.
        var original = JpegWithGps();
        var before = original.ToArray();

        _service.Sanitize(original);

        Assert.Equal(before, original);
    }

    [Fact]
    public void AnOversizedImageIsScaledDownToTheLongEdge()
    {
        using var big = new SKBitmap(4000, 3000);
        using (var canvas = new SKCanvas(big)) canvas.Clear(SKColors.Gray);
        using var image = SKImage.FromBitmap(big);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);

        var sanitized = _service.Sanitize(data.ToArray(), maxLongEdge: 2048);

        using var result = SKBitmap.Decode(sanitized);
        Assert.Equal(2048, Math.Max(result.Width, result.Height));
        Assert.Equal(1536, Math.Min(result.Width, result.Height));   // aspect ratio preserved
    }

    [Fact]
    public void ASmallImageIsNotUpscaled()
    {
        using var small = new SKBitmap(120, 80);
        using (var canvas = new SKCanvas(small)) canvas.Clear(SKColors.Gray);
        using var image = SKImage.FromBitmap(small);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);

        var sanitized = _service.Sanitize(data.ToArray());

        using var result = SKBitmap.Decode(sanitized);
        Assert.Equal(120, result.Width);
        Assert.Equal(80, result.Height);
    }

    [Fact]
    public void SomethingThatIsNotAnImageIsRejected()
    {
        var notAnImage = "This is a text file pretending to be a photo."u8.ToArray();

        Assert.Throws<UnreadableImageException>(() => _service.Sanitize(notAnImage));
    }

    [Theory]
    [InlineData("image/jpeg", true)]
    [InlineData("image/png", true)]
    [InlineData("image/heic", true)]
    [InlineData("image/svg+xml", false)]   // markup, not raster — nothing to strip
    [InlineData("video/mp4", false)]       // needs an ffmpeg remux, a later phase
    [InlineData("audio/mpeg", false)]
    [InlineData("", false)]
    public void OnlyRasterImagesAreSanitizableForNow(string contentType, bool expected)
    {
        Assert.Equal(expected, _service.CanSanitize(contentType));
    }

    [Fact]
    public void DerivativePathsSitBesideTheOriginal()
    {
        Assert.Equal("users/abc/photo.jpg.clean.jpg", _service.SanitizedPathFor("users/abc/photo.jpg"));
        Assert.Equal("users/abc/photo.jpg.thumb.jpg", _service.ThumbnailPathFor("users/abc/photo.jpg"));
    }
}
