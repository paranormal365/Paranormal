using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Feed;
using Ben.Data.WebApi.Services.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// The automatic screener (item 186 F5b): the decision map, the model's input contract, and the
/// sweep that recovers whatever the inline path missed.
/// </summary>
/// <remarks>
/// The claim under protection is unchanged from F5: nothing reaches the public feed unscreened.
/// F5b adds a second claim with an opposite failure mode — the screener must not quietly wave
/// things through, and it must not quietly eat honest photos either. The decision-map boundary
/// tests carry the first; the real-model false-positive gate (skipped where the model is absent)
/// carries the second, in the spirit of the EVP detector's fixture gate.
/// </remarks>
public sealed class NsfwScreenerTests
{
    // ── The decision map, to the boundary ───────────────────────────────────

    [Theory]
    [InlineData(0.00, FeedMediaReviewState.Approved)]
    [InlineData(0.29, FeedMediaReviewState.Approved)]
    [InlineData(0.30, FeedMediaReviewState.Held)]     // review edge is inclusive
    [InlineData(0.50, FeedMediaReviewState.Held)]
    [InlineData(0.84, FeedMediaReviewState.Held)]
    [InlineData(0.85, FeedMediaReviewState.Held)]     // block edge is inclusive
    [InlineData(1.00, FeedMediaReviewState.Held)]
    public void Decision_map_is_asymmetric_and_inclusive_at_both_edges(
        double probability, FeedMediaReviewState expected)
    {
        Assert.Equal(expected, NsfwDecision.Decide(probability).State);
    }

    [Fact]
    public void Decision_never_returns_pending_because_deciding_means_having_looked()
    {
        foreach (var p in new[] { 0.0, 0.3, 0.5, 0.85, 1.0 })
            Assert.NotEqual(FeedMediaReviewState.Pending, NsfwDecision.Decide(p).State);
    }

    [Theory]
    [InlineData(0.05)]
    [InlineData(0.50)]
    [InlineData(0.99)]
    public void The_verdict_carries_the_score_so_the_spam_rule_need_not_parse_the_note(double p)
    {
        // Item 217 counts confident refusals per author from the stored number.
        Assert.Equal(p, NsfwDecision.Decide(p).Score);
    }

    [Fact]
    public void Blocked_and_borderline_reasons_are_distinguishable_for_the_queue()
    {
        Assert.Contains("blocked", NsfwDecision.Decide(0.99).Reason);
        Assert.Contains("borderline", NsfwDecision.Decide(0.5).Reason);
    }

    [Fact]
    public void Softmax_matches_hand_computed_values()
    {
        // Equal logits: exactly half.
        Assert.Equal(0.5, NsfwDecision.NsfwProbability([1.0f, 1.0f]), precision: 10);
        // logits [0, ln 3]: nsfw = 3/(1+3).
        Assert.Equal(0.75, NsfwDecision.NsfwProbability([0f, (float)Math.Log(3)]), precision: 5);
        // Large-logit stability: must not overflow into NaN.
        Assert.Equal(1.0, NsfwDecision.NsfwProbability([-500f, 500f]), precision: 10);
    }

    // ── The model's input contract (transcribed from preprocessor_config.json) ──

    [Fact]
    public void Preprocessing_normalizes_to_the_documented_range()
    {
        Assert.Equal(-1f, NsfwPreprocessing.Normalize(0));
        Assert.Equal(1f, NsfwPreprocessing.Normalize(255));
        Assert.Equal(0f, NsfwPreprocessing.Normalize(128), precision: 2);
    }

    [Fact]
    public void Preprocessing_produces_nchw_224_with_solid_color_where_expected()
    {
        using var bitmap = new SKBitmap(50, 30);
        using (var canvas = new SKCanvas(bitmap)) canvas.Clear(new SKColor(255, 0, 128));

        var tensor = NsfwPreprocessing.ToTensor(bitmap);

        Assert.Equal([1, 3, 224, 224], tensor.Dimensions.ToArray());
        // A solid-color source stays solid after resize: check corners and center per channel.
        foreach (var (y, x) in new[] { (0, 0), (223, 223), (112, 112) })
        {
            Assert.Equal(1f, tensor[0, 0, y, x], precision: 2);                   // R = 255
            Assert.Equal(-1f, tensor[0, 1, y, x], precision: 2);                  // G = 0
            Assert.Equal(NsfwPreprocessing.Normalize(128), tensor[0, 2, y, x], 2); // B = 128
        }
    }

