using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Access;
using Ben.Data.WebApi.Services.Audio;
using Ben.Data.WebApi.Services.Audio.Scanning;
using Ben.Data.WebApi.Services.Billing;
using Ben.Data.WebApi.Services.FieldSessions;
using Ben.Data.WebApi.Services.Media;
using Ben.Service.Models.Entities;
using Ben.Service.Models.FieldSessions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Scanning a committed .ben session (phase 9 of the audio plan, 10/27/2026): the scanner's own copy,
/// the session left exactly as it was sent, the questions carried onto the candidates.
/// </summary>
public sealed class SessionEvpScanTests
{
    private static readonly Guid Guest = Guid.NewGuid();
    private static readonly Guid Organizer = Guid.NewGuid();
    private static readonly Guid Stranger = Guid.NewGuid();
    private static readonly Guid Group = Guid.NewGuid();
    private static readonly Guid Hosted = Guid.NewGuid();
    private static readonly DateTime Now = DateTime.UtcNow;

    private const string AudioEntry = "media/audio-001.m4a";
    private const string Question = "Is anyone here with us tonight?";

    /// <summary>
    /// The recording began at 0:05 (an audio_ref 2 s into it, at 0:07); the question was asked at 0:10
    /// and the wait ended at 0:20.
    /// </summary>
    private static readonly string Document = $$$$"""
        {"format_version":"1.0.0","device":{"manufacturer":"Apple","model":"iPhone17,1"},
         "session":{"started_at":"2026-10-12T01:00:00Z","ended_at":"2026-10-12T01:01:00Z","location_label":"Room 7"},
         "readings":[
           {"at":"2026-10-12T01:00:00Z","measurements":{"emf":{"value":48}}},
           {"at":"2026-10-12T01:00:07Z","measurements":{"emf":{"value":48}},
            "audio_ref":{"filename":"{{{{AudioEntry}}}}","start_offset_seconds":2}},
           {"at":"2026-10-12T01:00:10Z","measurements":{"marker":{"value":"evp_question"}},"note":"{{{{Question}}}}"},
           {"at":"2026-10-12T01:00:20Z","measurements":{"marker":{"value":"evp_wait_end"}}},
           {"at":"2026-10-12T01:01:00Z","measurements":{"emf":{"value":49}}}
         ]}
        """;

    // ── The copy ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_groups_copy_is_the_recordings_own_bytes_and_the_session_is_never_written()
    {
        var w = await WorldAsync();
        var bundleBefore = SHA256.HashData(w.Storage.Files[w.BundlePath]);

        var outcome = await EnsureAsync(w, Organizer, AnalysisScope.Organization, Group);

        Assert.True(outcome.Created);
        var copy = outcome.Copy!;
        Assert.Equal(w.Audio, w.Storage.Files[copy.StoragePath!]);
        Assert.StartsWith($"orgs/{Group}/session-copies/", copy.StoragePath);
        Assert.Equal(Group, copy.OwnerOrganizationId);
        Assert.Null(copy.AppUserId);
        Assert.Equal(w.SessionId, copy.SourceFieldSessionUploadId);
        Assert.Equal(AudioEntry, copy.SourceClipPath);
        Assert.Equal("audio/mp4", copy.ContentType);

        Assert.Equal(bundleBefore, SHA256.HashData(w.Storage.Files[w.BundlePath]));
        Assert.DoesNotContain(w.BundlePath, w.Storage.Written);
    }

    [Fact]
    public async Task Opening_again_finds_the_same_copy()
    {
        var w = await WorldAsync();
        var first = await EnsureAsync(w, Organizer, AnalysisScope.Organization, Group);
        var again = await EnsureAsync(w, Organizer, AnalysisScope.Organization, Group);

        Assert.False(again.Created);
        Assert.Equal(first.Copy!.Id, again.Copy!.Id);
        Assert.Single(w.Storage.Written, p => p.Contains("session-copies/"));
    }

    [Fact]
    public async Task The_guests_own_copy_is_in_their_files_and_apart_from_the_groups()
    {
        var w = await WorldAsync();
        var groups = await EnsureAsync(w, Organizer, AnalysisScope.Organization, Group);
        var own = await EnsureAsync(w, Guest, AnalysisScope.Person, null);

        Assert.NotEqual(groups.Copy!.Id, own.Copy!.Id);
        Assert.StartsWith($"users/{Guest}/session-copies/", own.Copy.StoragePath);
        Assert.Equal(Guest, own.Copy.AppUserId);
        Assert.Null(own.Copy.OwnerOrganizationId);
    }

