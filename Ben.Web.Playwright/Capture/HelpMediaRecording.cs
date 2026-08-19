using System.Diagnostics;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Capture;

/// <summary>
/// Records the short animations the help documents embed, as GIFs.
/// </summary>
/// <remarks>
/// <para><b>Opt-in, like <see cref="HelpMediaCapture"/>:</b> set <c>BEN_CAPTURE=1</c>. It writes
/// into the working tree and takes far longer than a screenshot, so it is not something a plain
/// test run should do.</para>
///
/// <para><b>Why GIF and not video.</b> The help is markdown rendered with raw HTML disabled, so
/// there is no way to write a <c>&lt;video&gt;</c> tag in a document — but an animated GIF is just
/// an image, and goes in with the same <c>![…](…)</c> syntax every screenshot uses. It also
/// survives into the printed PDF as its first frame rather than as a hole in the page.</para>
///
/// <para><b>Recordings are for movement only.</b> A still frame explains a screen; a recording
/// earns its size only where the thing being explained *is* the sequence — which tab reveals what,
/// in what order. Anything a screenshot can carry stays a screenshot.</para>
///
/// <para>Nothing here mutates data. A recording that filled in a form would leave rows behind on
/// every run and drift the very screens the other fixture captures.</para>
/// </remarks>
[TestFixture]
[Category("Capture")]
[NonParallelizable]
public sealed class HelpMediaRecording : BenTestBase
{
    /// <summary>
    /// Smaller than the screenshots on purpose: a GIF carries every frame uncompressed between
    /// palettes, so width is the whole file-size budget. 1280×800 downscales to a legible 1000px.
    /// </summary>
    private static string VideoDir => Path.Combine(Path.GetTempPath(), "ben-help-recordings");

    /// <summary>Runs from the moment the recorded context is created, which is when filming starts.</summary>
    private readonly Stopwatch _sinceRecordingStarted = new();

    /// <summary>How much of the front of the video is set-up rather than subject.</summary>
    private TimeSpan _subjectStartsAt;

    /// <summary>
    /// The default context does <b>not</b> record. Signing in happens here, and a recording of
    /// the sign-in page would carry the operator's own email address — in Development the form
    /// arrives with <c>DevLogin</c> already filled in — into a published document. The recorded
    /// context is created afterwards, from the signed-in session, so the credentials are not
    /// merely trimmed off the front of the video: they were never filmed.
    /// </summary>
    public override BrowserNewContextOptions ContextOptions() => new()
    {
        ViewportSize = new ViewportSize { Width = 1280, Height = 800 },
        ColorScheme  = ColorScheme.Dark,
    };

    /// <summary>
    /// Dark mode, plus the operator's own profile photo neutralised.
    /// </summary>
    /// <remarks>
    /// Injected as an init script rather than a stylesheet added after load, because a recording
    /// films the first paint: a style applied a moment later shows the real photo for the frames
    /// before it lands. The screenshots do the same masking, but they can afford to do it after
    /// the page settles.
    /// </remarks>
    private const string RecordingInitScript = """
        try {
            localStorage.setItem('layoutSettings', JSON.stringify({ theme: 'dark' }));
            localStorage.setItem('ben-theme', 'dark');
        } catch (e) { }

        (function () {
            var css = 'header img.profile-image, .page-header img.profile-image {'
                    + 'filter: grayscale(1) brightness(0) opacity(0.35) !important; }';
            function add() {
                var style = document.createElement('style');
                style.textContent = css;
                document.head.appendChild(style);
            }
            if (document.head) { add(); }
            else { document.addEventListener('DOMContentLoaded', add); }
        })();
        """;

    [SetUp]
    public async Task RequireOptInAndGoDark()
    {
        if (Environment.GetEnvironmentVariable("BEN_CAPTURE") != "1")
            Assert.Ignore("Set BEN_CAPTURE=1 to re-record the help animations.");

        await Context.AddInitScriptAsync(RecordingInitScript);
    }

    [Test]
    [Description("working-a-case: clicking through a case's tabs.")]
    public async Task Record_CaseTabs()
    {
        var (page, context) = await StartRecordingAsync(async () =>
        {
            await LoginAsync(SuperAdminEmail, SuperAdminPassword);
            if (!await OpenOrgCaseAsync("Tennessee Ghost Hunters", "Bell Witch"))
                Assert.Ignore("Seed case not present in Tennessee Ghost Hunters.");
            return Page.Url;
        });

        // Held on each tab long enough to read it. Faster than this and the GIF is a flicker;
        // slower and it is a file nobody waits for.
        foreach (var tab in new[] { "Timeline", "Investigations", "Files", "Reports", "Overview" })
        {
            var target = page.GetByRole(AriaRole.Tab, new() { Name = tab, Exact = true })
                             .Or(page.Locator(".nav-tabs .nav-link", new() { HasTextString = tab }))
                             .First;
            await target.ClickAsync();
            await page.WaitForTimeoutAsync(1_200);
        }

        await WriteGifAsync(page, context, "working-a-case", "case-tabs.gif");
    }

