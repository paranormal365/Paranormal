using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Ben's rule (2026-08-24): <b>EXIF comes off on ANY upload</b>, and what came off is kept in a
/// table linked to the file's record. Derived files — clips, edits, copies — carry the source's
/// capture details forward, because an encoder writes no EXIF and losing where a recording was
/// made is not an acceptable answer to "where did this clip come from?".
/// </summary>
public sealed class UploadMetadataCoverageTests
{
    private static BenDataContext NewDb() =>
        new(new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static MediaIngestService Ingest() =>
        new(new Mock<IFileStorageService>().Object,
            new FileMetadataExtractorService(),
            new MediaSanitizationService(),
            NullLogger<MediaIngestService>.Instance);

    // ── Derived files carry the recording's place forward ────────────────────

    [Fact]
    public async Task A_clip_inherits_where_and_when_the_recording_was_made()
    {
        await using var db = NewDb();
        var sourceId = Guid.NewGuid();
        db.UploadFileMetadata.Add(new UploadFileMetadata
        {
            Id = Guid.NewGuid(), UploadFileId = sourceId, MediaKind = "Audio",
            GpsLatitude = 36.1043, GpsLongitude = -86.7930, GpsAltitudeMeters = 182.4,
            CapturedAtUtc = new DateTime(2026, 8, 14, 2, 15, 0, DateTimeKind.Utc),
            CameraManufacturer = "Zoom", CameraModel = "H6",
            DurationSeconds = 600, RawMetadataJson = "[{\"Directory\":\"source\"}]",
            ExtractedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var derivedId = Guid.NewGuid();
        var derived = await Ingest().DeriveMetadataAsync(db, sourceId, derivedId, "Audio", default);

        Assert.NotNull(derived);
        Assert.Equal(36.1043, derived!.GpsLatitude);
        Assert.Equal(-86.7930, derived.GpsLongitude);
        Assert.Equal(182.4, derived.GpsAltitudeMeters);
        Assert.Equal(new DateTime(2026, 8, 14, 2, 15, 0, DateTimeKind.Utc), derived.CapturedAtUtc);
        Assert.Equal("Zoom", derived.CameraManufacturer);
        Assert.Equal("H6", derived.CameraModel);

        // Said plainly rather than implied: these were carried, not measured off the clip.
        Assert.Equal(sourceId, derived.InheritedFromUploadFileId);
    }

    [Fact]
    public async Task What_belongs_to_the_new_bytes_is_not_inherited()
    {
        await using var db = NewDb();
        var sourceId = Guid.NewGuid();
        db.UploadFileMetadata.Add(new UploadFileMetadata
        {
            Id = Guid.NewGuid(), UploadFileId = sourceId, MediaKind = "Audio",
            DurationSeconds = 600, SampleRateHz = 48000, Channels = 2,
            WidthPixels = 1920, HeightPixels = 1080,
            RawMetadataJson = "[{\"Directory\":\"the source, not this file\"}]",
            ExtractedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var derived = await Ingest().DeriveMetadataAsync(db, sourceId, Guid.NewGuid(), "Audio", default);

        // A thirty-second clip of a ten-minute recording is thirty seconds — the caller sets that
        // from what it actually produced, so the source's figures must not be carried.
        Assert.NotNull(derived);
        Assert.Null(derived!.DurationSeconds);
        Assert.Null(derived.SampleRateHz);
        Assert.Null(derived.WidthPixels);
        // The raw dump describes a file this is not.
        Assert.Null(derived.RawMetadataJson);
    }

    [Fact]
    public async Task A_source_with_no_metadata_row_yields_nothing_to_carry()
    {
        await using var db = NewDb();
        Assert.Null(await Ingest().DeriveMetadataAsync(db, Guid.NewGuid(), Guid.NewGuid(), "Audio", default));
    }

    // ── Coverage: every upload door records metadata ─────────────────────────

    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!;
    }

    private static string StripComments(string source)
    {
        var s = Regex.Replace(source, @"(?<![\w""'])/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        return string.Join('\n', s.Split('\n').Select(line =>
        {
            var slashes = line.IndexOf("//", StringComparison.Ordinal);
            return slashes >= 0 ? line[..slashes] : line;
        }));
    }

    /// <summary>
    /// A controller that mints an <c>UploadFile</c> must also record its metadata — either freshly
    /// extracted (<c>IngestAsync</c>) or carried from a source (<c>DeriveMetadataAsync</c>).
    /// </summary>
    /// <remarks>
    /// Structural rather than behavioural on purpose: the failure this prevents is a NEW upload
    /// door added later that quietly stores raw bytes, which is exactly how case evidence — the
    /// most sensitive upload on the site — went years without extracting anything.
    /// </remarks>
    [Fact]
    public void Every_controller_that_creates_an_upload_file_also_records_its_metadata()
    {
        var controllers = Directory.EnumerateFiles(
            Path.Combine(RepoRoot().FullName, "Ben.Data.WebApi", "Controllers"), "*.cs", SearchOption.AllDirectories);

        var offenders = new List<string>();
        foreach (var file in controllers)
        {
            var text = StripComments(File.ReadAllText(file));
            if (!text.Contains("new UploadFile\n", StringComparison.Ordinal)
                && !text.Contains("new UploadFile\r\n", StringComparison.Ordinal)
                && !text.Contains("new UploadFile {", StringComparison.Ordinal)
                && !text.Contains("new UploadFile(", StringComparison.Ordinal)) continue;

            var records = text.Contains("UploadFileMetadata.Add", StringComparison.Ordinal)
                       || text.Contains("DeriveMetadataAsync", StringComparison.Ordinal)
                       || text.Contains("IngestAsync", StringComparison.Ordinal);
            if (!records) offenders.Add(Path.GetFileName(file));
        }

        Assert.True(offenders.Count == 0,
            "These controllers create an UploadFile without recording its metadata:\n  "
            + string.Join("\n  ", offenders)
            + "\n\nEvery upload strips EXIF and keeps what came off in UploadFileMetadata "
            + "(Ben's rule, 2026-08-24). Use IMediaIngestService.IngestAsync for a real upload, "
            + "or DeriveMetadataAsync when the file is a clip, edit or copy of another.");
    }
}
