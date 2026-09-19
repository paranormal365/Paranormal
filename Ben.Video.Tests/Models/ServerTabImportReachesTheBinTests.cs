using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Video.Tests.Models;

/// <summary>
/// Everything the Server tab brings in lands in a bin (V-2, site evaluation 2026-09-06).
/// </summary>
/// <remarks>
/// <para><b>What happened.</b> Download on an audio file: progress ran to 100%, the download
/// answered 200, and the file was nowhere. Audio bin still said "No audio yet", the library size
/// was unchanged, nothing said anything. Twice.</para>
///
/// <para><b>Why.</b> <c>AddCachedFileToTimelineAsync</c> — the Server tab's import — placed clips
/// on the timeline and never called <c>Clips.AddToBin</c>, while the local-file import beside it
/// always did. So a file brought in from the server was absent from the bin that claims to list
/// what the project has; and audio, on an editor with audio tracks switched off, went nowhere at
/// all. The download had worked perfectly.</para>
///
/// <para><b>Why this is a source scan.</b> That method is 200 lines of ffmpeg, OPFS and JS interop
/// — there is no seam a test can reach without a browser. What can be checked without one is
/// whether the three branches still put their clip in the bin, and that is the whole of the
/// defect. A Playwright walk of the real thing lives in <c>WasmEditorEditingTests</c>.</para>
/// </remarks>
public sealed class ServerTabImportReachesTheBinTests
{
    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;
        return dir ?? throw new InvalidOperationException("repo root not found");
    }

    /// <summary>The body of the Server tab's import, from its signature to the catch.</summary>
    private static string ImportBody()
    {
        var path = Path.Combine(RepoRoot().FullName, "Ben.Video.Editor", "Components", "ClipBrowser.razor");
        var text = File.ReadAllText(path);

        var start = text.IndexOf("private async Task AddCachedFileToTimelineAsync", StringComparison.Ordinal);
        Assert.True(start >= 0,
            "AddCachedFileToTimelineAsync is gone from ClipBrowser.razor — this guard has lost its "
            + "subject. If the Server tab's import was renamed, rename it here too.");

        var end = text.IndexOf("catch (Exception ex)", start, StringComparison.Ordinal);
        Assert.True(end > start, "Could not find the end of AddCachedFileToTimelineAsync.");

        return text[start..end];
    }

    /// <summary>
    /// Three branches — video, audio, image — and each one puts its clip in the bin.
    /// </summary>
    /// <remarks>
    /// Counted rather than matched once, because the failure was per-branch: audio was the one the
    /// evaluation walked, and a fix that only covered video would look exactly like a fix.
    /// </remarks>
    [Fact]
    public void Every_branch_of_the_server_import_adds_its_clip_to_the_bin()
    {
        var body = ImportBody();
        var adds = Regex.Matches(body, @"Clips\.AddToBin\(").Count;

        Assert.True(adds >= 3,
            $"""
             The Server tab's import calls Clips.AddToBin {adds} time(s); there are three kinds of
             clip it can bring in and each one needs a bin entry.

             Without it a downloaded file is on the timeline and absent from the bin that lists
             what the project has — and an audio file, in an editor with audio tracks switched
             off, is nowhere at all (V-2).
             """);
    }

    /// <summary>
    /// Audio with tracks switched off is told what happened, not silently dropped.
    /// </summary>
    /// <remarks>
    /// This is the exact state the evaluation was in. The clip is in the bin and reachable; what
    /// did not happen is the placing, and the reason is a setting rather than a fault.
    /// </remarks>
    [Fact]
    public void Audio_that_cannot_be_placed_still_says_where_it_went()
    {
        var body = ImportBody();
        Assert.Contains("audio tracks are switched off", body, StringComparison.OrdinalIgnoreCase);
    }
}
