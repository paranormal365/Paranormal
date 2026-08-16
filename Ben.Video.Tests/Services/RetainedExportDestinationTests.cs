using System.Text.RegularExpressions;
using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Phase 176 — the rendered video now has two possible destinations (the user's machine, or the
/// host application), chosen after the render instead of assumed before it.
///
/// <para><see cref="ExportService"/> still has no direct pipeline tests here — see
/// <see cref="ExportMemoryFlatteningTests"/> for why — so these cover the two seams that are
/// separable, and that would each fail silently rather than loudly.</para>
/// </summary>
public sealed class RetainedExportDestinationTests
{
    private static ExportJob JobWith(string filename, string format) =>
        new() { Settings = new ExportSettings { OutputFilename = filename, OutputFormat = format } };

    // ── Naming parity with the pipeline's own Phase 5 ─────────────────────────
    //
    // RunPipelineAsync builds `{SanitiseFilename(OutputFilename)}.{OutputFormat}` and
    // `"." + OutputFormat`. The destination path rebuilds both from the job. If they drift, the
    // download gets one name and the uploaded file another — or worse, the OPFS delete targets an
    // extension that isn't there and a full render is left behind in the user's storage.

    [Fact]
    public void RetainedFileName_MatchesThePipelinesOwnConstruction()
    {
        var job = JobWith("My Holiday Video", "mp4");

        var expected = $"{ExportArgBuilders.SanitiseFilename("My Holiday Video")}.mp4";
        Assert.Equal(expected, ExportService.RetainedFileName(job));
    }

    [Theory]
    [InlineData("clip/with:illegal*chars", "mp4")]
    [InlineData("  leading and trailing  ", "webm")]
    [InlineData("unicode – dash", "mkv")]
    public void RetainedFileName_SanitisesExactlyAsTheExportDoes(string filename, string format)
    {
        var job = JobWith(filename, format);

        Assert.Equal($"{ExportArgBuilders.SanitiseFilename(filename)}.{format}",
                     ExportService.RetainedFileName(job));
    }

    [Theory]
    [InlineData("mp4")]
    [InlineData("webm")]
    [InlineData("mkv")]
    public void RetainedExt_IsTheOutputFormatWithLeadingDot(string format)
    {
        Assert.Equal($".{format}", ExportService.RetainedExt(JobWith("out", format)));
    }

    [Fact]
    public void RetainedFileName_EndsWithRetainedExt_SoDeleteAndDownloadAgree()
    {
        var job = JobWith("Some Name", "webm");

        Assert.EndsWith(ExportService.RetainedExt(job), ExportService.RetainedFileName(job));
    }

    [Fact]
    public void RetainedFileName_CarriesTheExtension_NotTheBareOutputFilenameSetting()
    {
        // The destination prompt shows this name, and the host receives it on ExportedVideo.
        // Binding the prompt to Settings.OutputFilename instead — as it first did — made it
        // announce "output" for a file that the download, the upload and the OPFS delete all know
        // as "output.mp4". Caught by watching a real export, not by any earlier test.
        var job = JobWith("output", "mp4");

        Assert.Equal("output.mp4", ExportService.RetainedFileName(job));
        Assert.NotEqual(job.Settings.OutputFilename, ExportService.RetainedFileName(job));
    }

    // ── The host-facing payload ───────────────────────────────────────────────

    [Fact]
    public async Task ExportedVideo_DoesNotCarryBytes_ItResolvesThemOnDemand()
    {
        // The whole point of the record: a host that only needs to check size/name (an over-quota
        // guard, say) must be able to do that without a full render landing on the heap. If this
        // ever becomes a plain byte[] property, every host pays for every export.
        var reads = 0;
        var exported = new ExportedVideo(
            "out.mp4", "video/mp4", 1234, 5.0,
            () => { reads++; return Task.FromResult<byte[]?>([1, 2, 3]); });

        Assert.Equal(1234, exported.SizeBytes);
        Assert.Equal("out.mp4", exported.FileName);
        Assert.Equal(0, reads); // reading metadata must not have touched the body

        Assert.Equal([1, 2, 3], await exported.ReadBytesAsync());
        Assert.Equal(1, reads);
    }

    // ── JS contract ───────────────────────────────────────────────────────────

    [Fact]
    public void DomInterop_ExportsBlobUrlAsBytes_AndReadsTheBlobUrlNotOpfs()
    {
        var js = File.ReadAllText(DomInteropPath());

        // Called by name from ExportService.ReadRetainedBytesAsync — a rename here is invisible
        // to the compiler and shows up only as a failed upload at runtime.
        Assert.Matches(@"export async function blobUrlAsBytes\(", js);

        // Reading OPFS instead would return nothing on the OPFS-unavailable branch (Safari private
        // browsing), where the retained export lives only behind its MEMFS-minted blob URL.
        var body = Regex.Match(js, @"export async function blobUrlAsBytes\([^)]*\)\s*\{(.*?)\n\}", RegexOptions.Singleline).Groups[1].Value;
        Assert.Contains("fetch(url)", body);
        Assert.Contains("arrayBuffer()", body);
        Assert.DoesNotContain("opfs", body, StringComparison.OrdinalIgnoreCase);
    }

    private static string DomInteropPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Ben.Video.Editor")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var path = Path.Combine(dir!.FullName, "Ben.Video.Editor", "wwwroot", "js", "domInterop.js");
        Assert.True(File.Exists(path), $"domInterop.js not found at {path}");
        return path;
    }
}
