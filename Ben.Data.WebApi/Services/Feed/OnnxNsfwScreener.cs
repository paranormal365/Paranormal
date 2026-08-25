using System.Diagnostics;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace Ben.Data.WebApi.Services.Feed;

/// <summary>
/// The automatic screener (item 186 F5b): an on-device NSFW image classifier over every photo
/// and every sampled video frame, before anything can appear on the public feed.
/// </summary>
/// <remarks>
/// <para><b>Why a local model.</b> Ben's requirements, both of them: "I don't want a website full
/// of porn" and, from item 184, private-residence footage must not leak. A classifier running
/// in-process satisfies both at once — nothing leaves the server, there is no per-call cost, and
/// screening cannot be down because a third party is. The model is the ONNX export of
/// <c>Falconsai/nsfw_image_detection</c> (ViT, Apache-2.0, two classes: normal/nsfw), fetched by
/// <c>scripts/get-screener-model.sh</c> rather than committed — 87 MB has no business in git
/// history, and a missing model degrades to <see cref="ManualReviewScreener"/> behaviour loudly
/// rather than failing anything.</para>
///
/// <para><b>The decision is deliberately asymmetric.</b> Approving is the irreversible act — a
/// photo that appeared cannot be unseen — so only a confidently-clean score approves. Everything
/// from "probably fine but not sure" upward goes to <see cref="FeedMediaReviewState.Held"/>, where
/// the cost is a moderator's minute, not the site's reputation. The thresholds live in
/// <see cref="NsfwDecision"/> as constants with the tuning story documented there.</para>
///
/// <para><b>Fail-closed, inherited.</b> The seam's contract (see <see cref="IFeedMediaScreener"/>)
/// already guarantees a throwing screener leaves media Pending and unserved. This class therefore
/// throws freely on the truly unexpected and reserves its own judgment for states it understands:
/// an undecodable image is Held (a person should look at a file that pretends to be an image and
/// will not decode), and a video on a host with no ffmpeg stays Pending with a reason (we did not
/// look, and Pending is the only honest word for that —
/// <see cref="Scheduling.PendingMediaScreeningJob"/> retries once a tool is configured).</para>
/// </remarks>
public sealed class OnnxNsfwScreener : IFeedMediaScreener, IDisposable
{
    /// <summary>Where the model lives, relative to content root. One convention, no config.</summary>
    public const string ModelRelativePath = "Models/nsfw/model_quantized.onnx";

    private readonly Lazy<InferenceSession> _session;
    private readonly MediaToolOptions _mediaTools;
    private readonly IFileStorageService _storage;
    private readonly ILogger<OnnxNsfwScreener> _logger;
    private readonly Func<SKBitmap, double>? _inferenceOverride; // tests only

    public bool IsAutomatic => true;

    public OnnxNsfwScreener(
        IWebHostEnvironment environment,
        IOptions<MediaToolOptions> mediaTools,
        IFileStorageService storage,
        ILogger<OnnxNsfwScreener> logger)
    {
        var modelPath = Path.Combine(environment.ContentRootPath, ModelRelativePath);
        _session = new Lazy<InferenceSession>(
            () => new InferenceSession(modelPath),
            LazyThreadSafetyMode.ExecutionAndPublication);
        _mediaTools = mediaTools.Value;
        _storage = storage;
        _logger = logger;
    }

    /// <summary>Test seam: replaces the ONNX inference with a scripted probability.</summary>
    internal OnnxNsfwScreener(
        Func<SKBitmap, double> inference,
        MediaToolOptions mediaTools,
        IFileStorageService storage,
        ILogger<OnnxNsfwScreener> logger)
    {
        _inferenceOverride = inference;
        _session = new Lazy<InferenceSession>(() => throw new InvalidOperationException(
            "Test screener must not touch the real model."));
        _mediaTools = mediaTools;
        _storage = storage;
        _logger = logger;
    }

    public async Task<FeedMediaVerdict> ScreenAsync(string storagePath, string? contentType, CancellationToken ct)
    {
        // storagePath is storage-root-RELATIVE (UploadFile.StoragePath), and every read goes
        // through IFileStorageService — the abstraction the rest of the app honors, and the
        // detail the first live run of this class got wrong by decoding the relative path as if
        // it were a file. SkiaSharp answers null for a missing file exactly as it does for a
        // corrupt one, so that bug reported every healthy photo as "would not decode".
        if (!_storage.Exists(storagePath))
        {
            // The create path writes the file before screening, so a missing file is real
            // breakage (or a deletion racing the sweep) — a person's call either way.
            return new FeedMediaVerdict(FeedMediaReviewState.Held, "screener: stored file is missing");
        }

        if (contentType?.StartsWith("video/", StringComparison.OrdinalIgnoreCase) == true)
            return await ScreenVideoAsync(storagePath, ct);

        // Everything else arrived through the feed's upload validation, which only admits
        // image/* and video/* — so this is the image path, plus a safety net for anything odd.
        return await ScreenImageAsync(storagePath, ct);
    }

