using Ben.Data.Source.Entities;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.QuickTime;
using NAudio.Wave;
using System.Globalization;
using System.Text.Json;

namespace Ben.Data.WebApi.Services;

/// <summary>
/// Extracts technical metadata from audio, image and video files.
/// Results are stored in UploadFileMetadata (SuperAdmin-only — never returned in public responses).
/// </summary>
public sealed class FileMetadataExtractorService
{
    /// <summary>
    /// Extracts metadata from bytes already held in memory. Prefer the
    /// <see cref="Extract(Guid, string, Stream)"/> overload for uploaded files — it reads the
    /// stored file instead of requiring the whole thing to be resident.
    /// </summary>
    public UploadFileMetadata Extract(Guid uploadFileId, string contentType, byte[] fileBytes)
    {
        using var stream = new MemoryStream(fileBytes, writable: false);
        return Extract(uploadFileId, contentType, stream);
    }

    /// <summary>
    /// Extracts metadata by reading <paramref name="content"/>. The stream must be seekable —
    /// the audio path may rewind and re-read when the first decoder does not recognise the format.
    /// </summary>
    public UploadFileMetadata Extract(Guid uploadFileId, string contentType, Stream content)
    {
        var meta = new UploadFileMetadata
        {
            Id             = Guid.NewGuid(),
            UploadFileId   = uploadFileId,
            ExtractedAtUtc = DateTime.UtcNow,
        };

        try
        {
            if (contentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
            {
                meta.MediaKind = "Audio";
                ExtractAudio(meta, contentType, content);
            }
            else if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                meta.MediaKind = "Image";
                ExtractExifBased(meta, content);
            }
            else if (contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
            {
                meta.MediaKind = "Video";
                ExtractExifBased(meta, content); // MOV/MP4 from phones embed QuickTime atoms with GPS + duration
            }
        }
        catch
        {
            // Extraction is best-effort; never fail the upload
        }

        return meta;
    }

    /// <summary>
    /// Rewinds a seekable stream so the next reader starts at the beginning. Extraction attempts
    /// are sequential fallbacks over the same content, so each one needs its own fresh start.
    /// </summary>
    private static void Rewind(Stream content)
    {
        if (content.CanSeek) content.Position = 0;
    }

    // ── Audio: WAV / MP3 via NAudio; other formats via MetadataExtractor ──────

    private static void ExtractAudio(UploadFileMetadata meta, string contentType, Stream content)
    {
        try
        {
            Rewind(content);
            // Through AudioSourceReader, which picks NLayer's managed decoder for MP3.
            //
            // This used to construct Mp3FileReader directly, and that defaults to the ACM codec —
            // Msacm32.dll, a Windows system library. Off Windows every MP3 threw
            // DllNotFoundException here, the catch below swallowed it, and the file was recorded
            // with no duration, no sample rate and no channel count at all. Silently: an MP3 that
            // had never been measured looked exactly like one that could not be. The site runs on
            // Linux, so that was every MP3 anybody has ever uploaded, and it is why the mixer had
            // no lengths to draw with (2026-09-06 audio audit, phase 4).
            //
            // NAudio's stream constructors leave ownership with the caller, so disposing the reader
            // below does not close `content` — which matters, because the fallback re-reads it.
            using (var reader = Audio.AudioSourceReader.Open(content, contentType))
            {
                meta.DurationSeconds = reader.TotalTime.TotalSeconds;
                meta.SampleRateHz    = reader.WaveFormat.SampleRate;
                meta.Channels        = reader.WaveFormat.Channels;
                meta.BitRateKbps     = reader.WaveFormat.AverageBytesPerSecond * 8 / 1000;
                meta.AudioCodec      = reader.WaveFormat.Encoding.ToString();
            }
            return;
        }
        catch { /* fall through for OGG, FLAC, AAC, M4A, OPUS */ }

        // Try MetadataExtractor for other audio formats (tags only, no duration guarantee)
        TryBuildRawJson(meta, content);
    }

    // ── Images + Phone/Security-camera Video (QuickTime / MP4 containers) ─────

    private static void ExtractExifBased(UploadFileMetadata meta, Stream content)
    {
        IReadOnlyList<MetadataExtractor.Directory> dirs;
        try
        {
            Rewind(content);
            dirs = ImageMetadataReader.ReadMetadata(content);
        }
        catch { return; }

        BuildRawJson(meta, dirs);

        // ── JPEG dimensions ────────────────────────────────────────────────────
        var jpeg = dirs.OfType<MetadataExtractor.Formats.Jpeg.JpegDirectory>().FirstOrDefault();
        if (jpeg is not null)
        {
            if (jpeg.TryGetInt32(MetadataExtractor.Formats.Jpeg.JpegDirectory.TagImageWidth,  out var w)) meta.WidthPixels  = w;
            if (jpeg.TryGetInt32(MetadataExtractor.Formats.Jpeg.JpegDirectory.TagImageHeight, out var h)) meta.HeightPixels = h;
        }

        // ── PNG dimensions ─────────────────────────────────────────────────────
        if (meta.WidthPixels is null)
        {
            var png = dirs.OfType<MetadataExtractor.Formats.Png.PngDirectory>().FirstOrDefault();
            if (png is not null)
            {
                if (png.TryGetInt32(MetadataExtractor.Formats.Png.PngDirectory.TagImageWidth,  out var w)) meta.WidthPixels  = w;
                if (png.TryGetInt32(MetadataExtractor.Formats.Png.PngDirectory.TagImageHeight, out var h)) meta.HeightPixels = h;
            }
        }

        // ── QuickTime movie header (MOV/MP4 from phone or security cam) ────────
        var qtMovie = dirs.OfType<QuickTimeMovieHeaderDirectory>().FirstOrDefault();
        if (qtMovie is not null)
        {
            if (qtMovie.TryGetInt64(QuickTimeMovieHeaderDirectory.TagDuration,  out var durUnits)
             && qtMovie.TryGetInt32(QuickTimeMovieHeaderDirectory.TagTimeScale, out var timeScale)
             && timeScale > 0)
            {
                meta.DurationSeconds = (double)durUnits / timeScale;
            }
        }

        // ── EXIF IFD0 — camera make / model / date ─────────────────────────────
        var ifd0 = dirs.OfType<ExifIfd0Directory>().FirstOrDefault();
        if (ifd0 is not null)
        {
            meta.CameraManufacturer = ifd0.GetString(ExifDirectoryBase.TagMake)?.Trim();
            meta.CameraModel        = ifd0.GetString(ExifDirectoryBase.TagModel)?.Trim();
            if (ifd0.ContainsTag(ExifDirectoryBase.TagDateTime))
                meta.CapturedAtUtc = DateTime.SpecifyKind(ifd0.GetDateTime(ExifDirectoryBase.TagDateTime), DateTimeKind.Local).ToUniversalTime();
        }

        // EXIF SubIFD — DateTimeOriginal is when the shutter fired, which is what we want; IFD0's
        // DateTime can be a later modification.
        //
        // EXIF timestamps carry no timezone of their own. Where the camera recorded the offset
        // (OffsetTimeOriginal, on most phones since ~2016) use it, because it is the truth. Where it
        // did not, we are guessing, and the guess used is the SERVER's timezone — a photo taken
        // abroad and uploaded here shifts by however far apart the two are. The unconverted value
        // survives verbatim in RawMetadataJson either way, so nothing is lost to the guess.
        var subIfd = dirs.OfType<ExifSubIfdDirectory>().FirstOrDefault();
        if (subIfd is not null && subIfd.ContainsTag(ExifDirectoryBase.TagDateTimeOriginal))
        {
            var taken = subIfd.GetDateTime(ExifDirectoryBase.TagDateTimeOriginal);
            var offsetText = subIfd.GetString(ExifDirectoryBase.TagTimeZoneOriginal);

            meta.CapturedAtUtc =
                !string.IsNullOrWhiteSpace(offsetText)
                && TimeSpan.TryParseExact(offsetText.TrimStart('+'), @"hh\:mm", CultureInfo.InvariantCulture, out var offset)
                    ? new DateTimeOffset(taken, offsetText.StartsWith('-') ? -offset : offset).UtcDateTime
                    : DateTime.SpecifyKind(taken, DateTimeKind.Local).ToUniversalTime();
        }

        // ── GPS (EXIF standard — JPEG / PNG / TIFF) ────────────────────────────
        var gpsDir = dirs.OfType<GpsDirectory>().FirstOrDefault();
        if (gpsDir is not null)
        {
            var loc = gpsDir.GetGeoLocation(); // returns GeoLocation? (nullable struct)
            if (loc.HasValue)
            {
                meta.GpsLatitude  = loc.Value.Latitude;
                meta.GpsLongitude = loc.Value.Longitude;
            }
            if (gpsDir.TryGetDouble(GpsDirectory.TagAltitude, out var alt))
                meta.GpsAltitudeMeters = alt;
        }

        // ── QuickTime GPS (iPhone MOV/MP4 — ISO 6709 string atom) ─────────────
        if (meta.GpsLatitude is null)
        {
            var qtMeta = dirs.OfType<QuickTimeMetadataHeaderDirectory>().FirstOrDefault();
            if (qtMeta is not null)
            {
                // TagGpsLocation (14) contains "+36.1627-086.7816+180.000/" style string
                var locStr = qtMeta.GetString(QuickTimeMetadataHeaderDirectory.TagGpsLocation);
                if (locStr is not null) ParseIso6709(meta, locStr);
            }
        }

        // ── QuickTime track dimensions (video resolution) ──────────────────────
        if (meta.WidthPixels is null)
        {
            var qtTrack = dirs.OfType<QuickTimeTrackHeaderDirectory>().FirstOrDefault();
            if (qtTrack is not null)
            {
                if (qtTrack.TryGetInt32(QuickTimeTrackHeaderDirectory.TagWidth,  out var tw)) meta.WidthPixels  = tw;
                if (qtTrack.TryGetInt32(QuickTimeTrackHeaderDirectory.TagHeight, out var th)) meta.HeightPixels = th;
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Parses ISO 6709 location string such as +36.1627-086.7816+180.000/ from iPhone video.</summary>
    private static void ParseIso6709(UploadFileMetadata meta, string loc)
    {
        try
        {
            loc = loc.TrimEnd('/');
            int j = loc.IndexOfAny(['+', '-'], 1);
            if (j < 1) return;
            meta.GpsLatitude = double.Parse(loc[..j], System.Globalization.CultureInfo.InvariantCulture);
            int k = loc.IndexOfAny(['+', '-'], j + 1);
            var lonStr = k > j ? loc[j..k] : loc[j..];
            meta.GpsLongitude = double.Parse(lonStr, System.Globalization.CultureInfo.InvariantCulture);
            if (k > 0 && double.TryParse(loc[k..], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var alt))
                meta.GpsAltitudeMeters = alt;
        }
        catch { /* malformed — skip */ }
    }

    private static void TryBuildRawJson(UploadFileMetadata meta, Stream content)
    {
        try
        {
            Rewind(content);
            BuildRawJson(meta, ImageMetadataReader.ReadMetadata(content));
        }
        catch { }
    }

    private static void BuildRawJson(UploadFileMetadata meta, IReadOnlyList<MetadataExtractor.Directory> dirs)
    {
        var raw = dirs.Select(d => new
        {
            Directory = d.Name,
            Tags      = d.Tags.Select(t => new { t.Name, Value = t.Description }).ToList()
        }).ToList();
        meta.RawMetadataJson = JsonSerializer.Serialize(raw);
    }
}
