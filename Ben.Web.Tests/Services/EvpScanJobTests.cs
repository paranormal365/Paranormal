using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Data.WebApi.Services.Audio;
using Ben.Data.WebApi.Services.Audio.Scanning;
using Ben.Data.WebApi.Services.Scheduling;
using Ben.Service.Models.Entities;
using Ben.Web.Tests.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Security.Claims;
using System.Text.Json;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Background EVP scans: the runner, the worker's restart tidy-up, the API, and the weekly sweep
/// (phase 6, 10/26/2026).
/// </summary>
public class EvpScanJobTests
{
    private static IDbContextFactory<BenDataContext> CreateFactory() =>
        new PooledDbContextFactory<BenDataContext>(new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<(Guid FileId, Guid OwnerId)> SeedFileAsync(
        IDbContextFactory<BenDataContext> factory, string contentType = "audio/wav", byte[]? bytes = null)
    {
        var fileId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        bytes ??= AudioMarkerControllerTests.BuildScannableWav();
        await using var db = await factory.CreateDbContextAsync();
        db.UploadFiles.Add(new UploadFile
        {
            Id = fileId, UploadFileTypeId = Guid.NewGuid(), AppUserId = ownerId,
            FileName = contentType == "audio/wav" ? "evp.wav" : "phone.m4a", StoredFileName = "s", ContentType = contentType,
            FileSize = bytes.Length, FileData = bytes, DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
        });
        await db.SaveChangesAsync();
        return (fileId, ownerId);
    }

    private static async Task<Guid> SeedJobAsync(IDbContextFactory<BenDataContext> factory, Guid fileId, Guid userId,
        EvpScanJobStatus status = EvpScanJobStatus.Queued, DateTime? finished = null, DateTime? cancelRequested = null)
    {
        var job = new EvpScanJob
        {
            Id = Guid.NewGuid(), RequestedByAppUserId = userId, Sensitivity = EvpSensitivity.Medium,
            OptionsJson = JsonSerializer.Serialize(EvpDetectionOptions.FromSensitivity(EvpSensitivity.Medium), EvpScanRecorder.OptionsJson),
            Status = status, CreatedUtc = DateTime.UtcNow, FinishedUtc = finished, CancelRequestedUtc = cancelRequested,
        };
        job.Segments.Add(new EvpScanJobSegment
        {
            Id = Guid.NewGuid(), UploadFileId = fileId, Status = status, FinishedUtc = finished,
        });
        await using var db = await factory.CreateDbContextAsync();
        db.EvpScanJobs.Add(job);
        await db.SaveChangesAsync();
        return job.Id;
    }

    private static EvpScanService Service(IDbContextFactory<BenDataContext> factory, IAudioDecoder? decoder = null)
        => new(factory, new Mock<IFileStorageService>().Object, decoder ?? TestAudio.Decoder(),
               new EvpScanRecorder(factory, NullLogger<EvpScanRecorder>.Instance), NullLogger<EvpScanService>.Instance);

    private static EvpScanRunner Runner(IDbContextFactory<BenDataContext> factory, IAudioDecoder? decoder = null)
        => new(factory, Service(factory, decoder), NullLogger<EvpScanRunner>.Instance);

    private static async Task<EvpScanJob> JobAsync(IDbContextFactory<BenDataContext> factory, Guid id)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.EvpScanJobs.AsNoTracking().Include(j => j.Segments).SingleAsync(j => j.Id == id);
    }

    // ── The runner ────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_job_scans_its_recording_and_lands_the_candidates()
    {
        var factory = CreateFactory();
        var (fileId, ownerId) = await SeedFileAsync(factory);
        var jobId = await SeedJobAsync(factory, fileId, ownerId);

        await Runner(factory).RunAsync(jobId, default);

        var job = await JobAsync(factory, jobId);
        var segment = Assert.Single(job.Segments);
        Assert.Equal((EvpScanJobStatus.Done, 100), (job.Status, job.Percent));
        Assert.Equal((EvpScanJobStatus.Done, 2), (segment.Status, segment.CandidateCount));
        Assert.Equal(20.0, segment.ProcessedSeconds, 2);
        Assert.NotNull(segment.EvpScanId);
        Assert.NotNull(segment.FinishedUtc);

        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(2, await db.AudioMarkers.CountAsync(m => m.UploadFileId == fileId && m.ReviewStatus == EvpReviewStatus.Pending));
        Assert.Equal(1, await db.EvpScans.CountAsync(s => s.UploadFileId == fileId));
    }