    private async Task<FeedMediaVerdict> ScreenImageAsync(string storagePath, CancellationToken ct)
    {
        await using var stream = await _storage.OpenReadAsync(storagePath, ct);
        using var bitmap = SKBitmap.Decode(stream);
        if (bitmap is null)
        {
            // A file that claims to be an image and will not decode is exactly what a person
            // should look at — not something to wave through or to retry forever.
            return new FeedMediaVerdict(FeedMediaReviewState.Held, "screener: image would not decode");
        }
        return NsfwDecision.Decide(Infer(bitmap));
    }

    private async Task<FeedMediaVerdict> ScreenVideoAsync(string storagePath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_mediaTools.FfmpegPath) || !File.Exists(_mediaTools.FfmpegPath))
        {
            // We did not look. Pending is the only honest state, and the sweep job will bring
            // these back once an ffmpeg is configured (MediaTools:FfmpegPath).
            return new FeedMediaVerdict(FeedMediaReviewState.Pending,
                "screener: video sampling needs ffmpeg (MediaTools:FfmpegPath); waiting for a moderator");
        }

        var frameDir = Directory.CreateTempSubdirectory("feed-screen-").FullName;
        try
        {
            // ffmpeg wants a local file; the storage abstraction wants to stay a stream. The
            // scratch copy reconciles them (and is what a blob backend would need anyway).
            var scratchVideo = Path.Combine(frameDir, "input" + Path.GetExtension(storagePath));
            await using (var source = await _storage.OpenReadAsync(storagePath, ct))
            await using (var copy = File.Create(scratchVideo))
                await source.CopyToAsync(copy, ct);

            await ExtractFramesAsync(scratchVideo, frameDir, ct);
            var frames = Directory.GetFiles(frameDir, "*.png");
            if (frames.Length == 0)
            {
                // ffmpeg ran and produced nothing — a video with no decodable frames is a
                // person's call, same reasoning as the undecodable image.
                return new FeedMediaVerdict(FeedMediaReviewState.Held, "screener: no frames could be sampled");
            }

            // The worst frame decides: one pornographic second in a ten-minute clip is the clip.
            double worst = 0;
            foreach (var frame in frames)
            {
                ct.ThrowIfCancellationRequested();
                using var bitmap = SKBitmap.Decode(frame);
                if (bitmap is null) continue;
                worst = Math.Max(worst, Infer(bitmap));
                if (worst >= NsfwDecision.BlockThreshold) break; // already decided; stop paying
            }
            var verdict = NsfwDecision.Decide(worst);
            return verdict with { Reason = $"{verdict.Reason} (worst of {frames.Length} frames)" };
        }
        finally
        {
            try { Directory.Delete(frameDir, recursive: true); } catch { /* scratch dir */ }
        }
    }

    /// <summary>
    /// One ffmpeg run sampling frames at 1 fps up to the cap, then one more grabbing the final
    /// second — a clip that saves its content for the last moment is still sampled where it
    /// matters. Argument shapes are pure functions so the math is testable without a binary.
    /// </summary>
    private async Task ExtractFramesAsync(string videoPath, string frameDir, CancellationToken ct)
    {
        await RunFfmpegAsync(FrameSampling.SampleArgs(videoPath, frameDir), ct);
        await RunFfmpegAsync(FrameSampling.LastFrameArgs(videoPath, frameDir), ct);
    }

    private async Task RunFfmpegAsync(string arguments, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(_mediaTools.FfmpegPath!)
        {
            Arguments = arguments,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("ffmpeg would not start");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(_mediaTools.TimeoutSeconds));
        // Drain so a chatty ffmpeg can't deadlock on a full pipe.
        var drain = Task.WhenAll(
            process.StandardError.ReadToEndAsync(timeout.Token),
            process.StandardOutput.ReadToEndAsync(timeout.Token));
        await process.WaitForExitAsync(timeout.Token);
        await drain;
        // Non-zero exit is not thrown here: the last-frame grab legitimately fails on sub-second
        // clips, and the caller judges by the frames that exist, not by exit codes.
    }

    private double Infer(SKBitmap bitmap)
    {
        if (_inferenceOverride is not null) return _inferenceOverride(bitmap);

        var input = NsfwPreprocessing.ToTensor(bitmap);
        var session = _session.Value;
        // Names read from the model rather than hard-coded, so a re-export that renames
        // "pixel_values" is a startup-time discovery, not a silent mismatch.
        var inputName = session.InputMetadata.Keys.Single();
        using var results = session.Run(
            [NamedOnnxValue.CreateFromTensor(inputName, input)]);
        var logits = results[0].AsEnumerable<float>().ToArray();
        return NsfwDecision.NsfwProbability(logits);
    }

    public void Dispose()
    {
        if (_session.IsValueCreated) _session.Value.Dispose();
    }
}