    /// <summary>
    /// Signs in on the ordinary context, then opens a second, recording context that resumes that
    /// session and lands directly on the page to be filmed.
    /// </summary>
    /// <param name="arrange">
    /// Runs against the non-recorded page and returns the URL the recording should open at.
    /// </param>
    /// <remarks>
    /// The session survives the move because the app persists its signed-in state to
    /// localStorage, which <c>StorageStateAsync</c> carries across. If that ever stops being
    /// true, the recorded context lands on a signed-out page and the assertion below says so
    /// rather than quietly filming a sign-in form.
    /// </remarks>
    private async Task<(IPage Page, IBrowserContext Context)> StartRecordingAsync(Func<Task<string>> arrange)
    {
        var url = await arrange();
        var state = await Context.StorageStateAsync();

        // The video starts rolling the moment the context exists, so everything between here and
        // the page being ready — the restore, the spinner, the first paint — is filmed and then
        // trimmed back off in WriteGifAsync.
        _sinceRecordingStarted.Restart();

        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize    = new ViewportSize { Width = 1280, Height = 800 },
            ColorScheme     = ColorScheme.Dark,
            StorageState    = state,
            RecordVideoDir  = VideoDir,
            RecordVideoSize = new RecordVideoSize { Width = 1280, Height = 800 },
        });
        await context.AddInitScriptAsync(RecordingInitScript);

        var page = await context.NewPageAsync();
        await page.GotoAsync(url);
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Long enough for the circuit to restore the session and the page to paint its data.
        await page.WaitForTimeoutAsync(2_500);

        Assert.That(page.Url, Does.Not.Contain("/login"),
            "The recording context was bounced to the sign-in page — the signed-in session did "
            + "not carry across, and filming would have recorded the sign-in form.");

        // A case page renders for a signed-out visitor too — as a permanent "Loading case…" and a
        // Sign In button. Waiting for the tab strip is what proves the session actually restored,
        // rather than filming an anonymous shell.
        await Expect(page.GetByRole(AriaRole.Tab).Or(page.Locator(".nav-tabs .nav-link")).First)
            .ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Expect(page.GetByText("Sign In", new() { Exact = false }).First)
            .Not.ToBeVisibleAsync(new() { Timeout = 10_000 });

        // Let the restored page settle before the part worth watching begins.
        await page.WaitForTimeoutAsync(600);
        _subjectStartsAt = _sinceRecordingStarted.Elapsed;

        return (page, context);
    }

    /// <summary>
    /// Closes the context so Playwright finalises the video, then converts it to a GIF.
    /// </summary>
    /// <remarks>
    /// The webm only exists once the context is closed — reading <c>Video.PathAsync()</c> before
    /// that yields a path to a file still being written, and ffmpeg then encodes a truncated clip.
    /// </remarks>
    private async Task WriteGifAsync(IPage page, IBrowserContext context, string slug, string name)
    {
        var video = page.Video;
        Assert.That(video, Is.Not.Null, "Recording was not enabled for this context.");

        await context.CloseAsync();
        var webm = await video!.PathAsync();

        Assert.That(File.Exists(webm), Is.True, $"No recording was written for {slug}/{name}.");

        var target = Path.Combine(
            RepoRoot().FullName, "Ben.Web.Website", "wwwroot", "help", "media", slug);
        Directory.CreateDirectory(target);
        var gif = Path.Combine(target, name);

        // The repo already ships a real ffmpeg for the video sidecar; using it keeps this working
        // on a machine with nothing installed, which is the same reason the sidecar bundles one.
        var ffmpeg = Path.Combine(RepoRoot().FullName, "Ben.Video.Sidecar", "ffmpeg", "osx-arm64", "ffmpeg");
        Assert.That(File.Exists(ffmpeg), Is.True, $"No bundled ffmpeg at {ffmpeg}.");

        // Two passes over the frames: one to build a palette from the actual colours, one to map
        // to it. A GIF written without that gets the default 216-colour web palette, which turns
        // a dark UI into visible banding.
        // -ss before -i seeks rather than decoding and discarding. A little lead-in is better
        // than clipping the first click, hence the small margin.
        var from = Math.Max(0, _subjectStartsAt.TotalSeconds - 0.3)
                       .ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);

        var args = $"-y -ss {from} -i \"{webm}\" -vf \"fps=8,scale=1000:-1:flags=lanczos,split[s0][s1];"
                 + $"[s0]palettegen=stats_mode=diff[p];[s1][p]paletteuse=dither=bayer\" -loop 0 \"{gif}\"";

        var process = Process.Start(new ProcessStartInfo(ffmpeg, args)
        {
            RedirectStandardError = true,
            UseShellExecute = false,
        })!;
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.That(process.ExitCode, Is.Zero, $"ffmpeg failed:\n{stderr}");
        Assert.That(new FileInfo(gif).Length, Is.GreaterThan(0), $"{name} was written empty.");

        TestContext.Out.WriteLine($"recorded {slug}/{name} ({new FileInfo(gif).Length / 1024} KB)");
    }

    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;
        Assert.That(dir, Is.Not.Null, "Could not find the repository root.");
        return dir!;
    }
}
