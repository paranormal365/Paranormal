using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// The editor hosts carry COPIES of the website's Signal files, and the copies may not drift.
/// </summary>
/// <remarks>
/// <para>Ben.Wasm.Video and Ben.Wasm.Canvas are separate deployables, served from their own hosts
/// in development, so they cannot load the website's stylesheets directly; each has its own copy.
/// That arrangement already existed — each host had a hand-copied palette with a comment saying to
/// "keep in step" with the site — and it had failed in exactly the way such comments fail. The
/// video host carried a contrast fix the canvas host never got; the canvas host carried an older
/// Telerik bridge missing two fixes the site had made since; and both still painted the retired
/// Night palette after the site moved to Signal (2026-10-01).</para>
///
/// <para>So "keep in step" is now this test. Every copy must be identical to the website's file,
/// and when one is not, the failure says which and gives the command that fixes it. Whole-file
/// comparison rather than scanning, so none of the traps in the source-scan guards apply. Line
/// endings are normalised, because a Windows checkout turns both files to CRLF equally and that
/// is not drift.</para>
/// </remarks>
public class SignalCopiesStayInStepTests
{
    public static TheoryData<string, string> Copies => new()
    {
        { "Ben.Wasm.Video/wwwroot/css/signal-tokens.css",    "Ben.Web.Website/wwwroot/css/themes/signal-tokens.css" },
        { "Ben.Wasm.Canvas/wwwroot/css/signal-tokens.css",   "Ben.Web.Website/wwwroot/css/themes/signal-tokens.css" },
        { "Ben.Wasm.Video/wwwroot/theme/telerik-signal.css",  "Ben.Web.Website/wwwroot/theme/telerik-signal.css" },
        { "Ben.Wasm.Canvas/wwwroot/theme/telerik-signal.css", "Ben.Web.Website/wwwroot/theme/telerik-signal.css" },
    };

    [Theory]
    [MemberData(nameof(Copies))]
    public void An_editor_hosts_copy_is_identical_to_the_websites_file(string copy, string source)
    {
        var root = RepoRoot();
        var copyPath = Path.Combine(root, copy);
        var sourcePath = Path.Combine(root, source);

        Assert.True(File.Exists(sourcePath), $"{source} is missing.");
        Assert.True(File.Exists(copyPath), $"{copy} is missing. Restore it with:\n    cp {source} {copy}");

        Assert.True(Read(copyPath) == Read(sourcePath),
            $"{copy} has drifted from {source}. Edit the website's file, then copy it over:\n    cp {source} {copy}");
    }

    [Theory]
    [InlineData("Ben.Wasm.Video/wwwroot/index.html")]
    [InlineData("Ben.Wasm.Canvas/wwwroot/index.html")]
    public void Each_editor_host_actually_loads_its_copies(string indexHtml)
    {
        // A copy nobody links is a copy that can never be wrong and never be used.
        var html = Read(Path.Combine(RepoRoot(), indexHtml));
        Assert.Contains("href=\"css/signal-tokens.css\"", html);
        Assert.Contains("href=\"theme/telerik-signal.css\"", html);
        Assert.DoesNotContain("telerik-night.css\"", html);
    }

    private static string Read(string path) => File.ReadAllText(path).Replace("\r\n", "\n");

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