/// <summary>The decision map from a model score to a verdict — pure, and tested to the boundary.</summary>
public static class NsfwDecision
{
    /// <summary>At or above this, blocked outright. High because the model earns it: the ViT's
    /// confident positives are reliably positives, and a block needs no human second-guessing.</summary>
    public const double BlockThreshold = 0.85;

    /// <summary>
    /// At or above this (but below the block), a person looks. The band exists because the cost
    /// of the two mistakes is wildly asymmetric — an over-careful Held is a moderator's minute,
    /// an over-generous Approved is published pornography.
    /// </summary>
    public const double ReviewThreshold = 0.30;

    public static FeedMediaVerdict Decide(double nsfwProbability) => nsfwProbability switch
    {
        >= BlockThreshold => new FeedMediaVerdict(FeedMediaReviewState.Held,
            $"screener: nsfw {nsfwProbability:0.00} — blocked"),
        >= ReviewThreshold => new FeedMediaVerdict(FeedMediaReviewState.Held,
            $"screener: nsfw {nsfwProbability:0.00} — borderline, needs a person"),
        _ => new FeedMediaVerdict(FeedMediaReviewState.Approved,
            $"screener: nsfw {nsfwProbability:0.00}"),
    };

    /// <summary>Softmax over the model's two logits, ordered [normal, nsfw] per its config.</summary>
    public static double NsfwProbability(IReadOnlyList<float> logits)
    {
        if (logits.Count < 2) throw new InvalidOperationException(
            $"NSFW model produced {logits.Count} logits; expected [normal, nsfw].");
        var max = Math.Max(logits[0], logits[1]);
        var normal = Math.Exp(logits[0] - max);
        var nsfw = Math.Exp(logits[1] - max);
        return nsfw / (normal + nsfw);
    }
}

/// <summary>
/// The model's input contract, transcribed from its <c>preprocessor_config.json</c>:
/// 224×224 RGB, bilinear resample, scale 1/255, then normalize (x − 0.5) / 0.5.
/// </summary>
public static class NsfwPreprocessing
{
    public const int InputSize = 224;

    public static DenseTensor<float> ToTensor(SKBitmap source)
    {
        using var resized = source.Resize(
            new SKImageInfo(InputSize, InputSize, SKColorType.Rgba8888, SKAlphaType.Unpremul),
            new SKSamplingOptions(SKFilterMode.Linear));
        var bitmap = resized ?? throw new InvalidOperationException("Image resize failed.");

        var tensor = new DenseTensor<float>([1, 3, InputSize, InputSize]);
        for (var y = 0; y < InputSize; y++)
        {
            for (var x = 0; x < InputSize; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                tensor[0, 0, y, x] = Normalize(pixel.Red);
                tensor[0, 1, y, x] = Normalize(pixel.Green);
                tensor[0, 2, y, x] = Normalize(pixel.Blue);
            }
        }
        return tensor;
    }

    /// <summary>x/255 then (v − 0.5)/0.5 — one expression, so the two-step contract stays visible.</summary>
    public static float Normalize(byte channel) => (float)((channel / 255.0 - 0.5) / 0.5);
}

/// <summary>ffmpeg argument shapes for frame sampling — pure so the math has tests.</summary>
public static class FrameSampling
{
    /// <summary>Frames sampled at 1 fps. The cap bounds screening cost for long clips.</summary>
    public const int MaxSampledFrames = 12;

    public static string SampleArgs(string videoPath, string frameDir) =>
        $"-hide_banner -loglevel error -i \"{videoPath}\" -vf fps=1 -frames:v {MaxSampledFrames} " +
        $"\"{Path.Combine(frameDir, "sample_%03d.png")}\"";

    /// <summary>The final second, grabbed separately — fps sampling from the front never reaches
    /// the end of a clip longer than the cap.</summary>
    public static string LastFrameArgs(string videoPath, string frameDir) =>
        $"-hide_banner -loglevel error -sseof -1 -i \"{videoPath}\" -frames:v 1 " +
        $"\"{Path.Combine(frameDir, "last.png")}\"";
}
