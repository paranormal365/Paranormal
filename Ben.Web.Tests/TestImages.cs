using SkiaSharp;

namespace Ben.Web.Tests;

/// <summary>
/// Real image bytes for tests that go through the media-ingest pipeline.
/// </summary>
/// <remarks>
/// Uploads are validated by actually decoding them now, so a fake "image" of zero bytes is rejected
/// as a bad request — correctly. Tests that only care about attach/detach bookkeeping still need
/// something that genuinely decodes, so they get a real (tiny) JPEG rather than a mock that would
/// quietly stop exercising the pipeline.
/// </remarks>
internal static class TestImages
{
    /// <summary>A small, genuinely decodable JPEG.</summary>
    internal static byte[] Jpeg(int width = 32, int height = 24)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap)) canvas.Clear(SKColors.SlateGray);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 85);
        return data.ToArray();
    }

    internal static byte[] JpegWithGps()
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

    /// <summary>
    /// A correctly-formed TIFF/EXIF APP1 segment carrying a readable GPS position (51°30'N 0°7'E).
    /// </summary>
    /// <remarks>
    /// Offsets here are relative to the TIFF header and hand-computed, because that is what the
    /// format is. Laid out explicitly so the arithmetic can be checked by eye:
    /// <code>
    ///   0  "II", 42, IFD0 offset = 8
    ///   8  IFD0: 1 entry -> GPSInfoIFDPointer = 26
    ///  26  GPS IFD: 4 entries (LatRef, Lat, LonRef, Lon), next = 0
    ///  80  latitude  rationals (3 x 8 bytes)
    /// 104  longitude rationals (3 x 8 bytes)
    /// </code>
    /// An earlier version of this fixture wrote the wrong tag numbers and types: MetadataExtractor
    /// still reported a GPS directory (so a strip assertion passed) while no latitude could be read
    /// from it — which would have let an end-to-end "GPS reached the table" test pass vacuously.
    /// </remarks>
    private static byte[] BuildGpsExifApp1()
    {
        using var body = new MemoryStream();
        using var w = new BinaryWriter(body);

        void U16(ushort v) => w.Write(v);
        void U32(uint v) => w.Write(v);
        void Entry(ushort tag, ushort type, uint count, uint value)
        {
            U16(tag); U16(type); U32(count); U32(value);
        }
        void Rational(uint numerator, uint denominator)
        {
            U32(numerator); U32(denominator);
        }

        w.Write("Exif\0\0"u8.ToArray());
        w.Write("II"u8.ToArray());          // little-endian
        U16(42);                            // TIFF magic
        U32(8);                             // -> IFD0

        U16(1);                             // IFD0: one entry
        Entry(0x8825, 4, 1, 26);            // GPSInfoIFDPointer -> 26
        U32(0);                             // no IFD1

        U16(4);                             // GPS IFD: four entries
        Entry(1, 2, 2, 0x0000004E);         // GPSLatitudeRef  = "N" (fits inline)
        Entry(2, 5, 3, 80);                 // GPSLatitude     -> 80
        Entry(3, 2, 2, 0x00000045);         // GPSLongitudeRef = "E" (fits inline)
        Entry(4, 5, 3, 104);                // GPSLongitude    -> 104
        U32(0);                             // no next IFD

        Rational(51, 1); Rational(30, 1); Rational(0, 1);   // 51 deg 30' 00" N
        Rational(0, 1);  Rational(7, 1);  Rational(0, 1);   //  0 deg 07' 00" E

        var tiff = body.ToArray()[6..];     // everything after the "Exif\0\0" identifier
        var payload = new byte[6 + tiff.Length];
        "Exif\0\0"u8.CopyTo(payload);
        tiff.CopyTo(payload, 6);

        var segment = new byte[4 + payload.Length];
        segment[0] = 0xFF; segment[1] = 0xE1;               // APP1 marker
        var len = payload.Length + 2;                       // length includes the length field
        segment[2] = (byte)(len >> 8); segment[3] = (byte)(len & 0xFF);
        payload.CopyTo(segment, 4);
        return segment;
    }

    /// <summary>
    /// A JPEG carrying DateTimeOriginal, and optionally the OffsetTimeOriginal that says which
    /// timezone that reading was taken in.
    /// </summary>
    internal static byte[] JpegWithCaptureTime(string exifDateTime, string? offset)
    {
        using var bitmap = new SKBitmap(32, 24);
        using (var canvas = new SKCanvas(bitmap)) canvas.Clear(SKColors.DarkSlateGray);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        var plain = data.ToArray();

        var exif = BuildCaptureTimeApp1(exifDateTime, offset);
        var result = new byte[plain.Length + exif.Length];
        plain.AsSpan(0, 2).CopyTo(result);
        exif.CopyTo(result, 2);
        plain.AsSpan(2).CopyTo(result.AsSpan(2 + exif.Length));
        return result;
    }

    private static byte[] BuildCaptureTimeApp1(string exifDateTime, string? offset)
    {
        using var body = new MemoryStream();
        using var w = new BinaryWriter(body);

        void U16(ushort v) => w.Write(v);
        void U32(uint v) => w.Write(v);
        void Entry(ushort tag, ushort type, uint count, uint value)
        {
            U16(tag); U16(type); U32(count); U32(value);
        }

        var dateBytes = System.Text.Encoding.ASCII.GetBytes(exifDateTime + "\0");        // 20 bytes
        var offsetBytes = offset is null ? null : System.Text.Encoding.ASCII.GetBytes(offset + "\0"); // 7 bytes

        w.Write("Exif\0\0"u8.ToArray());
        w.Write("II"u8.ToArray());
        U16(42);
        U32(8);                                  // -> IFD0

        U16(1);                                  // IFD0: one entry
        Entry(0x8769, 4, 1, 26);                 // ExifSubIFDPointer -> 26
        U32(0);

        // SubIFD at 26: DateTimeOriginal (+ OffsetTimeOriginal when supplied)
        var entryCount = (ushort)(offsetBytes is null ? 1 : 2);
        var subIfdSize = 2 + entryCount * 12 + 4;
        var dataStart = (uint)(26 + subIfdSize);

        U16(entryCount);
        Entry(0x9003, 2, (uint)dateBytes.Length, dataStart);                 // DateTimeOriginal
        if (offsetBytes is not null)
            Entry(0x9011, 2, (uint)offsetBytes.Length, dataStart + (uint)dateBytes.Length);  // OffsetTimeOriginal
        U32(0);

        w.Write(dateBytes);
        if (offsetBytes is not null) w.Write(offsetBytes);

        var payload = body.ToArray();
        var segment = new byte[4 + payload.Length];
        segment[0] = 0xFF; segment[1] = 0xE1;
        var len = payload.Length + 2;
        segment[2] = (byte)(len >> 8); segment[3] = (byte)(len & 0xFF);
        payload.CopyTo(segment, 4);
        return segment;
    }
}
