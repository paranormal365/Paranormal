using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Audio;
using Ben.Data.WebApi.Services.Audio.Scanning;
using Ben.Data.WebApi.Services.Audio.Voice;
using Ben.Service.Models.Entities;
using Ben.Web.Tests.Controllers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// VoiceNet as the scan's second stage (audio phase 9b, 10/27/2026): each candidate gets how voice-shaped
/// it is, beside the detector's score and never instead of it.
/// </summary>
public sealed class VoiceNetTests
{
    private const int Rate = VoiceScoring.Rate;

    // ── Windows ───────────────────────────────────────────────────────────────

    [Fact]
    public void A_long_span_is_covered_by_one_second_windows_a_quarter_second_apart_to_its_end()
    {
        var starts = VoiceScoring.WindowStarts(2 * Rate, 3 * Rate + Rate / 10, 60 * Rate);

        Assert.Equal(2 * Rate, starts[0]);
        Assert.All(starts.Zip(starts.Skip(1)).Take(starts.Count - 2), p => Assert.Equal(VoiceScoring.HopSamples, p.Second - p.First));
        Assert.Equal(3 * Rate + Rate / 10 - VoiceScoring.WindowSamples, starts[^1]);   // the last one ends where the span ends
    }

    [Fact]
    public void A_short_span_is_measured_in_the_second_centred_on_it_inside_the_recording()
    {
        Assert.Equal([10 * Rate - Rate / 4], VoiceScoring.WindowStarts(10 * Rate, 10 * Rate + Rate / 2, 60 * Rate));
        Assert.Equal([0L], VoiceScoring.WindowStarts(0, Rate / 10, 60 * Rate));                // never before the start
        Assert.Equal([59L * Rate], VoiceScoring.WindowStarts(60 * Rate - 10, 60 * Rate, 60 * Rate)); // nor past the end
        Assert.Equal([0L], VoiceScoring.WindowStarts(0, Rate / 2, Rate / 2));                  // a recording under a second
    }

    // ── Measuring ─────────────────────────────────────────────────────────────

    /// <summary>A stand-in model: a window's reading is its loudest sample, so the test knows the answer.</summary>
    private sealed class LoudestSample : IVoiceScorer
    {
        public int Calls;
        public bool IsAvailable => true;
        public string? ModelName => "test-model";
        public string? UnavailableReason => null;
        public float[] Score(IReadOnlyList<float[]> windows)
        {
            Calls++;
            return windows.Select(w => w.Max(Math.Abs)).ToArray();
        }
    }

    private static float[] Silence(double seconds) => new float[(int)(seconds * Rate)];

    private static async IAsyncEnumerable<ReadOnlyMemory<float>> InPieces(float[] samples, int size)
    {
        for (var at = 0; at < samples.Length; at += size)
            yield return samples.AsMemory(at, Math.Min(size, samples.Length - at));
        await Task.CompletedTask;
    }

    [Theory]
    [InlineData(7)]
    [InlineData(65_536)]
    public async Task A_candidate_takes_its_loudest_window_however_the_audio_arrives(int pieceSize)
    {
        var audio = Silence(30);
        audio[(int)(12.6 * Rate)] = 0.8f;     // a moment of "voice" inside the first span
        audio[(int)(20.2 * Rate)] = 0.3f;     // and a quieter one inside the second
        audio[(int)(25.0 * Rate)] = 0.9f;     // outside both: must not count

        var readings = await VoiceScoring.MeasureAsync(InPieces(audio, pieceSize),
            [(10.0, 14.0), (19.5, 20.5)], 30, new LoudestSample(), default);

        Assert.Equal(0.8f, readings[0]);
        Assert.Equal(0.3f, readings[1]);
    }

    [Fact]
    public async Task Overlapping_spans_each_get_their_own_reading()
    {
        var audio = Silence(10);
        audio[(int)(4.5 * Rate)] = 0.7f;

        var readings = await VoiceScoring.MeasureAsync(InPieces(audio, 4096),
            [(3.0, 5.0), (4.0, 8.0), (6.0, 9.0)], 10, new LoudestSample(), default);

        Assert.Equal([0.7f, 0.7f, 0f], readings);
    }