    /// <summary>A recording this server cannot read fails with the server's sentence, which the editor shows.</summary>
    [Fact]
    public async Task A_recording_the_server_cannot_read_fails_with_the_reason()
    {
        var factory = CreateFactory();
        var (fileId, ownerId) = await SeedFileAsync(factory, "audio/mp4", new byte[64]);
        var jobId = await SeedJobAsync(factory, fileId, ownerId);

        await Runner(factory).RunAsync(jobId, default);

        var job = await JobAsync(factory, jobId);
        Assert.Equal(EvpScanJobStatus.Failed, job.Status);
        Assert.Equal(CompositeAudioDecoder.UnavailableSentence, job.Error);
        Assert.Equal(EvpScanJobStatus.Failed, job.Segments.Single().Status);
    }

    [Fact]
    public async Task A_job_canceled_before_it_ran_scans_nothing()
    {
        var factory = CreateFactory();
        var (fileId, ownerId) = await SeedFileAsync(factory);
        var jobId = await SeedJobAsync(factory, fileId, ownerId, cancelRequested: DateTime.UtcNow);

        await Runner(factory).RunAsync(jobId, default);

        var job = await JobAsync(factory, jobId);
        Assert.Equal(EvpScanJobStatus.Canceled, job.Status);
        Assert.Equal(EvpScanJobStatus.Canceled, job.Segments.Single().Status);
        await using var db = await factory.CreateDbContextAsync();
        Assert.False(await db.AudioMarkers.AnyAsync(m => m.UploadFileId == fileId));
    }

    /// <summary>Canceled mid-scan: the old Pending candidates stay, because they're only replaced when a scan completes.</summary>
    [Fact]
    public async Task A_job_canceled_while_scanning_keeps_the_old_candidates()
    {
        var factory = CreateFactory();
        var (fileId, ownerId) = await SeedFileAsync(factory);
        await Runner(factory).RunAsync(await SeedJobAsync(factory, fileId, ownerId), default);
        var second = await SeedJobAsync(factory, fileId, ownerId);

        using var cancel = new CancellationTokenSource();
        var decoder = new CancelsPartway(TestAudio.Decoder(), async () =>
        {
            // What the Cancel endpoint does: the flag, then the token.
            await using var db = await factory.CreateDbContextAsync();
            (await db.EvpScanJobs.SingleAsync(j => j.Id == second)).CancelRequestedUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
            await cancel.CancelAsync();
        });
        await Runner(factory, decoder).RunAsync(second, cancel.Token);

        var job = await JobAsync(factory, second);
        Assert.Equal(EvpScanJobStatus.Canceled, job.Status);
        Assert.Equal(EvpScanJobStatus.Canceled, job.Segments.Single().Status);
        await using var check = await factory.CreateDbContextAsync();
        Assert.Equal(2, await check.AudioMarkers.CountAsync(m => m.UploadFileId == fileId && m.ReviewStatus == EvpReviewStatus.Pending));
    }

    /// <summary>A shutdown is not a cancel: the job is left running for the restart tidy-up to explain.</summary>
    [Fact]
    public async Task A_shutdown_leaves_the_job_for_the_restart_to_explain()
    {
        var factory = CreateFactory();
        var (fileId, ownerId) = await SeedFileAsync(factory);
        var jobId = await SeedJobAsync(factory, fileId, ownerId);
        using var stopping = new CancellationTokenSource();
        var decoder = new CancelsPartway(TestAudio.Decoder(), () => stopping.CancelAsync());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Runner(factory, decoder).RunAsync(jobId, stopping.Token));