    [Fact]
    public async Task A_copy_that_does_not_match_the_phones_digest_is_not_kept()
    {
        var w = await WorldAsync(digest: new string('0', 64));

        var outcome = await EnsureAsync(w, Organizer, AnalysisScope.Organization, Group);

        Assert.Null(outcome.Copy);
        Assert.Equal(StatusCodes.Status409Conflict, outcome.Status);
        Assert.Equal(FieldSessionAnalysisCopies.MismatchSentence, outcome.Problem);
        Assert.DoesNotContain(w.Storage.Files.Keys, k => k.Contains("session-copies/"));
        await using var db = await w.Factory.CreateDbContextAsync();
        Assert.Empty(db.FieldSessionAnalysisCopies);
        Assert.False(await db.UploadFiles.AnyAsync(f => f.SourceFieldSessionUploadId == w.SessionId));
    }

    // ── The endpoints ─────────────────────────────────────────────────────────

    [Fact]
    public async Task The_organizer_sees_the_recording_and_can_scan_it_for_the_group()
    {
        var w = await WorldAsync();
        var view = Ok<FieldSessionAnalysisView>(await Controller(w, Organizer).GetAnalysis(w.SessionId, null, null, Decoder(), default));

        Assert.True(view.MayScan);
        Assert.True(view.DecoderAvailable);
        Assert.Equal("Hollow Creek Paranormal's copy", Assert.Single(view.Scopes).Label);
        var clip = Assert.Single(view.Clips);
        Assert.Equal(AudioEntry, clip.RelativePath);
        Assert.Null(clip.CopyUploadFileId);

        var started = await Controller(w, Organizer).StartSessionScan(w.SessionId,
            new StartSessionScanRequest(EvpSensitivity.Medium), Copies(w), Decoder(), new EvpScanQueue(), default);
        var job = Assert.IsType<EvpScanJobRecord>(Assert.IsType<AcceptedResult>(started.Result).Value);
        var segment = Assert.Single(job.Segments);

        await using var db = await w.Factory.CreateDbContextAsync();
        var row = await db.EvpScanJobs.SingleAsync();
        Assert.Equal(w.SessionId, row.FieldSessionUploadId);
        var copy = await db.FieldSessionAnalysisCopies.SingleAsync();
        Assert.Equal(copy.CopyUploadFileId, segment.UploadFileId);

        // The page finds the scan that is running, and the copy it works on.
        var after = Ok<FieldSessionAnalysisView>(await Controller(w, Organizer).GetAnalysis(w.SessionId, null, null, Decoder(), default));
        Assert.Equal(job.Id, after.LiveJob?.Id);
        Assert.Equal(copy.CopyUploadFileId, Assert.Single(after.Clips).CopyUploadFileId);

        // A second scan while the first runs is refused, not queued to replace its candidates.
        var twice = await Controller(w, Organizer).StartSessionScan(w.SessionId,
            new StartSessionScanRequest(EvpSensitivity.Medium), Copies(w), Decoder(), new EvpScanQueue(), default);
        Assert.IsType<ConflictObjectResult>(twice.Result);
    }

    [Fact]
    public async Task Somebody_with_no_part_in_the_session_is_told_it_isnt_here()
    {
        var w = await WorldAsync();

        Assert.IsType<NotFoundResult>((await Controller(w, Stranger).GetAnalysis(w.SessionId, null, null, Decoder(), default)).Result);
        Assert.IsType<NotFoundResult>((await Controller(w, Stranger).EnsureAnalysisCopy(
            w.SessionId, w.RecordingId, new AnalysisCopyRequest(), Copies(w), default)).Result);
        Assert.IsType<NotFoundResult>((await Controller(w, Stranger).StartSessionScan(w.SessionId,
            new StartSessionScanRequest(EvpSensitivity.Medium), Copies(w), Decoder(), new EvpScanQueue(), default)).Result);
        Assert.DoesNotContain(w.Storage.Written, p => p.Contains("session-copies/"));
    }

    [Fact]
    public async Task A_server_that_cannot_read_m4a_refuses_before_copying_anything()
    {
        var w = await WorldAsync();
        var cannot = new Mock<IAudioDecoder>();
        cannot.Setup(d => d.CanDecode(It.IsAny<string?>(), It.IsAny<string?>())).Returns(false);

        var started = await Controller(w, Organizer).StartSessionScan(w.SessionId,
            new StartSessionScanRequest(EvpSensitivity.Medium), Copies(w), cannot.Object, new EvpScanQueue(), default);

        var refusal = Assert.IsType<ScanRefusalRecord>(Assert.IsType<BadRequestObjectResult>(started.Result).Value);
        Assert.Equal(ScanRefusalRecord.DecoderUnavailable, refusal.Reason);
        Assert.DoesNotContain(w.Storage.Written, p => p.Contains("session-copies/"));
    }