    // ── In the scan ───────────────────────────────────────────────────────────

    [Fact]
    public async Task A_scan_stores_each_candidates_voice_reading_and_the_model_that_made_it()
    {
        var factory = CreateFactory();
        var (fileId, ownerId) = await SeedFileAsync(factory);
        var scorer = new LoudestSample();

        var (outcome, problem) = await Service(factory, scorer).RunAsync(await FileAsync(factory, fileId),
            EvpSensitivity.Medium, EvpDetectionOptions.FromSensitivity(EvpSensitivity.Medium), ownerId, false, null, default);

        Assert.Null(problem);
        Assert.NotEmpty(outcome!.Markers.Fresh);
        Assert.Equal(1, scorer.Calls);
        await using var db = await factory.CreateDbContextAsync();
        var markers = await db.AudioMarkers.Where(m => m.UploadFileId == fileId).ToListAsync();
        Assert.All(markers, m => Assert.InRange(m.VoiceLikelihood!.Value, 0.01f, 1f));
        var scan = await db.EvpScans.Include(s => s.Candidates).SingleAsync(s => s.UploadFileId == fileId);
        Assert.Equal("test-model", scan.VoiceModel);
        Assert.All(scan.Candidates, c => Assert.NotNull(c.VoiceLikelihood));
        // Features 8 and 9 come from the same second reading (phase 11); the fixture's utterances are voiced.
        Assert.All(scan.Candidates, c => Assert.InRange(c.VoicedFrameFraction!.Value, 0.5f, 1f));
        Assert.All(scan.Candidates, c => Assert.InRange(c.SpectralFlatness!.Value, 0f, 1f));
        Assert.Equal(markers.OrderBy(m => m.TimeSeconds).Select(m => m.VoiceLikelihood),
                     scan.Candidates.OrderBy(c => c.StartSeconds).Select(c => c.VoiceLikelihood));
    }

    [Fact]
    public async Task Voice_never_changes_what_the_detector_found_or_how_it_scored_it()
    {
        var with = CreateFactory();
        var (withFile, owner) = await SeedFileAsync(with);
        var without = CreateFactory();
        var (withoutFile, _) = await SeedFileAsync(without);
        var options = EvpDetectionOptions.FromSensitivity(EvpSensitivity.Medium);

        await Service(with, new LoudestSample()).RunAsync(await FileAsync(with, withFile), EvpSensitivity.Medium, options, owner, false, null, default);
        await Service(without, null).RunAsync(await FileAsync(without, withoutFile), EvpSensitivity.Medium, options, owner, false, null, default);

        Assert.Equal(await SpansAsync(without, withoutFile), await SpansAsync(with, withFile));
    }

    [Fact]
    public async Task Without_the_model_nothing_is_measured_and_the_scan_still_proposes()
    {
        var factory = CreateFactory();
        var (fileId, ownerId) = await SeedFileAsync(factory);

        var (outcome, _) = await Service(factory, new NullVoiceScorer(NullVoiceScorer.MissingModelReason)).RunAsync(
            await FileAsync(factory, fileId), EvpSensitivity.Medium, EvpDetectionOptions.FromSensitivity(EvpSensitivity.Medium),
            ownerId, false, null, default);

        Assert.NotEmpty(outcome!.Markers.Fresh);
        Assert.All(outcome.Markers.Fresh, m => Assert.Null(m.VoiceLikelihood));
        await using var db = await factory.CreateDbContextAsync();
        var scan = await db.EvpScans.Include(s => s.Candidates).SingleAsync();
        Assert.Null(scan.VoiceModel);
        // Features 8 and 9 need no model.
        Assert.All(scan.Candidates, c => Assert.NotNull(c.VoicedFrameFraction));
    }

