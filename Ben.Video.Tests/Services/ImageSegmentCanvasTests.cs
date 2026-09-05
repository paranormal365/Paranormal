using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Item #9 — image segments must be rendered onto the PROJECT's output canvas.
///
/// <para>The bug this guards was in the <i>caller</i>, not the builder:
/// <c>ExportService.RenderImageSegmentsAsync</c> passed <c>clip.Width</c>/<c>clip.Height</c> — the
/// image's own source size — as the builder's <c>outputWidth</c>/<c>outputHeight</c>. That produced
/// <c>scale={imgW}:{imgH},pad={imgW}:{imgH}</c>, a no-op, so every image segment stayed at its
/// native resolution instead of being letterboxed onto the project canvas. Every other segment
/// path in <c>ExportService</c> already derived its canvas from
/// <c>ParseResolution(s.Resolution)</c>; the image path was the only one that didn't.</para>
///
/// <para>Builder-level tests could never have caught this — the builder was always correct, it was
/// simply handed the wrong numbers. Hence the source-level guard below, following the same
/// precedent as <see cref="NoEvalInteropTests"/>.</para>
/// </summary>
public sealed class ImageSegmentCanvasTests
{
    private static string EditorRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Ben.Video.Editor")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "Ben.Video.Editor");
    }

    private static string ReadSource(string relativePath) =>
        File.ReadAllText(Path.Combine(EditorRoot(), relativePath));

    [Fact]
    public void ExportService_DoesNotUseTheClipsOwnSizeAsTheImageSegmentCanvas()
    {
        var src = ReadSource(Path.Combine("Services", "ExportService.cs"));

        // EVERY call, not just the first: the export grew a second one when tracks above the
        // primary started being composited, and a guard that reads only the first call is a guard
        // the next call can walk straight past.
        var calls = new List<string>();
        for (var i = src.IndexOf("BuildImageSegmentArgs(", StringComparison.Ordinal);
             i >= 0;
             i = src.IndexOf("BuildImageSegmentArgs(", i + 1, StringComparison.Ordinal))
        {
            calls.Add(src[i..src.IndexOf(");", i, StringComparison.Ordinal)]);
        }

        Assert.NotEmpty(calls);

        foreach (var call in calls)
        {
            Assert.False(call.Contains("clip.Width", StringComparison.Ordinal),
                "image segments must scale to the PROJECT canvas, not the image's own source " +
                "size — passing the clip's width makes the scale/pad a no-op.");
            Assert.False(call.Contains("image.Width", StringComparison.Ordinal),
                "image segments must scale to the PROJECT canvas, not the image's own source size.");
        }

        // The canvas comes from ResolveCanvas, which answers "source resolution" from the first
        // clip instead of silently returning Full HD (2026-09-05 audit, export-5).
        Assert.Contains("ResolveCanvas(s)", src, StringComparison.Ordinal);
    }

    [Fact]
    public void NativeClipEncoder_AgreesWithTheWasmPathOnTheImageCanvas()
    {
        // The two paths must agree, or a sidecar-encoded image segment and a wasm-encoded one
        // land on different canvases inside the same export — the kind of mismatch that only
        // shows up when the sidecar happens to be paired.
        var src = ReadSource(Path.Combine("Services", "NativeClipEncoder.cs"));

        var i = src.IndexOf("SegmentKind.Image", StringComparison.Ordinal);
        Assert.True(i >= 0, "image spec construction not found in NativeClipEncoder");
        var spec = src[i..];

        Assert.False(spec.Contains("OutputWidth: clip.Width", StringComparison.Ordinal),
            "the native image path must use the project canvas, matching ExportService.");
        Assert.Contains("ParseResolution", src, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildImageSegmentArgs_GivenTheProjectCanvas_LetterboxesOntoIt()
    {
        // Behavioural companion to the guards above: with the project canvas passed in, the
        // filter preserves aspect ratio (decrease) and centres with padding, rather than
        // stretching the image to fill.
        var s = new ExportSettings { Resolution = "1920x1080" };

        var args = ExportArgBuilders.BuildImageSegmentArgs("img.png", "seg.mp4", 5.0, s,
                                                          outputWidth: 1920, outputHeight: 1080);
        var vf = args[Array.IndexOf(args, "-vf") + 1];

        Assert.Contains("scale=1920:1080:force_original_aspect_ratio=decrease", vf, StringComparison.Ordinal);
        Assert.Contains("pad=1920:1080:(ow-iw)/2:(oh-ih)/2", vf, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildImageSegmentArgs_GivenTheSourceSizeAsCanvas_ProducesTheNoOpTheBugCaused()
    {
        // Documents WHY the old call was wrong rather than merely asserting the new one is right:
        // scaling a 800x600 image to 800x600 changes nothing, which is exactly why the symptom
        // was "images render at native resolution" with no error anywhere.
        var s = new ExportSettings { Resolution = "1920x1080" };

        var args = ExportArgBuilders.BuildImageSegmentArgs("img.png", "seg.mp4", 5.0, s,
                                                          outputWidth: 800, outputHeight: 600);
        var vf = args[Array.IndexOf(args, "-vf") + 1];

        Assert.Contains("scale=800:600", vf, StringComparison.Ordinal);
        Assert.DoesNotContain("1920", vf, StringComparison.Ordinal);
    }
}
