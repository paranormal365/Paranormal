using System.Text.RegularExpressions;

namespace Ben.Video.Tests.Services;

/// <summary>
/// What the deployed editor promises, and what it actually ships with.
/// </summary>
/// <remarks>
/// Every check here is a source scan because every one of them is invisible to the compiler and
/// only shows up in a browser, on a server, or in front of a person reading the words
/// (2026-09-05 audit, phase 10).
/// </remarks>
public sealed class EditorDeploymentGuardTests
{
    private static string Repo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Ben.Video.Editor")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine([Repo(), .. parts]));

    /// <summary>The page with its HTML comments removed.</summary>
    /// <remarks>
    /// The comments describe the deployment in prose and name a <c>&lt;rid&gt;</c> placeholder,
    /// which is documentation rather than a link somebody can click.
    /// </remarks>
    private static string ReadPageMarkup(params string[] parts) =>
        Regex.Replace(Read(parts), "<!--.*?-->", string.Empty, RegexOptions.Singleline);

    // ── The ffmpeg core ───────────────────────────────────────────────────────

    /// <summary>
    /// The core ships with the app rather than being fetched from a CDN at every load.
    /// </summary>
    /// <remarks>
    /// Thirty megabytes of WebAssembly came from cdn.jsdelivr.net on every start, undocumented,
    /// with a retry loop around it because it failed often enough to need one — so the editor
    /// could not start at all if that CDN was slow or blocked (2026-09-05 audit, media-13).
    /// </remarks>
    [Theory]
    [InlineData("st", "ffmpeg-core.js")]
    [InlineData("st", "ffmpeg-core.wasm")]
    [InlineData("mt", "ffmpeg-core.js")]
    [InlineData("mt", "ffmpeg-core.wasm")]
    [InlineData("mt", "ffmpeg-core.worker.js")]
    public void The_ffmpeg_core_ships_with_the_editor(string variant, string file)
    {
        var path = Path.Combine(
            Repo(), "Ben.Video.Editor", "wwwroot", "js", "ffmpeg-core", variant, file);

        Assert.True(File.Exists(path), $"{variant}/{file} is missing — the editor cannot start.");
        Assert.True(new FileInfo(path).Length > 1024, $"{variant}/{file} is suspiciously small.");
    }

    [Theory]
    [InlineData("st")]
    [InlineData("mt")]
    public void The_vendored_wasm_is_really_webassembly(string variant)
    {
        var path = Path.Combine(
            Repo(), "Ben.Video.Editor", "wwwroot", "js", "ffmpeg-core", variant, "ffmpeg-core.wasm");

        using var stream = File.OpenRead(path);
        var magic = new byte[4];
        Assert.Equal(4, stream.Read(magic));

        // "\0asm" — the WebAssembly module preamble.
        Assert.Equal([0x00, 0x61, 0x73, 0x6D], magic);
    }

    [Fact]
    public void Nothing_in_the_loader_reaches_a_cdn()
    {
        var loader = Read("Ben.Video.Editor", "wwwroot", "js", "ffmpegInterop.js");

        // Only the note explaining why it used to may mention the CDN, and a note is not a fetch.
        var fetching = Regex.Matches(loader, @"['""`]https?://[^'""`]*cdn\.[^'""`]*['""`]");

        Assert.Empty(fetching);
    }

    // ── The deployed shell ────────────────────────────────────────────────────

    /// <summary>
    /// The headers a static app can set and mean, including the two that decide whether the
    /// multi-thread core can ever be selected.
    /// </summary>
    /// <remarks>
    /// The app shipped none, and TokenStore's own doc comment cited a policy that did not exist as
    /// the reason a bearer token in memory was safe enough (2026-09-05 audit, wasm-5 and wasm-7).
    /// </remarks>
    [Theory]
    [InlineData("X-Frame-Options")]
    [InlineData("Content-Security-Policy")]
    [InlineData("X-Content-Type-Options")]
    [InlineData("Cross-Origin-Opener-Policy")]
    [InlineData("Cross-Origin-Embedder-Policy")]
    public void The_editor_sends_the_header(string header)
        => Assert.Contains($"name=\"{header}\"", Read("Ben.Wasm.Video", "wwwroot", "web.config"));

