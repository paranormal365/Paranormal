using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Admin;
using Ben.Data.WebApi.Services.Audio;
using Ben.Data.WebApi.Services.Audio.Learning;
using Ben.Data.WebApi.Services.Audio.Scanning;
using Ben.Service.Models.Admin;
using Ben.Service.Models.Entities;
using Ben.Web.Tests.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The score learns from Keep and Dismiss (audio phase 13, 10/27/2026): fitting its three weights, the gate
/// a fit must pass, switching one on, and scans and Why? using it.
/// </summary>
public sealed class EvpScoreRefitTests
{
    // ── The weights ───────────────────────────────────────────────────────────

    [Fact]
    public void The_built_in_weights_are_exactly_the_version_one_score()
    {
        var random = new Random(13);
        for (var i = 0; i < 200; i++)
        {
            var f = new EvpFeatures(random.NextDouble() * 30, -random.NextDouble() * 20, random.NextDouble() * 15, 0, 0, 0, 0);
            Assert.Equal(EvpDetector.Score(f), EvpDetector.Score(f, EvpScoreWeights.BuiltIn));
            // The same numbers under another version's name take the general path and agree too.
            Assert.Equal(EvpDetector.Score(f), EvpDetector.Score(f, EvpScoreWeights.BuiltIn with { Version = 2 }), 3);
        }
    }

    [Fact]
    public void Why_adds_up_to_the_score_with_any_weights()
    {
        var w = new EvpScoreWeights(2, 0.2, 0.35, 0.45);
        var f = new EvpFeatures(14, -3, 0.9, 0, 0, 0, 0);
        Assert.Equal(EvpDetector.Score(f, w), EvpDetector.Explain(f, w).Total, 3);
    }

    // ── Ranking ───────────────────────────────────────────────────────────────

    [Fact]
    public void Ranking_is_measured_as_how_often_kept_outranks_dismissed()
    {
        Assert.Equal(1.0, EvpScoreRefit.Auc([(0.9, true, 1), (0.1, false, 1)]));
        Assert.Equal(0.0, EvpScoreRefit.Auc([(0.1, true, 1), (0.9, false, 1)]));
        Assert.Equal(0.5, EvpScoreRefit.Auc([(0.5, true, 1), (0.5, false, 1)]));
        // A decision made after listening counts twice: the one right pair outweighs the one wrong pair.
        Assert.Equal(2.0 / 3, EvpScoreRefit.Auc([(0.9, true, 2), (0.5, false, 1), (0.2, true, 1)]), 6);
        Assert.True(double.IsNaN(EvpScoreRefit.Auc([(0.9, true, 1)])));
    }

    // ── Fitting and the gate ──────────────────────────────────────────────────

    /// <summary>
    /// A world where the built-in weights rank badly: what people keep is quieter but speech-length, and
    /// what they dismiss is loud short clicks and long talking, all in the listening range.
    /// </summary>
    private static List<EvpDecision> Decisions(int count, int recordings, int seed = 7)
    {
        var random = new Random(seed);
        var ids = Recordings(recordings);
        return Enumerable.Range(0, count).Select(i =>
        {
            // Kept and dismissed alternate WITHIN each recording, so every recording, the held-back
            // ones included, has both. Alternating on i alone gave a recording only one kind
            // whenever the recording count was even.
            var kept = (i / recordings) % 2 == 0;
            var features = kept
                ? new EvpFeatures(6 + random.NextDouble() * 4, -1, 0.5 + random.NextDouble() * 1.5, 0, 0, 0, 0)
                : i % 4 == 1
                    ? new EvpFeatures(18 + random.NextDouble() * 7, -1, 0.05 + random.NextDouble() * 0.1, 0, 0, 0, 0)
                    : new EvpFeatures(16 + random.NextDouble() * 6, -1, 12 + random.NextDouble() * 8, 0, 0, 0, 0);
            return new EvpDecision(ids[i % recordings], features, kept, i % 3 == 0 ? 2 : 1);
        }).ToList();
    }

    /// <summary>
    /// Recording ids that are the same on every run, with exactly every fifth one held back.
    /// </summary>
    /// <remarks>
    /// <see cref="EvpScoreRefit.IsHeldOut"/> decides from the id, and these tests used
    /// <c>Guid.NewGuid()</c>, so each run split the decisions differently. One run in about
    /// eighteen held back recordings with only kept decisions or only dismissed ones, and the gate
    /// refused with "no kept and dismissed decisions to test on" (measured over 1,000 runs,
    /// 10/27/2026). The first byte decides the split, so it is the recording's number.
    /// </remarks>
    private static List<Guid> Recordings(int count)
        => [.. Enumerable.Range(0, count).Select(r =>
        {
            var bytes = new byte[16];
            bytes[0] = (byte)r;
            bytes[15] = 0xE7;
            return new Guid(bytes);
        })];

