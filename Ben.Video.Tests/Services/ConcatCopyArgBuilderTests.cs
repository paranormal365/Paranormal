using System.Text.RegularExpressions;
using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Item #70 phase 160 — the concat-copy argv. Unlike the thumbnail argv (JS-only, kept honest by a
/// fixture that reads the .js file), this builder is <b>shared by construction</b>: the sidecar's
/// ConcatJobRunner calls this exact method via InternalsVisibleTo. These tests pin the shape, and
/// <see cref="Argv_MatchesTheJavaScriptConcatCopy"/> confirms the browser's own JS still agrees.
/// </summary>
public sealed class ConcatCopyArgBuilderTests
{
    [Fact]
    public void BuildConcatCopyArgs_UsesConcatDemuxerWithStreamCopy()
    {
        var args = ExportArgBuilders.BuildConcatCopyArgs("/work/list.txt", "output.mp4");

        Assert.Equal(["-f", "concat", "-safe", "0", "-i", "/work/list.txt", "-c", "copy", "output.mp4"], args);
    }

    [Fact]
    public void BuildConcatListContent_OneQuotedFileLinePerSegmentInOrder()
    {
        var content = ExportArgBuilders.BuildConcatListContent(["a.mp4", "b.mp4", "c.mp4"]);

        Assert.Equal("file 'a.mp4'\nfile 'b.mp4'\nfile 'c.mp4'", content);
    }

    [Fact]
    public void BuildConcatListContent_Empty_ProducesEmptyString()
    {
        Assert.Equal("", ExportArgBuilders.BuildConcatListContent([]));
    }

    /// <summary>
    /// Reads the real ffmpegInterop.js and asserts its concatCopy still builds the same argv and
    /// list format. The C# is shared with the sidecar, but the BROWSER's copy of this logic lives
    /// in JS — so drift is still possible between the two languages even though it isn't possible
    /// between browser and sidecar on the C# side.
    /// </summary>
    [Fact]
    public void Argv_MatchesTheJavaScriptConcatCopy()
    {
        var js = File.ReadAllText(ResolveJsPath());
        var start = js.IndexOf("export async function concatCopy", StringComparison.Ordinal);
        Assert.True(start >= 0, "concatCopy not found in ffmpegInterop.js — was it renamed?");
        var next = js.IndexOf("\nexport ", start + 1, StringComparison.Ordinal);
        var body = next < 0 ? js[start..] : js[start..next];

        Assert.Contains("'-f', 'concat', '-safe', '0', '-i', listName, '-c', 'copy', outputName", body);
        Assert.Contains("`file '${n}'`", body);
        Assert.Contains("join('\\n')", body);
    }

    private static string ResolveJsPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Ben.Video.Editor")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var path = Path.Combine(dir!.FullName, "Ben.Video.Editor", "wwwroot", "js", "ffmpegInterop.js");
        Assert.True(File.Exists(path), $"ffmpegInterop.js not found at {path}");
        return path;
    }
}