    // ── The questions ─────────────────────────────────────────────────────────

    [Fact]
    public async Task A_candidate_heard_after_a_question_carries_it()
    {
        var w = await WorldAsync();
        var copy = (await EnsureAsync(w, Organizer, AnalysisScope.Organization, Group)).Copy!;
        var context = new EvpQuestionContext(w.Factory, Timelines(w), NullLogger<EvpQuestionContext>.Instance);

        var noteFor = await context.ForAsync(copy.Id, default);

        Assert.NotNull(noteFor);
        // The recording began at 0:05, so its 7 s is the session's 0:12: two seconds after the question.
        Assert.Equal($"Q: {Question}", noteFor(new AudioCandidateRequest(7, 8, 50)));
        // Its 2 s is 0:07, before the question was asked; its 16 s is 0:21, after the wait ended.
        Assert.Null(noteFor(new AudioCandidateRequest(2, 3, 50)));
        Assert.Null(noteFor(new AudioCandidateRequest(16, 17, 50)));
    }

    [Fact]
    public void A_question_lasts_until_the_wait_ends_or_the_next_question_or_a_minute()
    {
        var timeline = TimelineWith(
            Mark(10_000, "evp_question", "Who is here?"),
            Mark(15_000, "evp_wait_end"),
            Mark(30_000, "evp_question", "What is your name?"),
            Mark(40_000, "evp_question", null),
            Mark(500_000, "evp_wait_end"));

        var windows = EvpQuestionWindows.Of(timeline);

        Assert.Collection(windows,
            first => Assert.Equal((10_000L, 15_000L, "Q: Who is here?"), (first.FromMs, first.ToMs, first.Note)),
            cut => Assert.Equal((30_000L, 40_000L, "Q: What is your name?"), (cut.FromMs, cut.ToMs, cut.Note)),
            unnamed => Assert.Equal((40_000L, 500_000L, "After the question at 0:40"), (unnamed.FromMs, unnamed.ToMs, unnamed.Note)));

        var last = EvpQuestionWindows.Of(TimelineWith(Mark(70_000, "evp_question", "Anyone?")));
        Assert.Equal(70_000 + EvpQuestionWindows.DefaultWindowMs, Assert.Single(last).ToMs);
    }

    [Fact]
    public void A_recording_the_timeline_could_not_place_gets_no_notes()
    {
        var recording = Guid.NewGuid();
        var timeline = TimelineWith(Mark(10_000, "evp_question", "Who is here?")) with
        {
            Clips = [new TimelineClip(recording, AudioEntry, "audio", StartMs: null, DurationMs: null, NotedEndMs: null)],
        };
        Assert.Null(EvpQuestionWindows.For(timeline, recording));

        // Placed from when it was noted finished and how long it ran (rule M4).
        var placed = timeline with
        {
            Clips = [new TimelineClip(recording, AudioEntry, "audio", StartMs: null, DurationMs: 20_000, NotedEndMs: 25_000)],
        };
        Assert.Equal("Q: Who is here?", EvpQuestionWindows.For(placed, recording)!(new AudioCandidateRequest(6, 7, 40)));
    }

    // ── The world ─────────────────────────────────────────────────────────────

    private sealed class Storage : IFileStorageService
    {
        public readonly ConcurrentDictionary<string, byte[]> Files = new(StringComparer.OrdinalIgnoreCase);
        public readonly ConcurrentBag<string> Written = [];
        public async Task WriteAsync(string relativePath, Stream data, CancellationToken ct = default)
        {
            Written.Add(relativePath);
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

    private sealed record World(
        IDbContextFactory<BenDataContext> Factory, Storage Storage, Guid SessionId, Guid RecordingId,
        string BundlePath, byte[] Audio);

    private static async Task<World> WorldAsync(string? digest = null)
    {
        var factory = new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var storage = new Storage();
        var audio = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "device-clip.m4a"));

        // A session as the phone seals it: stored, never compressed, recordings and document side by side.
        using var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            await using (var entry = archive.CreateEntry(BenBundle.DocumentEntryPath, CompressionLevel.NoCompression).Open())
                await entry.WriteAsync(Encoding.UTF8.GetBytes(Document));
            await using (var entry = archive.CreateEntry(AudioEntry, CompressionLevel.NoCompression).Open())
                await entry.WriteAsync(audio);
        }
        var bundlePath = $"users/{Guest}/field-sessions/{Guid.NewGuid():N}.ben";
        storage.Files[bundlePath] = zip.ToArray();