    // ── ffmpeg argument shapes ──────────────────────────────────────────────

    [Fact]
    public void Frame_sampling_args_cap_the_frames_and_quote_the_paths()
    {
        var args = FrameSampling.SampleArgs("/in put/video.mp4", "/tmp/frames");
        Assert.Contains($"-frames:v {FrameSampling.MaxSampledFrames}", args);
        Assert.Contains("fps=1", args);
        Assert.Contains("\"/in put/video.mp4\"", args); // spaces survive
        Assert.Contains("sample_%03d.png", args);

        var last = FrameSampling.LastFrameArgs("/in/video.mp4", "/tmp/frames");
        Assert.Contains("-sseof -1", last);
        Assert.Contains("-frames:v 1", last);
    }

    // ── The screener's own judgment calls (stubbed inference, fake storage) ─
    // Paths handed to ScreenAsync are storage-root-RELATIVE and readable only through
    // IFileStorageService — the exact contract the first live run broke by treating the
    // relative path as a filesystem path. These tests enforce it by construction: the fake
    // storage is the only place the bytes exist.

    private sealed class FakeStorage : Ben.Data.Common.Interfaces.IFileStorageService
    {
        private readonly Dictionary<string, byte[]> _files = [];
        public FakeStorage Add(string relativePath, byte[] bytes) { _files[relativePath] = bytes; return this; }

