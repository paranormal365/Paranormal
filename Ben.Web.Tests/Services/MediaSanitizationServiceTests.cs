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
    private static bool HasAnyExifOrGps(byte[] jpeg)
    {
        using var stream = new MemoryStream(jpeg);
        var directories = ImageMetadataReader.ReadMetadata(stream);
        return directories.Any(d => d is GpsDirectory or ExifIfd0Directory or ExifSubIfdDirectory);
    }

    [Fact]
    public void TheFixtureItselfCarriesAReadableGpsPosition_OtherwiseTheStripTestProvesNothing()
    {
        // Guards the guard, twice over. A GPS *directory* is not enough: an earlier version of this
        // fixture produced one that no latitude could be read from, which would have let an
        // end-to-end "GPS reached the table" assertion pass on a file that never really had any.
        using var stream = new MemoryStream(TestImages.JpegWithGps());
        var gps = ImageMetadataReader.ReadMetadata(stream).OfType<GpsDirectory>().Single();

        Assert.NotNull(gps.GetGeoLocation());
    }

    [Fact]
    public void SanitizingAnImageRemovesItsExifAndGps()
    {
        var sanitized = _service.Sanitize(TestImages.JpegWithGps());

        Assert.False(HasAnyExifOrGps(sanitized));
    }

    [Fact]
    public void SanitizingLeavesTheOriginalBytesUntouched()
    {
        // The original is the evidence; only the served copy is rewritten.
        var original = TestImages.JpegWithGps();
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

    /// <summary>
    /// Capture time is the field an investigation actually cares about, and EXIF stores it without
    /// a timezone. Where the camera recorded the offset we must use it rather than assuming the
    /// server's — a photo taken abroad would otherwise land hours from when it was really taken.
    /// </summary>
    [Fact]
    public void CaptureTimeUsesTheOffsetTheCameraRecorded()
    {
        var extractor = new FileMetadataExtractorService();

        // Shot at 21:30 with the camera set to +05:00 — so 16:30 UTC, wherever this test runs.
        var meta = extractor.Extract(
            Guid.NewGuid(), "image/jpeg", TestImages.JpegWithCaptureTime("2026:03:14 21:30:00", "+05:00"));

        Assert.NotNull(meta.CapturedAtUtc);
        Assert.Equal(new DateTime(2026, 3, 14, 16, 30, 0, DateTimeKind.Utc), meta.CapturedAtUtc!.Value);
    }

    [Fact]
    public void CaptureTimeIsStillRecordedWhenTheCameraGaveNoOffset()
    {
        var extractor = new FileMetadataExtractorService();

        var meta = extractor.Extract(
            Guid.NewGuid(), "image/jpeg", TestImages.JpegWithCaptureTime("2026:03:14 21:30:00", offset: null));

        // No offset to trust, so the server's is assumed — recorded rather than dropped, and the
        // untouched original value stays in RawMetadataJson.
        Assert.NotNull(meta.CapturedAtUtc);
        Assert.Contains("2026", meta.RawMetadataJson!);
    }

    [Fact]
    public void DerivativePathsSitBesideTheOriginal()
    {
        Assert.Equal("users/abc/photo.jpg.clean.jpg", _service.SanitizedPathFor("users/abc/photo.jpg"));
        Assert.Equal("users/abc/photo.jpg.thumb.jpg", _service.ThumbnailPathFor("users/abc/photo.jpg"));
    }
}