        await using var db = await factory.CreateDbContextAsync();
        db.Organizations.Add(new Organization { Id = Group, Name = "Hollow Creek Paranormal", DateCreated = Now, CreatedByAppUserId = Organizer });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = Group, AppUserId = Organizer, Role = OrganizationMemberRole.Owner,
            IsActive = true, DateCreated = Now, CreatedByAppUserId = Organizer,
        });
        db.HostedEvents.Add(new HostedEvent
        {
            Id = Hosted, OrganizationId = Group, Name = "Seance Weekend", UrlName = "seance-weekend",
            StartsOn = Now.Date, EndsOn = Now.Date.AddDays(1), LayoutKind = HostedEventLayoutKind.Seats,
            DateCreated = Now, CreatedByAppUserId = Organizer,
        });

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
            DeviceModel = "iPhone17,1", DocumentUploadFileId = document.Id, IsBundle = true,
            StartedAt = new DateTime(2026, 10, 12, 1, 0, 0, DateTimeKind.Utc), LocationLabel = "Room 7",
            DateCreated = Now, CreatedByAppUserId = Guest,
        });
        var recordingId = Guid.NewGuid();
        db.FieldSessionUploadFiles.Add(new FieldSessionUploadFile
        {
            Id = recordingId, FieldSessionUploadId = sessionId, BundleEntryPath = AudioEntry, RelativePath = AudioEntry,
            ContentType = "audio/mp4", FileSize = audio.Length,
            Sha256 = digest ?? Convert.ToHexString(SHA256.HashData(audio)).ToLowerInvariant(), DigestMatched = true,
            DateCreated = Now, CreatedByAppUserId = Guest,
        });
        await db.SaveChangesAsync();
        return new World(factory, storage, sessionId, recordingId, bundlePath, audio);
    }

    private static FieldSessionAnalysisCopies Copies(World w) => new(
        w.Storage, TestBundles.Store(w.Storage), new Mock<IMediaIngestService>().Object,
        new SubscriptionLimitGuard(w.Factory), NullLogger<FieldSessionAnalysisCopies>.Instance);

    private static FieldSessionTimelineService Timelines(World w)
        => new(w.Storage, TestBundles.Store(w.Storage), NullLogger<FieldSessionTimelineService>.Instance);

    private static async Task<AnalysisCopyOutcome> EnsureAsync(World w, Guid userId, AnalysisScope scope, Guid? org)
    {
        await using var db = await w.Factory.CreateDbContextAsync();
        var session = await db.FieldSessionUploads.AsNoTracking().Include(s => s.Files).SingleAsync(s => s.Id == w.SessionId);
        var decision = await FieldSessionAnalysisAccess.MayScanAsync(db, session, userId, scope, org, default);
        Assert.True(decision.Allowed, decision.Message);
        return await Copies(w).EnsureAsync(db, session, session.Files.Single(), decision.Scope!, userId, default);
    }

    private static IAudioDecoder Decoder()
    {
        var decoder = new Mock<IAudioDecoder>();
        decoder.Setup(d => d.CanDecode(It.IsAny<string?>(), It.IsAny<string?>())).Returns(true);
        return decoder.Object;
    }

    private static FieldSessionUploadController Controller(World w, Guid userId) => new(
        w.Factory, w.Storage, new Mock<IMediaIngestService>().Object,
        new MediaRetentionPolicy(new SubscriptionLimitGuard(w.Factory)),
        TestBundles.Store(w.Storage), NullLogger<FieldSessionUploadController>.Instance)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Bearer")),
            },
        },
    };

    private static T Ok<T>(ActionResult<T> result) => Assert.IsType<T>(Assert.IsType<OkObjectResult>(result.Result).Value);

    private static TimelineMarker Mark(long t, string kind, string? note = null)
        => new(t, kind, kind, Automatic: false, note, Room: null, FieldMilligauss: null);

    private static SessionTimeline TimelineWith(params TimelineMarker[] markers) => new(
        SessionTimeline.CurrentVersion, Now, 600_000, true, TimelinePositionPolicy.Exact,
        new TimelineReadings([], [], [], [], [], [], [], [], [], []), [], markers, [],
        [new TimelineClip(Guid.Empty, AudioEntry, "audio", 0, 600_000, null)], [],
        new TimelineTrace([], [], false), [], 0, null);
}