        public bool Exists(string relativePath) => _files.ContainsKey(relativePath);
        public Task<Stream> OpenReadAsync(string relativePath, CancellationToken ct = default)
            => Task.FromResult<Stream>(new MemoryStream(_files[relativePath]));
        public Task WriteAsync(string relativePath, Stream data, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task DeleteAsync(string relativePath, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task DeleteDirectoryAsync(string relativeDirectory, CancellationToken ct = default) => Task.CompletedTask;
        public IReadOnlyList<string> ListFiles(string relativeDirectory)
            => throw new NotSupportedException();
        public string UserFilePath(Guid userId, string storedFileName) => $"users/{userId}/{storedFileName}";
        public string OrgFilePath(Guid orgId, string storedFileName) => $"orgs/{orgId}/{storedFileName}";
        public string CaseFilePath(Guid caseId, string storedFileName) => $"cases/{caseId}/{storedFileName}";
    }

    private static OnnxNsfwScreener BuildStubbed(
        double probability, FakeStorage storage, string? ffmpegPath = null)
        => new(_ => probability,
               new MediaToolOptions { FfmpegPath = ffmpegPath },
               storage,
               NullLogger<OnnxNsfwScreener>.Instance);

    [Fact]
    public async Task Clean_image_approves_with_the_score_in_the_note()
    {
        var storage = new FakeStorage().Add("users/u/photo.jpg", TestImages.Jpeg());
        var verdict = await BuildStubbed(0.02, storage)
            .ScreenAsync("users/u/photo.jpg", "image/jpeg", CancellationToken.None);
        Assert.Equal(FeedMediaReviewState.Approved, verdict.State);
        Assert.Contains("0.02", verdict.Reason);
    }

    [Fact]
    public async Task Undecodable_image_is_held_not_approved_and_not_stuck()
    {
        var storage = new FakeStorage().Add("users/u/junk.jpg", [0xDE, 0xAD, 0xBE, 0xEF]);
        var verdict = await BuildStubbed(0.0, storage)
            .ScreenAsync("users/u/junk.jpg", "image/jpeg", CancellationToken.None);
        Assert.Equal(FeedMediaReviewState.Held, verdict.State);
        Assert.Contains("decode", verdict.Reason);
    }

    [Fact]
    public async Task Missing_file_is_held_and_says_missing_not_undecodable()
    {
        // The regression that reached the live API: a healthy photo whose path was resolved
        // wrongly reported "would not decode". Missing must be its own, accurate message.
        var verdict = await BuildStubbed(0.0, new FakeStorage())
            .ScreenAsync("users/u/vanished.jpg", "image/jpeg", CancellationToken.None);
        Assert.Equal(FeedMediaReviewState.Held, verdict.State);
        Assert.Contains("missing", verdict.Reason);
    }

    [Fact]
    public async Task Video_without_ffmpeg_stays_pending_because_nobody_looked()
    {
        var storage = new FakeStorage().Add("users/u/clip.mp4", [0x00]);
        var verdict = await BuildStubbed(0.0, storage, ffmpegPath: null)
            .ScreenAsync("users/u/clip.mp4", "video/mp4", CancellationToken.None);
        Assert.Equal(FeedMediaReviewState.Pending, verdict.State);
        Assert.Contains("ffmpeg", verdict.Reason);
    }

    // ── The false-positive gate: the REAL model over the repo's neutral images ──
    // Skipped where the model is absent (CI, a fresh checkout) — screening degrades to manual
    // there, so there is nothing to gate. Where the model exists, every neutral generated image
    // must approve: a screener that quarantines slate-gray rectangles would bury the moderator
    // queue in noise and teach everyone to rubber-stamp it.

    /// <summary>Walks up from the test bin to the repo's Ben.Data.WebApi project directory.</summary>
    private static string? WebApiContentRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "Ben.Data.WebApi");
            if (Directory.Exists(candidate)) return candidate;
        }
        return null;
    }

    [Fact]
    public async Task Real_model_approves_the_neutral_fixture_images()
    {
        var contentRoot = WebApiContentRoot();
        if (contentRoot is null
            || !File.Exists(Path.Combine(contentRoot, OnnxNsfwScreener.ModelRelativePath)))
        {
            // Model not fetched on this machine (scripts/get-screener-model.sh) — screening runs
            // manual-only there, so there is nothing to gate. Deliberately a pass, not a failure:
            // a fresh checkout must build and test green.
            return;
        }

        var env = new Moq.Mock<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>();
        env.SetupGet(e => e.ContentRootPath).Returns(contentRoot);

        var storage = new FakeStorage()
            .Add("users/u/small.jpg", TestImages.Jpeg())
            .Add("users/u/large.jpg", TestImages.Jpeg(640, 480))
            .Add("users/u/gps.jpg", TestImages.JpegWithGps());

        using var screener = new OnnxNsfwScreener(
            env.Object,
            Microsoft.Extensions.Options.Options.Create(new MediaToolOptions()),
            storage,
            NullLogger<OnnxNsfwScreener>.Instance);

        foreach (var path in new[] { "users/u/small.jpg", "users/u/large.jpg", "users/u/gps.jpg" })
        {
            var verdict = await screener.ScreenAsync(path, "image/jpeg", CancellationToken.None);
            Assert.Equal(FeedMediaReviewState.Approved, verdict.State);
        }
    }

    // ── The sweep job ───────────────────────────────────────────────────────

    private sealed class SimpleFactory(DbContextOptions<BenDataContext> opts) : IDbContextFactory<BenDataContext>
    {
        public BenDataContext CreateDbContext() => new(opts);
        public Task<BenDataContext> CreateDbContextAsync(CancellationToken ct = default)
            => Task.FromResult(new BenDataContext(opts));
    }

    private sealed class ScriptedScreener(FeedMediaVerdict verdict, bool isAutomatic = true) : IFeedMediaScreener
    {
        public int Calls;
        public bool IsAutomatic => isAutomatic;
        public Task<FeedMediaVerdict> ScreenAsync(string storagePath, string? contentType, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(verdict);
        }
    }

    private static async Task<(IDbContextFactory<BenDataContext> Factory, Guid PostId)> SeedPendingAsync(
        TimeSpan age, FeedMediaReviewState state = FeedMediaReviewState.Pending)
    {
        var factory = new SimpleFactory(new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        Guid authorId = Guid.NewGuid(), postId = Guid.NewGuid(), fileId = Guid.NewGuid();
        await using var db = await ((IDbContextFactory<BenDataContext>)factory).CreateDbContextAsync();

        db.AppUsers.Add(new AppUser { Id = authorId, UserName = "s", Email = "s@t.dev", DisplayName = "S", Handle = "s" });
        db.UploadFiles.Add(new UploadFile
        {
            Id = fileId, AppUserId = authorId, FileName = "p.jpg", StoredFileName = "p.jpg",
            ContentType = "image/jpeg", StoragePath = "/tmp/p.jpg", FileSize = 1,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = authorId,
        });
        db.OrgMessages.Add(new OrgMessage
        {
            Id = postId, AuthorAppUserId = authorId, CreatedByAppUserId = authorId,
            Body = "post", IsPublic = true, ChannelType = OrgMessageChannel.PublicFeed,
            MediaUploadFileId = fileId, MediaReviewState = state,
            DateCreated = DateTime.UtcNow - age,
        });
        await db.SaveChangesAsync();
        return (factory, postId);
    }

    private static async Task<FeedMediaReviewState> StateOfAsync(IDbContextFactory<BenDataContext> factory, Guid id)
    {
        await using var db = await factory.CreateDbContextAsync();
        return (await db.OrgMessages.SingleAsync(m => m.Id == id)).MediaReviewState;
    }

    [Fact]
    public async Task Sweep_approves_old_pending_media()
    {
        var (factory, postId) = await SeedPendingAsync(TimeSpan.FromMinutes(10));
        var screener = new ScriptedScreener(new FeedMediaVerdict(FeedMediaReviewState.Approved, "screener: nsfw 0.01"));

        await new PendingMediaScreeningJob(factory, screener, NullLogger<PendingMediaScreeningJob>.Instance)
            .RunAsync(CancellationToken.None);

        Assert.Equal(1, screener.Calls);
        Assert.Equal(FeedMediaReviewState.Approved, await StateOfAsync(factory, postId));
    }

    [Fact]
    public async Task Sweep_leaves_fresh_posts_for_the_inline_path()
    {
        var (factory, postId) = await SeedPendingAsync(TimeSpan.FromSeconds(30));
        var screener = new ScriptedScreener(new FeedMediaVerdict(FeedMediaReviewState.Approved, "x"));

        await new PendingMediaScreeningJob(factory, screener, NullLogger<PendingMediaScreeningJob>.Instance)
            .RunAsync(CancellationToken.None);

        Assert.Equal(0, screener.Calls);
        Assert.Equal(FeedMediaReviewState.Pending, await StateOfAsync(factory, postId));
    }

    [Fact]
    public async Task Sweep_is_a_noop_under_the_manual_screener()
    {
        var (factory, _) = await SeedPendingAsync(TimeSpan.FromMinutes(10));
        var screener = new ScriptedScreener(
            new FeedMediaVerdict(FeedMediaReviewState.Pending, "manual"), isAutomatic: false);

        await new PendingMediaScreeningJob(factory, screener, NullLogger<PendingMediaScreeningJob>.Instance)
            .RunAsync(CancellationToken.None);

        Assert.Equal(0, screener.Calls);
    }

    [Fact]
    public async Task Sweep_does_not_touch_decided_media()
    {
        // A moderator already Held it; the sweep queries Pending only.
        var (factory, postId) = await SeedPendingAsync(TimeSpan.FromMinutes(10), FeedMediaReviewState.Held);
        var screener = new ScriptedScreener(new FeedMediaVerdict(FeedMediaReviewState.Approved, "x"));

        await new PendingMediaScreeningJob(factory, screener, NullLogger<PendingMediaScreeningJob>.Instance)
            .RunAsync(CancellationToken.None);

        Assert.Equal(0, screener.Calls);
        Assert.Equal(FeedMediaReviewState.Held, await StateOfAsync(factory, postId));
    }

    [Fact]
    public async Task Sweep_leaves_pending_verdicts_unrecorded_for_the_next_pass()
    {
        // A video on a host with no ffmpeg: the screener answers Pending; nothing is written.
        var (factory, postId) = await SeedPendingAsync(TimeSpan.FromMinutes(10));
        var screener = new ScriptedScreener(new FeedMediaVerdict(FeedMediaReviewState.Pending, "no ffmpeg"));

        await new PendingMediaScreeningJob(factory, screener, NullLogger<PendingMediaScreeningJob>.Instance)
            .RunAsync(CancellationToken.None);

        Assert.Equal(1, screener.Calls);
        Assert.Equal(FeedMediaReviewState.Pending, await StateOfAsync(factory, postId));
    }
}
