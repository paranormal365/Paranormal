using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.FieldSessions;
using Ben.Data.WebApi.Services.Files;
using Ben.Web.Tests.Controllers;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Certificates of authenticity for clips made from a field session's recordings (10/28/2026).
/// </summary>
/// <remarks>
/// Ben: an enhanced recording "should retain verification that it is from the original, but
/// enhanced... not AI or edited externally." These hold the chain from the phone's sealed recording
/// through the scanner's copy to each clip and edit, what counts as enhanced, where it sits in the
/// recording, and that a changed file or a changed certificate no longer checks.
/// </remarks>
public sealed class FileProvenanceTests
{
    private static readonly Guid Guest = Guid.NewGuid();
    private static readonly Guid Organizer = Guid.NewGuid();
    private static readonly Guid OtherMember = Guid.NewGuid();
    private static readonly Guid Group = Guid.NewGuid();
    private static readonly Guid Hosted = Guid.NewGuid();
    private static readonly DateTime Now = DateTime.UtcNow;
    private const string AudioEntry = "media/audio-001.m4a";

    private sealed class Storage : IFileStorageService
    {
        public readonly ConcurrentDictionary<string, byte[]> Files = new(StringComparer.OrdinalIgnoreCase);
        public async Task WriteAsync(string relativePath, Stream data, CancellationToken ct = default)
        {
            using var buffer = new MemoryStream();
            await data.CopyToAsync(buffer, ct);
            Files[relativePath] = buffer.ToArray();
        }
        public Task<Stream> OpenReadAsync(string relativePath, CancellationToken ct = default)
            => Task.FromResult<Stream>(new MemoryStream(Files[relativePath], writable: false));
        public Task DeleteAsync(string relativePath, CancellationToken ct = default)
        { Files.TryRemove(relativePath, out _); return Task.CompletedTask; }
        public Task DeleteDirectoryAsync(string relativeDirectory, CancellationToken ct = default) => Task.CompletedTask;
        public bool Exists(string relativePath) => Files.ContainsKey(relativePath);
        public IReadOnlyList<string> ListFiles(string relativeDirectory) => [];
        public string UserFilePath(Guid userId, string storedFileName) => $"users/{userId}/{storedFileName}";
        public string OrgFilePath(Guid orgId, string storedFileName) => $"orgs/{orgId}/{storedFileName}";
        public string CaseFilePath(Guid caseId, string storedFileName) => $"cases/{caseId}/{storedFileName}";
    }

    private sealed record World(IDbContextFactory<BenDataContext> Factory, Storage Storage, FileProvenanceService Provenance,
                                Guid SessionId, byte[] Audio);