    [Fact]
    public async Task A_model_that_fails_leaves_the_candidates_unmeasured_rather_than_failing_the_scan()
    {
        var factory = CreateFactory();
        var (fileId, ownerId) = await SeedFileAsync(factory);
        var broken = new Mock<IVoiceScorer>();
        broken.SetupGet(s => s.IsAvailable).Returns(true);
        broken.Setup(s => s.Score(It.IsAny<IReadOnlyList<float[]>>())).Throws(new InvalidOperationException("model broke"));

        var (outcome, problem) = await Service(factory, broken.Object).RunAsync(
            await FileAsync(factory, fileId), EvpSensitivity.Medium, EvpDetectionOptions.FromSensitivity(EvpSensitivity.Medium),
            ownerId, false, null, default);

        Assert.Null(problem);
        Assert.All(outcome!.Markers.Fresh, m => Assert.Null(m.VoiceLikelihood));
    }

    // ── The real model ────────────────────────────────────────────────────────

    private static string ModelPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "Ben.Data.WebApi", OnnxVoiceScorer.ModelRelativePath());
    }

    [Fact]
    public void The_committed_model_answers_once_per_window_between_0_and_1_and_batches_agree()
    {
        Assert.True(File.Exists(ModelPath()), "voicenet-v1.onnx is missing from Ben.Data.WebApi/Models.");
        using var scorer = new OnnxVoiceScorer(ModelPath());
        var random = new Random(253);
        var windows = Enumerable.Range(0, 70)   // more than one batch of 64
            .Select(i => Enumerable.Range(0, Rate).Select(_ => (float)(random.NextDouble() - 0.5) * (i % 3 == 0 ? 0.01f : 0.5f)).ToArray())
            .ToList();

        var together = scorer.Score(windows);
        var alone = scorer.Score([windows[66]]);

        Assert.Equal(70, together.Length);
        Assert.All(together, r => Assert.InRange(r, 0f, 1f));
        Assert.Equal(alone[0], together[66], 4);
    }

    [Fact]
    public void A_steady_tone_does_not_read_as_a_voice()
    {
        using var scorer = new OnnxVoiceScorer(ModelPath());
        // A 60 Hz hum and its harmonic: the room noise the detector is most often fooled by.
        var hum = Enumerable.Range(0, Rate)
            .Select(i => (float)(0.3 * Math.Sin(2 * Math.PI * 60 * i / Rate) + 0.1 * Math.Sin(2 * Math.PI * 120 * i / Rate)))
            .ToArray();
        Assert.True(scorer.Score([hum])[0] < 0.5f, "a steady hum read as voice-like");
    }

    // ── The world ─────────────────────────────────────────────────────────────

    private static IDbContextFactory<BenDataContext> CreateFactory() =>
        new PooledDbContextFactory<BenDataContext>(new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<(Guid FileId, Guid OwnerId)> SeedFileAsync(IDbContextFactory<BenDataContext> factory)
    {
        var fileId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var bytes = AudioMarkerControllerTests.BuildScannableWav();
        await using var db = await factory.CreateDbContextAsync();
        db.UploadFiles.Add(new UploadFile
        {
            Id = fileId, UploadFileTypeId = Guid.NewGuid(), AppUserId = ownerId, FileName = "evp.wav", StoredFileName = "s",
            ContentType = "audio/wav", FileSize = bytes.Length, FileData = bytes, DateCreated = DateTime.UtcNow, CreatedByAppUserId = ownerId,
        });
        await db.SaveChangesAsync();
        return (fileId, ownerId);
    }

    private static async Task<UploadFile> FileAsync(IDbContextFactory<BenDataContext> factory, Guid id)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.UploadFiles.AsNoTracking().SingleAsync(f => f.Id == id);
    }

    private static async Task<List<(double, double?, float?)>> SpansAsync(IDbContextFactory<BenDataContext> factory, Guid fileId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return (await db.AudioMarkers.Where(m => m.UploadFileId == fileId).OrderBy(m => m.TimeSeconds).ToListAsync())
            .Select(m => (m.TimeSeconds, m.EndSeconds, m.DetectionScore)).ToList();
    }

    private static EvpScanService Service(IDbContextFactory<BenDataContext> factory, IVoiceScorer? voice)
        => new(factory, new Mock<IFileStorageService>().Object, TestAudio.Decoder(),
               new EvpScanRecorder(factory, NullLogger<EvpScanRecorder>.Instance), NullLogger<EvpScanService>.Instance,
               questions: null, voice: voice);
}