        Assert.Equal(EvpScanJobStatus.Running, (await JobAsync(factory, jobId)).Status);
    }

    /// <summary>
    /// While a scan runs, its progress reaches the table: how much is read, how much is settled,
    /// and a percent for the job, weighted by length. That is all the editor has to draw from.
    /// </summary>
    [Fact]
    public async Task Progress_is_written_while_the_scan_runs()
    {
        var factory = CreateFactory();
        var (fileId, ownerId) = await SeedFileAsync(factory);
        var jobId = await SeedJobAsync(factory, fileId, ownerId);
        var seen = new List<(int Job, int Segment, double Processed, double Settled, double? Total)>();

        var interval = EvpScanRunner.ProgressInterval;
        EvpScanRunner.ProgressInterval = TimeSpan.FromMilliseconds(20);
        try
        {
            // Twenty pieces of a second each, slowed so the writer gets several looks in.
            var run = Runner(factory, new Slowly(TestAudio.Decoder(), TimeSpan.FromMilliseconds(60), 16_000)).RunAsync(jobId, default);
            while (!run.IsCompleted)
            {
                await Task.Delay(15);
                var job = await JobAsync(factory, jobId);
                var segment = job.Segments.Single();
                if (segment.FinishedUtc is null)
                    seen.Add((job.Percent, segment.Percent, segment.ProcessedSeconds, segment.SettledSeconds, segment.TotalSeconds));
            }
            await run;
        }
        finally { EvpScanRunner.ProgressInterval = interval; }

        var midway = seen.Where(p => p.Segment is > 0 and < 100).ToList();
        Assert.True(midway.Count > 0, $"no progress between 0 and 100 was ever written; saw {seen.Count} looks");
        Assert.All(midway, p =>
        {
            Assert.Equal(20.0, p.Total!.Value, 2);
            Assert.True(p.Settled <= p.Processed);
        });
        // One recording: the job's percent is its percent. Not asserted at every look: the
        // in-memory provider applies one save's rows one at a time, so a look can land between
        // the segment and the job (failed once in a full run, 10/26/2026). SQL Server commits them together.
        Assert.Contains(midway, p => p.Segment == p.Job);
    }

    /// <summary>Hands the audio over in small pieces, waiting between them.</summary>
    private sealed class Slowly(IAudioDecoder inner, TimeSpan pause, int pieceSamples) : IAudioDecoder
    {
        public bool IsAvailable => inner.IsAvailable;
        public bool CanDecode(string? contentType, string? fileName) => inner.CanDecode(contentType, fileName);

        public async IAsyncEnumerable<ReadOnlyMemory<float>> DecodeMono16kAsync(Stream source, string? contentType, string? fileName,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            await foreach (var chunk in inner.DecodeMono16kAsync(source, contentType, fileName, ct))
                for (var at = 0; at < chunk.Length; at += pieceSamples)
                {
                    await Task.Delay(pause, ct);
                    yield return chunk.Slice(at, Math.Min(pieceSamples, chunk.Length - at));
                }
        }

        public Task<float[]> ReadMono16kAsync(Stream s, string? t, string? n, CancellationToken ct) => inner.ReadMono16kAsync(s, t, n, ct);
        public Task<byte[]> DecodeRangeToWavAsync(Stream s, string? t, string? n, double a, double b, CancellationToken ct)
            => inner.DecodeRangeToWavAsync(s, t, n, a, b, ct);
        public Task<double?> DurationSecondsAsync(Stream s, string? t, string? n, CancellationToken ct) => inner.DurationSecondsAsync(s, t, n, ct);
    }

    /// <summary>Hands over the first piece of audio, then runs <paramref name="partway"/> before the next.</summary>
    private sealed class CancelsPartway(IAudioDecoder inner, Func<Task> partway) : IAudioDecoder
    {
        public bool IsAvailable => inner.IsAvailable;
        public bool CanDecode(string? contentType, string? fileName) => inner.CanDecode(contentType, fileName);

        public async IAsyncEnumerable<ReadOnlyMemory<float>> DecodeMono16kAsync(Stream source, string? contentType, string? fileName,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            var first = true;
            await foreach (var chunk in inner.DecodeMono16kAsync(source, contentType, fileName, ct))
            {
                if (!first) await partway();
                first = false;
                ct.ThrowIfCancellationRequested();
                yield return chunk;
            }
        }

        public Task<float[]> ReadMono16kAsync(Stream s, string? t, string? n, CancellationToken ct) => inner.ReadMono16kAsync(s, t, n, ct);
        public Task<byte[]> DecodeRangeToWavAsync(Stream s, string? t, string? n, double a, double b, CancellationToken ct)
            => inner.DecodeRangeToWavAsync(s, t, n, a, b, ct);
        public Task<double?> DurationSecondsAsync(Stream s, string? t, string? n, CancellationToken ct) => inner.DurationSecondsAsync(s, t, n, ct);
    }

    [Fact]
    public async Task A_deleted_recording_says_so()
    {
        var factory = CreateFactory();
        var jobId = await SeedJobAsync(factory, Guid.NewGuid(), Guid.NewGuid());

        await Runner(factory).RunAsync(jobId, default);

        var job = await JobAsync(factory, jobId);
        Assert.Equal(EvpScanJobStatus.Failed, job.Status);
        Assert.Contains("deleted", job.Error);
    }

    [Fact]
    public async Task A_job_already_taken_is_not_run_twice()
    {
        var factory = CreateFactory();
        var (fileId, ownerId) = await SeedFileAsync(factory);
        var jobId = await SeedJobAsync(factory, fileId, ownerId, status: EvpScanJobStatus.Running);

        await Runner(factory).RunAsync(jobId, default);

        Assert.Equal(EvpScanJobStatus.Running, (await JobAsync(factory, jobId)).Status);
    }

    [Fact]
    public void The_jobs_percent_weights_each_recording_by_its_length()
    {
        var job = new EvpScanJob();
        job.Segments.Add(new EvpScanJobSegment { TotalSeconds = 300, FinishedUtc = DateTime.UtcNow });   // 100%
        job.Segments.Add(new EvpScanJobSegment { TotalSeconds = 100, Percent = 50 });

        Assert.Equal(87, EvpScanRunner.JobPercent(job));   // (300×100 + 100×50) / 400
    }

    // ── The worker after a restart ────────────────────────────────────────────

    [Fact]
    public async Task A_scan_running_when_the_server_restarted_is_marked_failed_and_says_why()
    {
        var factory = CreateFactory();
        var (fileId, ownerId) = await SeedFileAsync(factory);
        var jobId = await SeedJobAsync(factory, fileId, ownerId, status: EvpScanJobStatus.Running);

        await new EvpScanWorker(new Mock<IServiceScopeFactory>().Object, factory, new EvpScanQueue(),
            NullLogger<EvpScanWorker>.Instance).RecoverAsync(default);

        var job = await JobAsync(factory, jobId);
        Assert.Equal((EvpScanJobStatus.Failed, EvpScanWorker.RestartedSentence), (job.Status, job.Error));
        Assert.NotNull(job.Segments.Single().FinishedUtc);   // frees the recording for the next scan
    }

    // ── The API ───────────────────────────────────────────────────────────────

    private static EvpScanJobController Api(IDbContextFactory<BenDataContext> factory, Guid userId, EvpScanQueue? queue = null,
        string? ifNoneMatch = null)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Bearer")),
        };
        if (ifNoneMatch is not null) context.Request.Headers.IfNoneMatch = ifNoneMatch;
        return new EvpScanJobController(factory, TestAudio.Decoder(), queue ?? new EvpScanQueue())
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };
    }

    private static StartScanJobRequest Medium => new(EvpSensitivity.Medium);

    [Fact]
    public async Task Starting_a_scan_queues_it_and_rings_the_worker()
    {
        var factory = CreateFactory();
        var (fileId, ownerId) = await SeedFileAsync(factory);
        var queue = new EvpScanQueue();

        var result = await Api(factory, ownerId, queue).Start(fileId, Medium, default);

        var record = Assert.IsType<EvpScanJobRecord>(Assert.IsType<AcceptedResult>(result.Result).Value);
        Assert.Equal(EvpScanJobStatus.Queued, record.Status);
        Assert.Equal(fileId, record.Segments.Single().UploadFileId);
        using var rang = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await queue.WaitAsync(TimeSpan.FromSeconds(5), rang.Token);
        Assert.False(rang.IsCancellationRequested, "the worker was never woken");
    }

    [Fact]
    public async Task A_second_scan_of_the_same_recording_waits_for_the_first()
    {
        var factory = CreateFactory();
        var (fileId, ownerId) = await SeedFileAsync(factory);
        await Api(factory, ownerId).Start(fileId, Medium, default);

        var again = await Api(factory, ownerId).Start(fileId, Medium, default);

        Assert.Equal(EvpScanJobController.AlreadyRunningSentence, Assert.IsType<ConflictObjectResult>(again.Result).Value);
    }

    [Fact]
    public async Task Nobody_may_have_more_than_three_scans_waiting()
    {
        var factory = CreateFactory();
        var me = Guid.NewGuid();
        for (var i = 0; i < EvpScanJobController.MaxLivePerPerson; i++)
            await SeedJobAsync(factory, (await SeedFileAsync(factory)).FileId, me);
        var (another, _) = await SeedFileAsync(factory);
        await using (var db = await factory.CreateDbContextAsync())
        {
            (await db.UploadFiles.SingleAsync(f => f.Id == another)).IsPublic = true;
            await db.SaveChangesAsync();
        }

        var result = await Api(factory, me).Start(another, Medium, default);

        Assert.Equal(StatusCodes.Status429TooManyRequests, Assert.IsType<ObjectResult>(result.Result).StatusCode);
    }

    [Fact]
    public async Task A_recording_the_server_cannot_read_is_refused_before_it_is_queued()
    {
        var factory = CreateFactory();
        var (fileId, ownerId) = await SeedFileAsync(factory, "audio/mp4", new byte[64]);

        var result = await Api(factory, ownerId).Start(fileId, Medium, default);

        var refusal = Assert.IsType<ScanRefusalRecord>(Assert.IsType<BadRequestObjectResult>(result.Result).Value);
        Assert.Equal(CompositeAudioDecoder.UnavailableSentence, refusal.Message);
        await using var db = await factory.CreateDbContextAsync();
        Assert.False(await db.EvpScanJobs.AnyAsync());
    }

    [Fact]
    public async Task Somebody_who_cannot_see_the_recording_cannot_scan_it()
    {
        var factory = CreateFactory();
        var (fileId, _) = await SeedFileAsync(factory);

        Assert.IsType<ForbidResult>((await Api(factory, Guid.NewGuid()).Start(fileId, Medium, default)).Result);
    }

    [Fact]
    public async Task Only_the_person_who_started_a_scan_can_see_or_stop_it()
    {
        var factory = CreateFactory();
        var (fileId, ownerId) = await SeedFileAsync(factory);
        var jobId = await SeedJobAsync(factory, fileId, ownerId);
        var stranger = Guid.NewGuid();

        Assert.IsType<NotFoundObjectResult>((await Api(factory, stranger).Get(jobId, default)).Result);
        Assert.IsType<NotFoundObjectResult>(await Api(factory, stranger).Cancel(jobId, default));
        Assert.IsType<NoContentResult>((await Api(factory, stranger).Live(fileId, default)).Result);
    }

    /// <summary>The editor asks every second and a half; nothing moved is a 304 with no body.</summary>
    [Fact]
    public async Task An_unchanged_scan_answers_not_modified()
    {
        var factory = CreateFactory();
        var (fileId, ownerId) = await SeedFileAsync(factory);
        var jobId = await SeedJobAsync(factory, fileId, ownerId);
        var first = Api(factory, ownerId);
        Assert.IsType<OkObjectResult>((await first.Get(jobId, default)).Result);
        var etag = first.Response.Headers.ETag.ToString();

        var second = await Api(factory, ownerId, ifNoneMatch: etag).Get(jobId, default);

        Assert.Equal(StatusCodes.Status304NotModified, Assert.IsType<StatusCodeResult>(second.Result).StatusCode);

        await Runner(factory).RunAsync(jobId, default);
        Assert.IsType<OkObjectResult>((await Api(factory, ownerId, ifNoneMatch: etag).Get(jobId, default)).Result);
    }

    [Fact]
    public async Task A_queued_scan_stops_at_once_when_canceled_and_frees_the_recording()
    {
        var factory = CreateFactory();
        var (fileId, ownerId) = await SeedFileAsync(factory);
        var jobId = await SeedJobAsync(factory, fileId, ownerId);

        Assert.IsType<NoContentResult>(await Api(factory, ownerId).Cancel(jobId, default));

        var job = await JobAsync(factory, jobId);
        Assert.Equal(EvpScanJobStatus.Canceled, job.Status);
        Assert.NotNull(job.Segments.Single().FinishedUtc);
        Assert.IsType<AcceptedResult>((await Api(factory, ownerId).Start(fileId, Medium, default)).Result);
    }

    [Fact]
    public async Task A_reopened_editor_finds_the_scan_still_running()
    {
        var factory = CreateFactory();
        var (fileId, ownerId) = await SeedFileAsync(factory);
        var jobId = await SeedJobAsync(factory, fileId, ownerId, status: EvpScanJobStatus.Running);

        var live = Assert.IsType<EvpScanJobRecord>(Assert.IsType<OkObjectResult>((await Api(factory, ownerId).Live(fileId, default)).Result).Value);

        Assert.Equal(jobId, live.Id);
    }

    // ── The weekly sweep ──────────────────────────────────────────────────────

    [Fact]
    public async Task Jobs_finished_over_a_week_ago_are_deleted_and_nothing_else()
    {
        var factory = CreateFactory();
        var (fileId, ownerId) = await SeedFileAsync(factory);
        var old    = await SeedJobAsync(factory, fileId, ownerId, EvpScanJobStatus.Done, DateTime.UtcNow.AddDays(-8));
        var recent = await SeedJobAsync(factory, fileId, ownerId, EvpScanJobStatus.Done, DateTime.UtcNow.AddDays(-6));
        var (other, _) = await SeedFileAsync(factory);
        var live   = await SeedJobAsync(factory, other, ownerId);

        await new EvpScanJobRetentionJob(factory, NullLogger<EvpScanJobRetentionJob>.Instance).RunAsync(default);

        await using var db = await factory.CreateDbContextAsync();
        var left = await db.EvpScanJobs.Select(j => j.Id).ToListAsync();
        Assert.DoesNotContain(old, left);
        Assert.Contains(recent, left);
        Assert.Contains(live, left);
        Assert.False(await db.EvpScanJobSegments.AnyAsync(s => s.EvpScanJobId == old));
    }
}