    [Fact]
    public void The_test_recordings_hold_back_every_fifth()
        => Assert.Equal([0, 5, 10, 15, 20, 25],
                        Recordings(30).Select((id, r) => (id, r)).Where(x => EvpScoreRefit.IsHeldOut(x.id)).Select(x => x.r));

    [Fact]
    public void A_fit_learns_what_people_keep_and_passes_its_gate()
    {
        var fit = EvpScoreRefit.Fit(Decisions(600, 30), version: 2)!;

        Assert.True(fit.PassedGate, fit.GateNote);
        Assert.Equal(2, fit.Weights.Version);
        Assert.Equal(1.0, fit.Weights.Prominence + fit.Weights.Band + fit.Weights.Duration, 6);
        Assert.True(fit.Weights.Duration > EvpScoreWeights.BuiltIn.Duration, $"length should count for more: {fit.Weights}");
        Assert.True(fit.HeldOutAuc >= fit.BaselineHeldOutAuc + EvpScoreRefit.MinimumImprovement);
        Assert.True(fit.HeldOutDecisions > 0);
        Assert.True(EvpScoreRefit.KeepsTheExpectedOrder(fit.Weights));
    }

    [Fact]
    public void Too_few_decisions_or_recordings_never_pass()
    {
        var few = EvpScoreRefit.Fit(Decisions(100, 30), 2)!;
        Assert.False(few.PassedGate);
        Assert.Contains("500 decisions", few.GateNote);

        var narrow = EvpScoreRefit.Fit(Decisions(600, 10), 2)!;
        Assert.False(narrow.PassedGate);
        Assert.Contains("20 recordings", narrow.GateNote);
    }

    [Fact]
    public void A_fit_no_better_than_the_built_in_weights_does_not_pass()
    {
        // People keep exactly what the built-in score already ranks first.
        var random = new Random(3);
        var ids = Recordings(30);
        var decisions = Enumerable.Range(0, 600).Select(i =>
        {
            var f = new EvpFeatures(random.NextDouble() * 25, -random.NextDouble() * 12, random.NextDouble() * 4, 0, 0, 0, 0);
            return new EvpDecision(ids[i % 30], f, EvpDetector.Score(f) > 60);
        }).ToList();

        var fit = EvpScoreRefit.Fit(decisions, 2)!;
        Assert.False(fit.PassedGate);
        Assert.Contains("Not better", fit.GateNote);
    }

    [Fact]
    public void Weights_that_put_a_click_above_a_word_are_refused()
    {
        Assert.True(EvpScoreRefit.KeepsTheExpectedOrder(EvpScoreWeights.BuiltIn));
        Assert.False(EvpScoreRefit.KeepsTheExpectedOrder(new EvpScoreWeights(2, 1, 0, 0)));
    }

    [Fact]
    public void The_held_back_recordings_are_always_the_same_ones()
    {
        var id = Guid.NewGuid();
        Assert.Equal(EvpScoreRefit.IsHeldOut(id), EvpScoreRefit.IsHeldOut(new Guid(id.ToString())));
        // Seeded, so the share is the same number on every run rather than a draw.
        var random = new Random(11);
        var share = Enumerable.Range(0, 5000).Count(_ =>
        {
            var bytes = new byte[16];
            random.NextBytes(bytes);
            return EvpScoreRefit.IsHeldOut(new Guid(bytes));
        }) / 5000.0;
        Assert.InRange(share, 0.15, 0.25);
    }

    // ── The database side ─────────────────────────────────────────────────────

    private static IDbContextFactory<BenDataContext> CreateFactory() =>
        new PooledDbContextFactory<BenDataContext>(new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task Decisions_count_each_candidate_once_by_its_latest_ruling()
    {
        var factory = CreateFactory();
        var file = Guid.NewGuid();
        var kept = Guid.NewGuid();
        var changed = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var scan = new EvpScan { Id = Guid.NewGuid(), UploadFileId = file, DateCreated = DateTime.UtcNow };
            scan.Candidates.Add(new EvpScanCandidate { Id = Guid.NewGuid(), AudioMarkerId = kept, PeakProminenceDb = 10, EventSeconds = 1 });
            scan.Candidates.Add(new EvpScanCandidate { Id = Guid.NewGuid(), AudioMarkerId = changed, PeakProminenceDb = 20, EventSeconds = 1 });
            db.EvpScans.Add(scan);
            var at = DateTime.UtcNow;
            db.EvpRulings.AddRange(
                new EvpRuling { Id = Guid.NewGuid(), UploadFileId = file, AudioMarkerId = kept, Ruling = EvpReviewStatus.Confirmed, PlayedFirst = true, DateCreated = at },
                new EvpRuling { Id = Guid.NewGuid(), UploadFileId = file, AudioMarkerId = changed, Ruling = EvpReviewStatus.Confirmed, DateCreated = at },
                new EvpRuling { Id = Guid.NewGuid(), UploadFileId = file, AudioMarkerId = changed, Ruling = EvpReviewStatus.Dismissed, DateCreated = at.AddMinutes(1) });
            await db.SaveChangesAsync();
        }

        await using var read = await factory.CreateDbContextAsync();
        var decisions = await EvpScoring.DecisionsAsync(read, default);

        Assert.Equal(2, decisions.Count);
        Assert.Contains(decisions, d => d.Kept && d.Weight == 2 && d.Features.PeakProminenceDb == 10);
        Assert.Contains(decisions, d => !d.Kept && d.Weight == 1 && d.Features.PeakProminenceDb == 20);
    }