    /// <summary>
    /// The two files that must never be cached: a stale copy of either points a returning visitor
    /// at a deployment that no longer exists (2026-09-05 audit, wasm-9).
    /// </summary>
    [Fact]
    public void The_shell_and_its_settings_are_not_cached()
    {
        var config = Read("Ben.Wasm.Video", "wwwroot", "web.config");

        Assert.Contains("extension=\".html\" policy=\"DisableCache\"", config);
        Assert.Contains("extension=\".json\" policy=\"DisableCache\"", config);
    }

    // ── The sidecar ───────────────────────────────────────────────────────────

    /// <summary>
    /// The panel that asks somebody to install the sidecar offers a way to get it.
    /// </summary>
    /// <remarks>
    /// It said "Download and run it" and gave nobody anything to click, while the downloads page
    /// sat one level below the editor with nothing linking to it (2026-09-05 audit, F17).
    /// </remarks>
    [Fact]
    public void The_sidecar_panel_offers_the_download()
        => Assert.Contains("SidecarDownloadUrl",
            Read("Ben.Video.Editor", "Components", "NativeSidecarPanel.razor"));

    [Theory]
    [InlineData("Ben.Wasm.Video")]
    [InlineData("Ben.Web.Website")]
    public void Each_host_says_where_the_sidecar_is(string host)
        => Assert.Contains("SidecarDownloadUrl", Read(host, "Program.cs"));

    /// <summary>
    /// Every installer the downloads page links is one the deploy script can actually stage.
    /// </summary>
    /// <remarks>
    /// The page hard-linked one filename per platform while the script staged whichever of two
    /// formats it found, so the page could offer a 404 to somebody who came specifically to
    /// download it (2026-09-05 audit, F17).
    /// </remarks>
    [Fact]
    public void The_downloads_page_links_only_files_the_deploy_can_produce()
    {
        var page   = ReadPageMarkup("Ben.Wasm.Video", "wwwroot", "downloads", "index.html");
        var deploy = Read("scripts", "deploy-ishaunted.ps1");

        var linked = Regex.Matches(page, @"/files/sidecar-video/(?<rid>[^/]+)/(?<file>BenVideoSidecar-[^""]+)")
            .Select(m => (Rid: m.Groups["rid"].Value, File: m.Groups["file"].Value))
            .Distinct()
            .ToList();

        Assert.NotEmpty(linked);

        foreach (var (rid, file) in linked)
        {
            // The script names its candidates with the RID interpolated, so check the shape.
            var suffix = file.Replace($"BenVideoSidecar-{rid}", string.Empty);
            Assert.True(
                deploy.Contains($"BenVideoSidecar-$rid{suffix}"),
                $"the page links {file}, and the deploy script stages no such format for {rid}.");
        }
    }

    /// <summary>Every RID the page links is one the deploy actually stages.</summary>
    [Fact]
    public void The_downloads_page_links_only_platforms_the_deploy_stages()
    {
        var page   = ReadPageMarkup("Ben.Wasm.Video", "wwwroot", "downloads", "index.html");
        var deploy = Read("scripts", "deploy-ishaunted.ps1");

        var rids = Regex.Matches(page, @"/files/sidecar-video/(?<rid>[^/]+)/")
            .Select(m => m.Groups["rid"].Value)
            .Distinct();

        var staged = Regex.Match(deploy, @"\$SidecarRids\s*=\s*@\((?<list>[^)]*)\)").Groups["list"].Value;

        foreach (var rid in rids)
            Assert.True(staged.Contains($"'{rid}'"),
                $"the page offers {rid} and the deploy stages nothing for it.");
    }

    // ── What the words claim ──────────────────────────────────────────────────

    /// <summary>
    /// Help does not promise hardware acceleration. There is no hwaccel anywhere in this codebase,
    /// in the browser or in the sidecar (2026-09-05 audit, F18).
    /// </summary>
    [Fact]
    public void Help_does_not_promise_video_hardware()
    {
        var help = Read("Ben.Web.Services", "Help", "Content", "using-the-video-editor.md");

        Assert.DoesNotContain("video hardware", help, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The export dialog does not claim the render happens entirely in the browser, which stopped
    /// being true the moment a sidecar could be paired (2026-09-05 audit, F18).
    /// </summary>
    [Fact]
    public void The_export_dialog_does_not_claim_the_browser_does_all_of_it()
    {
        var dialog = Read("Ben.Video.Editor", "Components", "ExportDialog.razor");

        Assert.DoesNotContain("entirely in your browser", dialog, StringComparison.OrdinalIgnoreCase);
    }
}