    /// <summary>A sealed session sent to the group's event, and the group's copy of its recording.</summary>
    private static async Task<World> WorldAsync(string? sealedHash = null)
    {
        var factory = new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var storage = new Storage();
        var audio = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "device-clip.m4a"));
        var hash = Convert.ToHexString(SHA256.HashData(audio)).ToLowerInvariant();

        using var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            await using (var entry = archive.CreateEntry(BenBundle.DocumentEntryPath, CompressionLevel.NoCompression).Open())
                await entry.WriteAsync(Encoding.UTF8.GetBytes("""{"format_version":"1.0.0","session":{"started_at":"2026-10-28T17:00:00Z"},"readings":[]}"""));
            await using (var entry = archive.CreateEntry(AudioEntry, CompressionLevel.NoCompression).Open())
                await entry.WriteAsync(audio);
            await using (var entry = archive.CreateEntry(BenBundle.SealEntryPath, CompressionLevel.NoCompression).Open())
                await entry.WriteAsync(Encoding.UTF8.GetBytes(
                    $$"""{"version":1,"entries":[{"path":"{{AudioEntry}}","sha256":"{{sealedHash ?? hash}}","byte_count":{{audio.Length}}}]}"""));
        }
        var bundlePath = $"users/{Guest}/field-sessions/{Guid.NewGuid():N}.ben";
        storage.Files[bundlePath] = zip.ToArray();

        await using var db = await factory.CreateDbContextAsync();
        db.Organizations.Add(new Organization { Id = Group, Name = "Hollow Creek Paranormal", DateCreated = Now, CreatedByAppUserId = Organizer });
        db.HostedEvents.Add(new HostedEvent
        {
            Id = Hosted, OrganizationId = Group, Name = "Seance Weekend", UrlName = "seance-weekend",
            StartsOn = Now.Date, EndsOn = Now.Date.AddDays(1), LayoutKind = HostedEventLayoutKind.Seats,
            DateCreated = Now, CreatedByAppUserId = Organizer,
        });
        foreach (var (id, name) in new[] { (Guest, "Daniel Park"), (Organizer, "Ben"), (OtherMember, "Sarah") })
            db.AppUsers.Add(new AppUser { Id = id, UserName = name, DisplayName = name });
        var document = new UploadFile
        {
            Id = Guid.NewGuid(), FileName = "session.ben", StoredFileName = "session.ben", ContentType = "application/zip",
            StoragePath = bundlePath, DateCreated = Now, CreatedByAppUserId = Guest,
        };
        db.UploadFiles.Add(document);
        var sessionId = Guid.NewGuid();
        db.FieldSessionUploads.Add(new FieldSessionUpload
        {
            Id = sessionId, SubmittedByAppUserId = Guest, HostedEventId = Hosted, DeviceSessionId = Guid.NewGuid(),
            DeviceModel = "iPhone16,2", DocumentUploadFileId = document.Id, IsBundle = true,
            StartedAt = new DateTime(2026, 10, 28, 17, 0, 0, DateTimeKind.Utc), LocationLabel = "Room 7",
            MediaReviewState = FeedMediaReviewState.Approved,
            EventResultsConsentAtUtc = Now, ShownWithEventResultsAtUtc = Now,
            DateCreated = Now, CreatedByAppUserId = Guest,
        });
        await db.SaveChangesAsync();

        var provenance = new FileProvenanceService(TestBundles.Store(storage), storage,
            new ProvenanceSigner(new EphemeralDataProtectionProvider()));
        return new World(factory, storage, provenance, sessionId, audio);
    }

    /// <summary>A copy of the recording for one scope, as <c>FieldSessionAnalysisCopies</c> makes it.</summary>
    private static async Task<Guid> CopyAsync(World w, AnalysisScope scope, Guid madeBy)
    {
        await using var db = await w.Factory.CreateDbContextAsync();
        var copyId = Guid.NewGuid();
        var path = $"copies/{copyId:N}.m4a";
        w.Storage.Files[path] = w.Audio;
        db.UploadFiles.Add(new UploadFile
        {
            Id = copyId, FileName = "copy.m4a", StoredFileName = "copy.m4a", ContentType = "audio/mp4", StoragePath = path,
            AppUserId = scope == AnalysisScope.Person ? madeBy : null,
            OwnerOrganizationId = scope == AnalysisScope.Organization ? Group : null,
            SourceFieldSessionUploadId = w.SessionId, SourceClipPath = AudioEntry, DateCreated = Now, CreatedByAppUserId = madeBy,
        });
        db.FieldSessionAnalysisCopies.Add(new FieldSessionAnalysisCopy
        {
            Id = Guid.NewGuid(), FieldSessionUploadId = w.SessionId, FieldSessionUploadFileId = Guid.NewGuid(),
            CopyUploadFileId = copyId, Scope = scope,
            ScopeAppUserId = scope == AnalysisScope.Person ? madeBy : null,
            ScopeOrganizationId = scope == AnalysisScope.Organization ? Group : null,
            SourceSha256 = Convert.ToHexString(SHA256.HashData(w.Audio)).ToLowerInvariant(),
            SourceRelativePath = AudioEntry, MadeByAppUserId = madeBy, DateCreated = Now,
        });
        await db.SaveChangesAsync();
        return copyId;
    }

    /// <summary>Saves a new file made from <paramref name="parent"/> the way a clip or edit does, certificate and all.</summary>
    private static async Task<(Guid Id, FileProvenance? Row)> MakeAsync(
        World w, Guid parent, string op, Dictionary<string, double>? settings, double startSeconds, Guid by)
    {
        await using var db = await w.Factory.CreateDbContextAsync();
        var bytes = Encoding.UTF8.GetBytes($"{op}-{Guid.NewGuid()}");
        var file = new UploadFile
        {
            Id = Guid.NewGuid(), FileName = $"{op}.flac", StoredFileName = $"{op}.flac", ContentType = "audio/flac",
            StoragePath = $"made/{Guid.NewGuid():N}.flac", ParentFileId = parent, SourceFieldSessionUploadId = w.SessionId,
            SourceClipPath = AudioEntry, OwnerOrganizationId = Group, DateCreated = Now, CreatedByAppUserId = by,
        };
        w.Storage.Files[file.StoragePath] = bytes;
        db.UploadFiles.Add(file);
        var row = await w.Provenance.RecordAsync(db, file, parent, op, settings, startSeconds, bytes, 2000, by, default);
        await db.SaveChangesAsync();
        return (file.Id, row);
    }

    [Fact]
    public async Task A_clip_of_the_copy_is_certified_back_to_the_phones_seal_and_placed_in_the_recording()
    {
        var w = await WorldAsync();
        var copy = await CopyAsync(w, AnalysisScope.Organization, Organizer);

        var (clipId, row) = await MakeAsync(w, copy, "clip",
            new() { ["start"] = 12.5, ["end"] = 14.5, ["normalize"] = 1 }, 12.5, Organizer);

        Assert.NotNull(row);
        Assert.True(row!.Enhanced);
        Assert.Equal(12_500, row.RecordingOffsetMs);
        Assert.Equal(row.CopySha256, row.SealSha256);
        Assert.Equal(["copy", "clip"], FileProvenanceService.Chain(row.ChainJson).Select(s => s.Op));

        await using var db = await w.Factory.CreateDbContextAsync();
        var certificate = await FileProvenanceService.DescribeAsync(db, clipId, default);
        Assert.True(certificate!.CopyMatchesSeal);
        Assert.Contains("matches what the phone sealed", certificate.Steps[0].Sentence);
        Assert.Contains("0:12 to 0:14", certificate.Steps[1].Sentence);
        Assert.Equal("verified", (await w.Provenance.CheckAsync(db, clipId, default))!.Outcome);
    }

    [Fact]
    public async Task Gain_keeps_it_enhanced_and_in_place_and_a_speed_change_takes_it_out_of_the_timeline()
    {
        var w = await WorldAsync();
        var copy = await CopyAsync(w, AnalysisScope.Organization, Organizer);
        var (clip, _) = await MakeAsync(w, copy, "clip", new() { ["start"] = 3, ["end"] = 5 }, 3, Organizer);

        var (louder, gained) = await MakeAsync(w, clip, "gain", new() { ["gainDb"] = 6 }, 0, Organizer);
        Assert.True(gained!.Enhanced);
        Assert.Equal(3_000, gained.RecordingOffsetMs);
        Assert.Equal(3, FileProvenanceService.Chain(gained.ChainJson).Count);

        var (_, slowed) = await MakeAsync(w, louder, "speed", new() { ["ratio"] = 0.75 }, 0, Organizer);
        Assert.False(slowed!.Enhanced);
        Assert.Null(slowed.RecordingOffsetMs);
        Assert.Equal("Clip 0:03–0:05 · +6 dB · speed ×0.75", FileProvenanceService.Summary(slowed.ChainJson));
    }

    [Fact]
    public async Task A_file_not_made_from_a_session_recording_has_no_certificate()
    {
        var w = await WorldAsync();
        await using var db = await w.Factory.CreateDbContextAsync();
        var loose = new UploadFile
        {
            Id = Guid.NewGuid(), FileName = "upload.mp3", StoredFileName = "upload.mp3", ContentType = "audio/mpeg",
            DateCreated = Now, CreatedByAppUserId = Organizer,
        };
        db.UploadFiles.Add(loose);
        await db.SaveChangesAsync();

        var (_, row) = await MakeAsync(w, loose.Id, "clip", new() { ["start"] = 0, ["end"] = 1 }, 0, Organizer);
        Assert.Null(row);
    }

    [Fact]
    public async Task A_copy_that_does_not_match_the_seal_says_so_on_its_certificate()
    {
        var w = await WorldAsync(sealedHash: new string('a', 64));
        var copy = await CopyAsync(w, AnalysisScope.Organization, Organizer);
        var (clipId, _) = await MakeAsync(w, copy, "clip", new() { ["start"] = 0, ["end"] = 1 }, 0, Organizer);

        await using var db = await w.Factory.CreateDbContextAsync();
        var certificate = await FileProvenanceService.DescribeAsync(db, clipId, default);
        Assert.False(certificate!.CopyMatchesSeal);
        Assert.Contains("does NOT match", certificate.Steps[0].Sentence);
    }

    [Fact]
    public async Task Changed_bytes_or_a_changed_certificate_no_longer_check()
    {
        var w = await WorldAsync();
        var copy = await CopyAsync(w, AnalysisScope.Organization, Organizer);
        var (clipId, row) = await MakeAsync(w, copy, "clip", new() { ["start"] = 0, ["end"] = 1 }, 0, Organizer);

        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            var path = (await db.UploadFiles.SingleAsync(f => f.Id == clipId)).StoragePath!;
            var original = w.Storage.Files[path];
            w.Storage.Files[path] = [.. original, 0];
            Assert.Equal("changed", (await w.Provenance.CheckAsync(db, clipId, default))!.Outcome);
            w.Storage.Files[path] = original;
        }

        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            var stored = await db.FileProvenances.SingleAsync(p => p.Id == row!.Id);
            stored.RecordingOffsetMs = 60_000;   // somebody moves it in the database
            await db.SaveChangesAsync();
            var check = await w.Provenance.CheckAsync(db, clipId, default);
            Assert.Equal("changed", check!.Outcome);
            Assert.Contains("certificate itself has been changed", check.Sentence);
        }
    }

    /// <summary>
    /// SQL Server gives a date back without saying it is UTC. The seal covered "…Z" and the row read
    /// back said "…" with no zone, so every certificate on the real database checked as changed
    /// (10/28/2026, caught by the browser test; the in-memory database keeps the zone and hid it).
    /// </summary>
    [Fact]
    public void A_certificate_read_back_without_its_time_zone_still_checks()
    {
        var issued = new DateTime(2026, 10, 28, 18, 9, 5, DateTimeKind.Utc);
        var asSigned = new FileProvenance
        {
            UploadFileId = Guid.NewGuid(), FieldSessionUploadId = Guid.NewGuid(), AnalysisCopyUploadFileId = Guid.NewGuid(),
            SourceClipPath = AudioEntry, CopySha256 = "c", ResultSha256 = "r", ChainJson = "[]",
            MadeByAppUserId = Organizer, IssuedUtc = issued,
        };
        var asRead = new FileProvenance
        {
            UploadFileId = asSigned.UploadFileId, FieldSessionUploadId = asSigned.FieldSessionUploadId,
            AnalysisCopyUploadFileId = asSigned.AnalysisCopyUploadFileId, SourceClipPath = AudioEntry,
            CopySha256 = "c", ResultSha256 = "r", ChainJson = "[]", MadeByAppUserId = Organizer,
            IssuedUtc = DateTime.SpecifyKind(issued, DateTimeKind.Unspecified),   // as SQL Server hands it back
        };
        Assert.Equal(FileProvenanceService.Canonical(asSigned), FileProvenanceService.Canonical(asRead));
    }

    /// <summary>
    /// A session shown with an event's results: visitors see the sender's own certified clips and the
    /// event group's, never another member's personal copy's; nothing before its media is approved.
    /// </summary>
    [Fact]
    public async Task The_public_sees_the_senders_and_the_showing_groups_clips_and_nobody_elses()
    {
        var w = await WorldAsync();
        var groups = await CopyAsync(w, AnalysisScope.Organization, Organizer);
        var senders = await CopyAsync(w, AnalysisScope.Person, Guest);
        var someoneElses = await CopyAsync(w, AnalysisScope.Person, OtherMember);
        var (fromGroup, _) = await MakeAsync(w, groups, "clip", new() { ["start"] = 0, ["end"] = 1 }, 0, Organizer);
        var (fromSender, _) = await MakeAsync(w, senders, "clip", new() { ["start"] = 1, ["end"] = 2 }, 1, Guest);
        var (fromOther, _) = await MakeAsync(w, someoneElses, "clip", new() { ["start"] = 2, ["end"] = 3 }, 2, OtherMember);

        await using var db = await w.Factory.CreateDbContextAsync();
        var session = await db.FieldSessionUploads.SingleAsync(s => s.Id == w.SessionId);
        var shown = (await SessionFindingsReader.ForPublicAsync(db, session, default)).Clips.Select(c => c.FileId).ToList();
        Assert.Contains(fromGroup, shown);
        Assert.Contains(fromSender, shown);
        Assert.DoesNotContain(fromOther, shown);

        session.MediaReviewState = FeedMediaReviewState.Pending;
        Assert.Empty((await SessionFindingsReader.ForPublicAsync(db, session, default)).Clips);
    }
}