    private static AdminEvpController Admin(IDbContextFactory<BenDataContext> factory) => new(factory)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "Bearer")),
            },
        },
    };

    private static async Task SeedFitsAsync(IDbContextFactory<BenDataContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        db.EvpScoringVersions.AddRange(
            new EvpScoringVersion { Id = Guid.NewGuid(), Version = 2, WeightProminence = 0.2, WeightBand = 0.3, WeightDuration = 0.5, PassedGate = true, GateNote = "Better", DateCreated = DateTime.UtcNow },
            new EvpScoringVersion { Id = Guid.NewGuid(), Version = 3, WeightProminence = 1, PassedGate = false, GateNote = "Not better", DateCreated = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Only_a_fit_that_passed_can_be_switched_on_and_the_built_in_weights_come_back()
    {
        var factory = CreateFactory();
        await SeedFitsAsync(factory);
        var scoring = new EvpScoring(factory, NullLogger<EvpScoring>.Instance);

        Assert.IsType<ConflictObjectResult>(await Admin(factory).Activate(3, default));
        Assert.Same(EvpScoreWeights.BuiltIn, await scoring.ActiveAsync(default));

        Assert.IsType<NoContentResult>(await Admin(factory).Activate(2, default));
        Assert.Equal(new EvpScoreWeights(2, 0.2, 0.3, 0.5), await scoring.ActiveAsync(default));
        var state = Assert.IsType<AdminEvpScoring>(Assert.IsType<OkObjectResult>((await Admin(factory).Scoring(default)).Result).Value);
        Assert.Equal(2, state.ActiveVersion);
        Assert.Equal(EvpScoreRefit.MinimumDecisions, state.MinimumDecisions);

        Assert.IsType<NoContentResult>(await Admin(factory).UseBuiltIn(default));
        Assert.Same(EvpScoreWeights.BuiltIn, await scoring.ActiveAsync(default));
    }

    [Fact]
    public async Task A_scan_uses_the_switched_on_weights_and_says_which()
    {
        var factory = CreateFactory();
        await SeedFitsAsync(factory);
        await Admin(factory).Activate(2, default);
        var bytes = AudioMarkerControllerTests.BuildScannableWav();
        var file = new UploadFile
        {
            Id = Guid.NewGuid(), UploadFileTypeId = Guid.NewGuid(), AppUserId = Guid.NewGuid(), FileName = "evp.wav", StoredFileName = "s",
            ContentType = "audio/wav", FileSize = bytes.Length, FileData = bytes, DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid(),
        };
        await using (var db = await factory.CreateDbContextAsync()) { db.UploadFiles.Add(file); await db.SaveChangesAsync(); }

        var service = new EvpScanService(factory, new Mock<IFileStorageService>().Object, TestAudio.Decoder(),
            new EvpScanRecorder(factory, NullLogger<EvpScanRecorder>.Instance), NullLogger<EvpScanService>.Instance,
            scoring: new EvpScoring(factory, NullLogger<EvpScoring>.Instance));
        await service.RunAsync(file, EvpSensitivity.Medium, EvpDetectionOptions.FromSensitivity(EvpSensitivity.Medium),
            file.AppUserId!.Value, false, null, default);

        await using var check = await factory.CreateDbContextAsync();
        var scan = await check.EvpScans.Include(s => s.Candidates).SingleAsync();
        Assert.Equal(2, scan.ScoringVersion);
        var w = new EvpScoreWeights(2, 0.2, 0.3, 0.5);
        Assert.All(scan.Candidates, c => Assert.Equal(
            EvpDetector.Score(new EvpFeatures(c.PeakProminenceDb, c.MeanBandGapDb, c.EventSeconds, 0, 0, 0, 0), w), c.Score, 3));
        Assert.All(scan.Candidates, c => Assert.NotEqual(
            EvpDetector.Score(new EvpFeatures(c.PeakProminenceDb, c.MeanBandGapDb, c.EventSeconds, 0, 0, 0, 0)), c.Score));
    }

    [Fact]
    public async Task Fitting_with_nothing_to_learn_from_records_nothing_and_says_so()
    {
        var factory = CreateFactory();
        var answer = await Admin(factory).FitNow(new EvpScoring(factory, NullLogger<EvpScoring>.Instance), default);
        Assert.Contains("Nothing to learn from", Assert.IsType<ConflictObjectResult>(answer).Value?.ToString());
        await using var db = await factory.CreateDbContextAsync();
        Assert.Empty(db.EvpScoringVersions);
    }
}
