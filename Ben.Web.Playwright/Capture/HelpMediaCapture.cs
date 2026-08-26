using System.Text.RegularExpressions;
using Microsoft.Playwright;
using System.Net.Http.Json;
using NUnit.Framework;

namespace Ben.Web.Playwright.Capture;

/// <summary>
/// Captures the screenshots the help documents embed, by driving the real site.
/// </summary>
/// <remarks>
/// <para><b>Opt-in.</b> Every other fixture here only reads the app; this one writes PNG files
/// into the working tree, which is not something a plain <c>dotnet test</c> should ever do. It
/// runs only when <c>BEN_CAPTURE=1</c> is set, and is otherwise ignored:</para>
/// <code>
/// BEN_CAPTURE=1 dotnet test Ben.Web.Playwright -p:IsTestProject=true --no-build \
///   --filter TestCategory=Capture
/// </code>
///
/// <para><b>Where the files land</b> follows the audience of the document that embeds them.
/// Documents for everyone, signed-in users and group members get plain static files under
/// <c>Ben.Web.Website/wwwroot/help/media/</c>. Documents for group and site administrators get
/// files embedded in <c>Ben.Web.Services</c> instead, which the renderer inlines as data URIs —
/// an admin screenshot therefore has no URL to guess, matching the reason the help *text* is
/// embedded rather than served from wwwroot.</para>
///
/// <para><b>Why data URIs rather than an authorised image endpoint:</b> a plain
/// <c>&lt;img src="…"&gt;</c> sends no bearer token, so a gated endpoint would 401 for exactly
/// the readers allowed to see it. The same reason <c>AvatarCache</c> serves avatars as data
/// URIs.</para>
///
/// <para><b>Not captured:</b> the video editor. It is still on the old styling, so its screens
/// would be the only ones in the help that do not look like the site.</para>
/// </remarks>
[TestFixture]
[Category("Capture")]
[NonParallelizable]
public sealed class HelpMediaCapture : BenTestBase
{
    /// <summary>
    /// A desktop viewport wide enough that the sidebar is expanded rather than collapsed to
    /// icons — the help describes the site as a desktop user sees it. Captured at 2× so the
    /// text stays sharp on the retina displays these are read on.
    /// </summary>
    public override BrowserNewContextOptions ContextOptions() => new()
    {
        ViewportSize      = new ViewportSize { Width = 1440, Height = 900 },
        DeviceScaleFactor = 2,
        ColorScheme       = ColorScheme.Dark,
    };

    /// <summary>
    /// Puts the site in dark mode before any page script runs.
    /// </summary>
    /// <remarks>
    /// The theme is applied synchronously from <c>&lt;head&gt;</c> out of the template's
    /// <c>layoutSettings</c> localStorage object, so seeding that object from an init script is
    /// what makes the very first paint dark. Clicking the header's theme toggle instead would
    /// capture a page that flashed light first, and would have to be repeated after every
    /// sign-in. <c>ColorScheme.Dark</c> above covers the parts that read
    /// <c>prefers-color-scheme</c> rather than the attribute.
    /// </remarks>
    private const string DarkModeInitScript = """
        try {
            localStorage.setItem('layoutSettings', JSON.stringify({ theme: 'dark' }));
            localStorage.setItem('ben-theme', 'dark');
        } catch (e) { /* storage blocked; the shot falls back to the default theme */ }
        """;

    /// <summary>
    /// Neutralises the signed-in operator's own profile photo in the header.
    /// </summary>
    /// <remarks>
    /// Whoever captures these is signed in as a real administrator, and their face would
    /// otherwise appear in the corner of every signed-in screenshot in the published
    /// documentation. Avatars *inside* the page are left alone: those belong to the seeded demo
    /// users and are part of what the screenshots are showing.
    /// </remarks>
    private const string HideOperatorAvatarCss = """
        header img.profile-image, .page-header img.profile-image {
            filter: grayscale(1) brightness(0) opacity(0.35) !important;
        }
        """;

    private readonly List<string> _console = [];

    [SetUp]
    public async Task RequireOptInAndGoDark()
    {
        if (Environment.GetEnvironmentVariable("BEN_CAPTURE") != "1")
            Assert.Ignore("Set BEN_CAPTURE=1 to re-capture the help screenshots.");

        await Context.AddInitScriptAsync(DarkModeInitScript);

        _console.Clear();
        Page.Console += (_, msg) =>
        {
            // Everything, not just errors. ffmpeg reports through console.log — including the
            // command it is about to run and its own stderr — so filtering to errors threw away
            // the only record of what a stalled render was actually doing.
            _console.Add($"[{msg.Type}] {msg.Text}");
            if (_console.Count > 400) _console.RemoveAt(0);
        };
        Page.PageError += (_, err) => _console.Add($"[pageerror] {err}");
    }

    // ── Where the files go ────────────────────────────────────────────────────

    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;
        Assert.That(dir, Is.Not.Null, "Could not find the repository root (no Ben.slnx above the test assembly).");
        return dir!;
    }

    /// <summary>Public shots are served from wwwroot; gated ones are embedded in the services assembly.</summary>
    private static string PathFor(string slug, string name, bool gated)
    {
        var root = RepoRoot().FullName;
        var dir = gated
            ? Path.Combine(root, "Ben.Web.Services", "Help", "Media", slug)
            : Path.Combine(root, "Ben.Web.Website", "wwwroot", "help", "media", slug);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, name);
    }

    /// <summary>
    /// Screenshots either the whole viewport or one element, after letting the circuit settle.
    /// </summary>
    /// <param name="selector">
    /// When given, the shot is cropped to that element — nearly always the better picture, since
    /// a full-page shot of a data-heavy screen reduces the part being explained to a few pixels.
    /// </param>
    private async Task ShootAsync(
        string slug, string name, bool gated = false, string? selector = null, string? proves = null)
    {
        // A screenshot of an empty state teaches nobody anything, and it is the failure mode this
        // fixture is most likely to hit silently: the page loads, renders "You aren't borrowing
        // anything", and the shot looks fine until someone reads the document. Naming the text
        // the picture is supposed to show turns that into a failed capture.
        if (proves is not null)
        {
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await WaitUntilLoadedAsync();
            await Expect(Page.GetByText(proves, new() { Exact = false }).First)
                .ToBeVisibleAsync(new() { Timeout = 15_000 });
        }

        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await WaitUntilLoadedAsync();

        await Page.AddStyleTagAsync(new() { Content = HideOperatorAvatarCss });

        // Dark mode is applied from <head> on load, but a shot taken straight after a Blazor
        // navigation can still catch the theme mid-application. Assert it rather than shipping a
        // light screenshot into a dark set.
        var theme = await Page.EvaluateAsync<string?>(
            "document.documentElement.getAttribute('data-bs-theme')");
        Assert.That(theme, Is.EqualTo("dark"),
            $"{slug}/{name} was about to be captured in the '{theme}' theme — the dark-mode init "
            + "script did not take effect.");

        // Blazor renders the data, then the layout settles a frame later; without this the shot
        // can catch a half-painted grid.
        await Page.WaitForTimeoutAsync(400);

        var path = PathFor(slug, name, gated);

        if (selector is not null)
        {
            var target = Page.Locator(selector).First;
            await Expect(target).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await target.ScrollIntoViewIfNeededAsync();
            await Page.WaitForTimeoutAsync(200);
            await target.ScreenshotAsync(new() { Path = path });
        }
        else
        {
            await Page.ScreenshotAsync(new() { Path = path });
        }

        Assert.That(new FileInfo(path).Length, Is.GreaterThan(0), $"{name} captured as an empty file.");

        if (gated) await HalveAsync(path);

        TestContext.Out.WriteLine(
            $"captured {(gated ? "[gated] " : "")}{slug}/{name} ({new FileInfo(path).Length / 1024} KB)");
    }

    /// <summary>Halves a gated screenshot's pixel dimensions, in place.</summary>
    /// <remarks>
    /// Gated screenshots are inlined into the page as base64, so their file size is page weight
    /// pushed down the circuit on every view — the four site-administration shots came to 1.2 MB,
    /// about 1.6 MB once encoded, for one help page. Public shots are ordinary cached files and
    /// keep their full 2x detail.
    /// </remarks>
    private static async Task HalveAsync(string path)
    {
        var ffmpeg = Path.Combine(
            RepoRoot().FullName, "Ben.Video.Sidecar", "ffmpeg", "osx-arm64", "ffmpeg");
        if (!File.Exists(ffmpeg))
        {
            TestContext.Out.WriteLine($"no bundled ffmpeg at {ffmpeg}; leaving {Path.GetFileName(path)} at full size");
            return;
        }

        var scaled = path + ".scaled.png";
        var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
            ffmpeg, $"-y -i \"{path}\" -vf \"scale=iw/2:-1:flags=lanczos\" \"{scaled}\"")
        {
            RedirectStandardError = true,
            UseShellExecute = false,
        })!;
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.That(process.ExitCode, Is.Zero, $"ffmpeg could not downscale {path}:\n{stderr}");
        File.Move(scaled, path, overwrite: true);
    }

    private async Task GoAsync(string route)
    {
        await Page.GotoAsync($"{BaseUrl}{route}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    // ── Everyone ──────────────────────────────────────────────────────────────

    [Test]
    [Description("getting-started: the signed-out view of the site.")]
    public async Task Capture_GettingStarted()
    {
        await LogoutAsync();

        await GoAsync("/");
        await ShootAsync("getting-started", "home.png");

        await GoAsync("/find");
        await ShootAsync("getting-started", "find-groups.png");

        await GoAsync("/help");
        await ShootAsync("getting-started", "help-index.png");
    }

    /// <summary>
    /// The feed, its tag pages, and the moderation queue.
    /// </summary>
    /// <remarks>
    /// <para>The feed is off by default, so this turns it on, captures, and puts it back exactly
    /// as it was found. A capture run that left a feature switched on would change the site for
    /// everything that ran afterwards.</para>
    ///
    /// <para>It also writes a post, because a screenshot of an empty feed teaches nobody anything —
    /// and the post carries a mention and a tag, since those becoming links is most of what the
    /// document is explaining.</para>
    /// </remarks>
    [Test]
    [Description("the-feed and moderating-the-feed: posting, tags, and the moderation queue.")]
    public async Task Capture_TheFeed()
    {
        var wasOn = await FeedFlagAsync();
        await SetFeedFlagAsync(true);

        try
        {
            // Clear whatever previous test runs left behind. A help screenshot full of posts
            // reading "r2c31a48c403 a post to report" teaches nobody anything and ships junk into
            // user-facing documentation — which is exactly what the first attempt at this
            // captured.
            await ClearFeedForCaptureAsync();

            // James writes the post, so that Sarah — capturing below — is not its author and the
            // Follow and Report controls are visible in the shot.
            await LoginAsync(MemberEmail, MemberPassword);
            await GoToFeedForCaptureAsync();
            await ComposeForCaptureAsync(
                "Clear #EVP in the upstairs hall around 2am — three separate responses to direct "
                + "questions. Full audio going up tomorrow. @sarahmitchell was on the recorder.");

            await LogoutAsync();
            await LoginAsync(UserEmail, UserPassword);
            await GoToFeedForCaptureAsync();
            await ShootAsync("the-feed", "feed.png", proves: "Clear");

            await Page.Locator(".bv-feed-tag").First.ClickAsync();
            await ShootAsync("the-feed", "tag-page.png", proves: "Clear");

            // Report it, so the moderation queue below has something in it.
            await GoToFeedForCaptureAsync();
            var post = Page.Locator(".bv-feed-post").First;
            var report = post.GetByRole(AriaRole.Button, new() { Name = "Report" });
            if (await report.CountAsync() > 0)
            {
                await report.ClickAsync();
                await Expect(post.GetByText("Reported")).ToBeVisibleAsync(new() { Timeout = 15_000 });
            }

            await LogoutAsync();
            await LoginAsync(SuperAdminEmail, SuperAdminPassword);
            await GoAsync("/admin/feed-reports");
            await ShootAsync("moderating-the-feed", "queue.png", gated: true, proves: "Reported by");
        }
        finally
        {
            await SetFeedFlagAsync(wasOn);
        }
    }

    /// <summary>
    /// Signing up, two-step sign-in, and the notifications page.
    /// </summary>
    /// <remarks>
    /// The two-step shot is of the enrolment step, which is the one worth a picture: a QR code and
    /// a key beside it is far quicker to recognise than to describe. It leaves 2FA switched off —
    /// the shot is taken before the code is entered, so nothing is enrolled.
    /// </remarks>
    [Test]
    [Description("getting-started: signing up, two-step sign-in, and notifications.")]
    public async Task Capture_Accounts()
    {
        await LogoutAsync();
        await GoAsync("/signup");
        await ShootAsync("getting-started", "signup.png", proves: "Your @name");

        await LoginAsync(UserEmail, UserPassword);

        await GoAsync("/profile");
        await Page.GetByRole(AriaRole.Tab, new() { Name = "Security" }).ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Turn on two-step sign-in" }).ClickAsync();
        await Expect(Page.Locator(".k-qrcode")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await ShootAsync("getting-started", "two-step.png", proves: "Scan this code");

        await GoAsync("/notifications");
        await ShootAsync("getting-started", "notifications.png", proves: "Waiting on you");
    }

    // ── Feed capture helpers ─────────────────────────────────────────────────

    /// <summary>
    /// Hides every post already in the feed, so a capture starts from an empty one.
    /// </summary>
    /// <remarks>
    /// <para>Done through the real report-and-hide flow rather than by deleting rows, because that
    /// is the only route there is — and it is the same one an administrator uses, so this cannot
    /// drift from what the product actually does.</para>
    ///
    /// <para>Hidden rather than deleted also means nothing is destroyed: the posts are still there
    /// for anybody who looks, which is the point of hiding rather than deleting in the first place.
    /// </para>
    /// </remarks>
    private async Task ClearFeedForCaptureAsync()
    {
        var token = await AdminTokenForCaptureAsync();
        if (token is null) return;

        using var http = new HttpClient { BaseAddress = new Uri(ApiUrl) };
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var page = await http.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/feed");
        if (!page.TryGetProperty("posts", out var posts)) return;

        foreach (var post in posts.EnumerateArray())
        {
            var id = post.GetProperty("id").GetString();

            // The SuperAdmin cannot report their own post; skip those rather than fail the run.
            if (post.GetProperty("isOwnPost").GetBoolean()) continue;

            using var reported = await http.PostAsJsonAsync(
                $"/api/feed/posts/{id}/report", new { reason = "clearing the feed for a help capture" });

            var queue = await http.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/admin/feed/reports");
            foreach (var report in queue.EnumerateArray())
            {
                if (report.GetProperty("orgMessageId").GetString() != id) continue;

                using var _ = await http.PostAsJsonAsync(
                    $"/api/admin/feed/reports/{report.GetProperty("id").GetString()}/resolve",
                    new { outcome = 2 });   // Hidden
                break;
            }
        }
    }

    private async Task GoToFeedForCaptureAsync()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            await GoAsync("/feed");
            if (await Page.Locator("#feed-composer").CountAsync() > 0) return;
            await Task.Delay(3000);
        }

        Assert.Fail("The feed page never appeared — is features.public-feed on?");
    }

    /// <remarks>
    /// Retried for the reason every typed input here is: a Blazor Server page renders its inputs
    /// before the circuit connects, and a value typed in that window is erased by the first
    /// interactive render rather than merely ignored. The Post button is disabled until the body
    /// reaches the server, which makes it the signal that the typing took.
    /// </remarks>
    private async Task ComposeForCaptureAsync(string body)
    {
        var box = Page.Locator("#feed-composer");
        var post = Page.GetByRole(AriaRole.Button, new() { Name = "Post", Exact = true });

        for (var attempt = 0; attempt < 10; attempt++)
        {
            await box.FillAsync(body);
            try
            {
                await Expect(post).ToBeEnabledAsync(new() { Timeout = 1_500 });
                await post.ClickAsync();
                await Expect(Page.Locator(".bv-feed-post").First)
                    .ToBeVisibleAsync(new() { Timeout = 15_000 });
                return;
            }
            catch (Exception)
            {
                // Circuit not live yet.
            }
        }

        Assert.Fail("The composer never accepted the post.");
    }

    private Task<bool> FeedFlagAsync() => FlagAsync("features.public-feed");

    private Task SetFeedFlagAsync(bool on) => SetFlagAsync("features.public-feed", on);

    /// <summary>Reads one feature switch. Anything but an explicit "true" reads as off.</summary>
    /// <remarks>
    /// Read before a capture and written back afterwards, so a run leaves the developer's site the
    /// way it found it. A capture that silently turned a feature on would be found weeks later by
    /// somebody wondering why their local site had grown a section.
    /// </remarks>
    private async Task<bool> FlagAsync(string key)
    {
        var token = await AdminTokenForCaptureAsync();
        if (token is null) return false;

        using var http = new HttpClient { BaseAddress = new Uri(ApiUrl) };
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var settings = await http.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/admin/site-settings");
        foreach (var setting in settings.EnumerateArray())
        {
            if (setting.GetProperty("key").GetString() != key) continue;
            return setting.TryGetProperty("value", out var value)
                && string.Equals(value.GetString(), "true", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private async Task SetFlagAsync(string key, bool on)
    {
        var token = await AdminTokenForCaptureAsync();
        if (token is null) return;

        using var http = new HttpClient { BaseAddress = new Uri(ApiUrl) };
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        using var _ = await http.PutAsJsonAsync(
            $"/api/admin/site-settings/{key}", new { value = on ? "true" : "false" });

        // The site caches the answer for up to 30 seconds, so a page opened immediately after the
        // switch would render the old one. Waiting here is the difference between capturing the
        // feature and capturing "page not found".
        await Task.Delay(35_000);
    }

    private static async Task<string?> AdminTokenForCaptureAsync()
    {
        using var http = new HttpClient { BaseAddress = new Uri(ApiUrl) };

        try
        {
            using var response = await http.PostAsJsonAsync("/login",
                new { email = SuperAdminEmail, password = SuperAdminPassword });
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            return json.GetProperty("accessToken").GetString();
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    /// <summary>
    /// The seeded client. The requests and cases the client documents describe belong to Daniel,
    /// not to the suite's default user — capturing them as Sarah produced screenshots of empty
    /// lists, which is what the <c>proves</c> assertions now catch.

    [Test]
    [Description("requesting-an-investigation: the request wizard, as a client sees it.")]
    public async Task Capture_RequestingAnInvestigation()
    {
        await LoginAsync(ClientEmail, ClientPassword);

        await GoAsync("/my-requests/new");
        await ShootAsync("requesting-an-investigation", "new-request.png");

        await GoAsync("/my-requests");
        await ShootAsync("requesting-an-investigation", "my-requests.png", proves: "Belmont");
    }

    // ── Signed in ─────────────────────────────────────────────────────────────

    [Test]
    [Description("your-profile: the profile screen and its two-photo section.")]
    public async Task Capture_YourProfile()
    {
        await LoginAsync(UserEmail, UserPassword);

        await GoAsync("/profile");
        // Wait for the tabs, not just the page: the hero band renders before the profile loads,
        // and a shot taken between the two captures a page with no content under its heading.
        await Page.GetByRole(AriaRole.Tab, new() { Name = "About" })
                  .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 20_000 });
        await ShootAsync("your-profile", "profile.png");
    }

    [Test]
    [Description("your-case: the client's own view of a case.")]
    public async Task Capture_YourCase()
    {
        await LoginAsync(ClientEmail, ClientPassword);

        await GoAsync("/my-cases");
        await ShootAsync("your-case", "my-cases.png", proves: "Case");
    }

    [Test]
    [Description("your-equipment: a personal inventory and the public catalog.")]
    public async Task Capture_YourEquipment()
    {
        await LoginAsync(UserEmail, UserPassword);

        await GoAsync("/my-equipment");
        await ShootAsync("your-equipment", "my-equipment.png", proves: "Field Recorder");

        await GoAsync("/equipment-catalog");
        // The catalog opens on its "Makes & models" tab, so the proof is a make the seed
        // added — the owned-item names live behind the second tab.
        await ShootAsync("your-equipment", "catalog.png", proves: "FLIR");
    }

    [Test]
    [Description("borrowing-equipment: gear out on loan, and a request waiting on the owner.")]
    public async Task Capture_BorrowingEquipment()
    {
        await LoginAsync(UserEmail, UserPassword);

        // Sarah is on both sides of a loan in the seed data: she has James's spirit box out, and
        // he has asked for her thermal camera. Both halves of the screen are therefore populated.
        await GoAsync("/my-checkouts");
        await ShootAsync("borrowing-equipment", "my-checkouts.png", proves: "Spirit Box");
    }

    // ── The video editor ──────────────────────────────────────────────────────

    /// <summary>
    /// The editor is reached through the site at /my-videos, where the seeded demo footage is
    /// already in the media library.
    /// </summary>
    private async Task OpenEditorAsync()
    {
        await LoginAsync(UserEmail, UserPassword);
        await GoAsync("/my-videos");

        // The editor is a large component tree behind a circuit; it takes noticeably longer to
        // appear than an ordinary page.
        await Expect(Page.Locator(".bv-editor")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Page.WaitForTimeoutAsync(1_500);
    }

    /// <summary>Switches the media panel to one of its tabs (Video, Audio, Image, Server).</summary>
    private async Task OpenMediaTabAsync(string name)
    {
        // An import dialog left open swallows every later click through its overlay, and the
        // failure then points at the tab rather than at the dialog sitting on top of it.
        await DismissImportDialogAsync();

        var tab = Page.GetByRole(AriaRole.Tab, new() { Name = name, Exact = true })
                      .Or(Page.Locator(".k-tabstrip-item", new() { HasTextString = name }))
                      .First;
        await Expect(tab).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await tab.ClickAsync();
        await Page.WaitForTimeoutAsync(800);
    }

    /// <summary>
    /// Answers the Insert/Overwrite prompt that appears when a clip is dropped onto a spot that
    /// is already occupied. Chooses Insert, which is the non-destructive answer.
    /// </summary>
    private async Task AnswerPlacementPromptAsync()
    {
        var insert = Page.GetByRole(AriaRole.Button, new() { Name = "Insert (Make Room)" });

        // The prompt is not instant — it appears once the clip is ready to be placed.
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline && await insert.CountAsync() == 0)
            await Page.WaitForTimeoutAsync(500);

        if (await insert.CountAsync() == 0) return;

        if (!_capturedPlacementPrompt)
        {
            await ShootAsync("using-the-video-editor", "insert-or-overwrite.png");
            _capturedPlacementPrompt = true;
        }

        await insert.First.EvaluateAsync("el => el.click()");
        await WaitForAsync(async () => await insert.CountAsync() == 0,
                           20, "the placement prompt would not close");
    }

    private bool _capturedPlacementPrompt;

    /// <summary>Closes the import summary dialog if it is open.</summary>
    private async Task DismissImportDialogAsync()
    {
        var done = Page.GetByRole(AriaRole.Button, new() { Name = "Done", Exact = true });
        if (await done.CountAsync() == 0) return;

        await done.First.EvaluateAsync("el => el.click()");
        await WaitForAsync(async () => await done.CountAsync() == 0,
                           20, "the import dialog would not close");
    }

    [Test]
    [Description("using-the-video-editor: the editor, its media library and a populated timeline.")]
    public async Task Capture_UsingTheVideoEditor()
    {
        await OpenEditorAsync();

        await ShootAsync("using-the-video-editor", "editor-overview.png");

        await OpenMediaTabAsync("Server");
        await ShootAsync("using-the-video-editor", "media-library.png", proves: "porch-camera.mp4");

        await InitializeFfmpegAsync();

        // Bringing a file over is two steps by design (phase 150): the card downloads and caches
        // it, and the clip that appears on the Video tab is what goes onto the timeline.
        await ImportFromServerAsync("porch-camera");
        // A clip on the timeline is a "chip".
        await Expect(Page.Locator(".bv-clip-chip").First)
            .ToBeVisibleAsync(new() { Timeout = 120_000 });

        await ImportFromServerAsync("hallway-camera");
        await Page.WaitForTimeoutAsync(2_000);

        await ShootAsync("using-the-video-editor", "timeline-two-clips.png");

        // ── The panels a clip opens ───────────────────────────────────────────
        await Page.Locator(".bv-clip-chip").First.ClickAsync();
        await Page.WaitForTimeoutAsync(800);
        await OpenMediaTabAsync("Properties");
        await ShootAsync("using-the-video-editor", "clip-properties.png");

        // ── Export ────────────────────────────────────────────────────────────
        // Captured before the overlays below: adding one starts a background render, and Export
        // is disabled while ffmpeg is working. See backlog item 94 — that render does not always
        // finish, which is a defect in its own right and not something to wait on here.
        await WaitForReadyWithTrailAsync(240);

        // DOM click: the media panel floats over the right-hand end of the toolbar, so a real
        // pointer click on a button beneath it is intercepted.
        await Page.GetByTitle("Render the final video locally in the browser (ffmpeg.wasm), then save or upload")
                  .First.EvaluateAsync("el => el.click()");
        await Page.WaitForTimeoutAsync(1_500);
        await ShootAsync("using-the-video-editor", "export-dialog.png");
        await CloseAnyWindowAsync();

        // ── The native helper ─────────────────────────────────────────────────
        await Page.GetByTitle("Native acceleration status — click to manage").First
                  .EvaluateAsync("el => el.click()");
        await Page.WaitForTimeoutAsync(1_000);
        await ShootAsync("using-the-video-editor", "sidecar-panel.png");
        await CloseAnyWindowAsync();

        // ── Titles and callouts ───────────────────────────────────────────────
        await ClickTimelineToolAsync("Add text overlay");
        await Page.WaitForTimeoutAsync(1_500);
        await ShootAsync("using-the-video-editor", "text-overlay.png");

        await ClickTimelineToolAsync("Add callout shape (rectangle, ellipse, arrow…)");
        await Page.WaitForTimeoutAsync(1_500);
        await ShootAsync("using-the-video-editor", "callout.png");
    }

    /// <summary>
    /// Clicks one of the timeline's add-a-thing buttons by its title.
    /// </summary>
    /// <remarks>
    /// By title and by DOM click: the media panel floats over the right-hand end of the timeline
    /// toolbar, so a real pointer click on a button underneath it is intercepted and times out
    /// pointing at the button rather than at the panel on top of it.
    /// </remarks>
    private async Task ClickTimelineToolAsync(string title)
    {
        var button = Page.GetByTitle(title).First;
        await Expect(button).ToBeAttachedAsync(new() { Timeout = 10_000 });
        await button.EvaluateAsync("el => el.click()");
    }

    /// <summary>Closes the topmost Telerik window, whatever it is.</summary>
    private async Task CloseAnyWindowAsync()
    {
        var close = Page.Locator(".k-window-actions button, .k-window .k-i-x, .k-window [title='Close']").Last;
        if (await close.CountAsync() > 0)
        {
            await close.EvaluateAsync("el => el.click()");
            await Page.WaitForTimeoutAsync(800);
        }
    }

    /// <summary>
    /// Loads the ffmpeg core, which every import and export goes through. It is not loaded on
    /// arrival — the editor waits to be asked, so opening a project costs nothing.
    /// </summary>
    private async Task InitializeFfmpegAsync()
    {
        var status = Page.Locator(".bv-toolbar__status");
        if ((await status.InnerTextAsync()).Contains("Ready", StringComparison.OrdinalIgnoreCase))
            return;

        await Page.GetByRole(AriaRole.Button, new() { Name = "Initialize" }).First.ClickAsync();

        // The core is a multi-megabyte wasm download on the first run.
        await Expect(status).ToHaveTextAsync(new Regex("Ready"), new() { Timeout = 120_000 });
    }

    /// <summary>
    /// Brings a Server-tab file onto the timeline. Two clicks, by design (phase 150): the first
    /// downloads and caches it, the second adds the cached file to the timeline. A single click
    /// caches and stops, which is why the first attempt at this waited forever for a clip that was
    /// never going to appear.
    /// </summary>
    private async Task ImportFromServerAsync(string namePart)
    {
        await OpenMediaTabAsync("Server");

        var card = Page.Locator(".bv-clip-card").Filter(new() { HasTextString = namePart }).First;
        await Expect(card).ToBeVisibleAsync(new() { Timeout = 15_000 });

        // Not the card's centre. A video card's middle is its thumbnail, which carries a
        // "load a preview" button that stops propagation — clicking there looks like a click on
        // the card and starts nothing at all. The name/meta block bubbles to the card's handler.
        var target = card.Locator(".bv-clip-card__info").First;
        var chipsBefore = await Page.Locator(".bv-clip-chip").CountAsync();

        if (await card.Locator(".bv-clip-card__cached-badge").CountAsync() == 0)
        {
            await target.ClickAsync();
            await WaitForAsync(
                async () => await card.Locator(".bv-clip-card__cached-badge").CountAsync() > 0,
                60, $"{namePart} never finished downloading to the browser cache");
        }

        // The card pulses while it is busy, and Playwright refuses to click a moving element.
        await WaitForAsync(
            async () => (await card.GetAttributeAsync("class") ?? "").Contains("busy") == false,
            30, $"{namePart}'s card never came out of its busy state");

        await target.ClickAsync();

        // Importing ends on a summary dialog, and its overlay swallows every later click until it
        // is dismissed. The first run of this fixture spent 30 seconds timing out on a tab that
        // was behind it.
        var done = Page.GetByRole(AriaRole.Button, new() { Name = "Done", Exact = true });
        await WaitForAsync(
            async () => await done.CountAsync() > 0
                     || await Page.Locator(".bv-clip-chip").CountAsync() > chipsBefore,
            120, $"{namePart} never reached the timeline");

        if (await done.CountAsync() > 0)
        {
            if (!_capturedImportDialog)
            {
                await ShootAsync("using-the-video-editor", "import-complete.png");
                _capturedImportDialog = true;
            }

            // A real click — even a forced one — lands on the dialog while it is still settling
            // and does nothing. Dispatching the DOM click reaches Blazor's handler directly;
            // this is a button, not a Telerik-bound input, so the event is honoured.
            await done.First.EvaluateAsync("el => el.click()");

            // Wait on the button's own disappearance, not on the overlay: the media panel is a
            // window too, so an overlay is on the page whether or not the import dialog is up —
            // waiting for zero overlays never came true and reported a dialog that had closed.
            await WaitForAsync(async () => await done.CountAsync() == 0,
                               20, "the import dialog would not close");
        }

        // Landing a clip where something already sits asks how to place it. Insert keeps what is
        // there and shuffles it along; Overwrite replaces the overlap.
        await AnswerPlacementPromptAsync();

        await WaitForAsync(
            async () => await Page.Locator(".bv-clip-chip").CountAsync() > chipsBefore,
            60, $"{namePart} imported but never appeared on the timeline");
    }

    private bool _capturedImportDialog;

    /// <summary>
    /// Waits for ffmpeg to go back to Ready, recording what the status said along the way.
    /// </summary>
    /// <remarks>
    /// The trail is the whole point: a background render that is merely slow and one that is
    /// wedged look identical in a single reading, and the difference decides whether the fix is
    /// "wait longer" or "something is broken". Reporting the samples turns that into evidence.
    /// </remarks>
    private async Task WaitForReadyWithTrailAsync(int seconds)
    {
        var status = Page.Locator(".bv-toolbar__status").First;
        var trail = new List<string>();
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        var started = DateTime.UtcNow;

        while (DateTime.UtcNow < deadline)
        {
            var text = (await status.InnerTextAsync()).Trim();
            var sample = $"{(int)(DateTime.UtcNow - started).TotalSeconds}s:{text}";
            if (trail.Count == 0 || trail[^1][(trail[^1].IndexOf(':') + 1)..] != text)
                trail.Add(sample);

            if (text.Contains("Ready", StringComparison.OrdinalIgnoreCase))
            {
                TestContext.Out.WriteLine("render trail: " + string.Join(" → ", trail));
                return;
            }

            await Page.WaitForTimeoutAsync(3_000);
        }

        await ReportEditorStateAsync(
            "ffmpeg never went back to Ready, so Export stayed disabled.\n"
            + "  render trail: " + string.Join(" → ", trail));
    }

    /// <summary>Polls a condition, and reports what the editor was showing when it never came true.</summary>
    private async Task WaitForAsync(Func<Task<bool>> condition, int seconds, string whatFailed)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition()) return;
            await Page.WaitForTimeoutAsync(500);
        }

        await ReportEditorStateAsync(whatFailed);
    }

    /// <summary>
    /// The console lines around the first <c>Aborted()</c>.
    /// </summary>
    /// <remarks>
    /// Once the wasm module aborts, every later command aborts too, so the tail of the log is all
    /// collateral and says nothing about the cause. The command immediately before the first abort
    /// is the one that killed it.
    /// </remarks>
    private List<string> FirstAbortWindow()
    {
        var ffmpeg = _console.Where(c => c.Contains("ffmpeg")).ToList();
        var first = ffmpeg.FindIndex(c => c.Contains("Aborted()"));
        if (first < 0) return ["(no abort recorded)"];

        var from = Math.Max(0, first - 5);
        return ffmpeg.GetRange(from, Math.Min(8, ffmpeg.Count - from));
    }

    /// <summary>Fails with what the editor was actually showing, including its console output.</summary>
    private async Task ReportEditorStateAsync(string what)
    {
        var status = await Page.Locator(".bv-toolbar__status").First.InnerTextAsync();
        var errors = await Page.Locator(".bv-browser__error").AllInnerTextsAsync();
        var editor = await Page.Locator(".bv-editor").First.InnerTextAsync();
        var windows = await Page.Locator(".k-window").AllInnerTextsAsync();
        var buttons = await Page.Locator(".k-window button").AllInnerTextsAsync();
        var overlays = await Page.Locator(".k-overlay").CountAsync();

        Assert.Fail($"{what}.\n"
                    + $"  open windows: {windows.Count} | overlays: {overlays}\n"
                    + $"  window buttons: {string.Join(" | ", buttons.Select(b => b.Trim()).Where(b => b.Length > 0))}\n"
                    + $"  ffmpeg status: {status}\n"
                    + $"  browser panel errors: {string.Join(" | ", errors)}\n"
                    + $"  around the first abort:\n            "
                    + string.Join("\n            ", FirstAbortWindow()) + "\n"
                    + $"  editor text: {editor.Replace("\n", " / ")}");
    }

    /// <summary>Adds an imported clip to the timeline via its card's + button.</summary>
    private async Task AddToTimelineAsync(string namePart)
    {
        await OpenMediaTabAsync("Video");

        var card = Page.Locator(".bv-clip-card").Filter(new() { HasTextString = namePart }).First;
        await Expect(card).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await card.GetByTitle("Add to timeline").First.ClickAsync();
        await Page.WaitForTimeoutAsync(1_200);
    }




    // ── Group members ─────────────────────────────────────────────────────────

    [Test]
    [Description("working-a-case: the group's case list, and a case with its tabs.")]
    public async Task Capture_WorkingACase()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);

        if (!await OpenOrganizationAsync("Paranormal365"))
            Assert.Ignore("Seed org 'Paranormal365' not present.");

        await ShootAsync("working-a-case", "org-hub.png");

        // The document is about a case, not about the hub that lists them, so the picture beside
        // "The case tabs" has to be a case that is actually open.
        if (!await OpenOrgCaseAsync("Paranormal365", "Bell Witch"))
            Assert.Ignore("Seed case not present in Paranormal365.");

        await ShootAsync("working-a-case", "case-detail.png");
    }

    // ── Group administrators (gated) ──────────────────────────────────────────

    [Test]
    [Description("organization-administration: settings, members and the calendar. Gated.")]
    public async Task Capture_OrganizationAdministration()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);

        if (!await OpenOrganizationAsync("Paranormal365"))
            Assert.Ignore("Seed org 'Paranormal365' not present.");

        var orgUrl = Page.Url;

        await GoAsync(new Uri(orgUrl).AbsolutePath + "/members");
        await ShootAsync("organization-administration", "members.png", gated: true);

        await GoAsync(new Uri(orgUrl).AbsolutePath + "/calendar");
        await ShootAsync("organization-administration", "calendar.png", gated: true);

        await GoAsync(new Uri(orgUrl).AbsolutePath + "/cms");
        await ShootAsync("organization-administration", "cms.png", gated: true);
    }

    // ── Site administrators (gated) ───────────────────────────────────────────

    [Test]
    [Description("site-administration: the admin screens. Gated.")]
    public async Task Capture_SiteAdministration()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);

        await GoAsync("/admin/site-settings");
        await ShootAsync("site-administration", "site-settings.png", gated: true);

        await GoAsync("/admin/support-tickets");
        await ShootAsync("site-administration", "support-tickets.png", gated: true);

        await GoAsync("/admin/audit-log");
        await ShootAsync("site-administration", "audit-log.png", gated: true);

        await GoAsync("/admin/equipment-taxonomy");
        await ShootAsync("site-administration", "equipment-taxonomy.png", gated: true);
    }

    // ── Publications ──────────────────────────────────────────────────────────

    /// <summary>
    /// The publications directory, a publication, the group's authoring tab, and the writing page.
    /// </summary>
    /// <remarks>
    /// <para><b>Two audiences in one run.</b> The two reading shots are captured <i>signed out</i>,
    /// because that is who the document is for and because a signed-in capture would not prove the
    /// page works for a visitor. The two authoring shots are gated, and taken as an administrator.
    /// </para>
    ///
    /// <para><b>The content is seeded through the API, idempotently.</b> Driving the UI to write a
    /// post would make this capture a second, worse copy of the authoring tests; and creating a new
    /// publication on every run would leave a dev site with field-notes, field-notes-2,
    /// field-notes-3 — the slug de-duplication working exactly as designed, producing junk. So it
    /// reuses the publication if it is already there.</para>
    ///
    /// <para>Publications are off by default, so the switch is read first and put back afterwards.
    /// </para>
    /// </remarks>
    [Test]
    [Description("reading-publications and publishing-with-publications: the directory, a publication, and authoring.")]
    public async Task Capture_Publications()
    {
        var wasOn = await FlagAsync(PublicationsFlag);
        await SetFlagAsync(PublicationsFlag, true);

        try
        {
            var seeded = await EnsurePublicationForCaptureAsync();
            if (seeded is null)
                Assert.Ignore("Could not seed a publication — is the API up and the seed org present?");

            var (orgId, publicationId, urlName, postId) = seeded.Value;

            // ── As a visitor ──────────────────────────────────────────────────
            await LogoutAsync();

            await GoAsync("/publications");
            await ShootAsync("publications", "directory.png", proves: PublicationTitle);

            await GoAsync($"/publications/{urlName}");
            await ShootAsync("publications", "publication.png", proves: PostTitle);

            // ── As an administrator ───────────────────────────────────────────
            await LoginAsync(SuperAdminEmail, SuperAdminPassword);

            await GoAsync($"/organizations/{orgId}");
            await OpenTabAsync("Publications", Main.GetByText(PublicationTitle).First);
            await ShootAsync("publications", "org-tab.png", gated: true, proves: PublicationTitle);

            await GoAsync($"/organizations/{orgId}/publications/{publicationId}/posts/{postId}");

            // The title is in an input, so its text is a value rather than page text and
            // ShootAsync's `proves` cannot see it. Assert the value directly — the point of the
            // check either way is that the shot is not of an empty editor.
            await Expect(Page.Locator("#post-title")).ToHaveValueAsync(
                PostTitle, new() { Timeout = 20_000 });

            await ShootAsync("publications", "post-editor.png", gated: true, proves: "Excerpt");
        }
        finally
        {
            await SetFlagAsync(PublicationsFlag, wasOn);
        }
    }

    /// <summary>
    /// The feature key, spelled out rather than referenced.
    /// </summary>
    /// <remarks>
    /// This project has no reference to the site's assemblies on purpose — it drives the running
    /// site over HTTP, the way a browser does. The string is the API's contract here, same as the
    /// URLs above it.
    /// </remarks>
    private const string PublicationsFlag = "features.publications";

    private const string PublicationTitle = "Field Notes";
    private const string PostTitle        = "A quiet night at the Belmont house";

    /// <summary>
    /// Makes sure the demo publication and its published post exist, and returns their ids.
    /// </summary>
    /// <remarks>
    /// Everything here is find-or-create. Run it ten times and the site has one publication with
    /// one post, which is what the screenshots need and what a developer's dev site can live with.
    /// </remarks>
    private async Task<(string OrgId, string PublicationId, string UrlName, string PostId)?>
        EnsurePublicationForCaptureAsync()
    {
        var token = await AdminTokenForCaptureAsync();
        if (token is null) return null;

        using var http = new HttpClient { BaseAddress = new Uri(ApiUrl) };
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var orgs = await http.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/organizations");
        string? orgId = null;
        foreach (var org in orgs.EnumerateArray())
        {
            var name = org.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (name is null || !name.Contains("Paranormal365", StringComparison.OrdinalIgnoreCase))
                continue;

            orgId = org.GetProperty("id").GetString();
            break;
        }

        if (orgId is null) return null;

        // ── The publication ───────────────────────────────────────────────────
        var existing = await http.GetFromJsonAsync<System.Text.Json.JsonElement>(
            $"/api/organizations/{orgId}/publications");

        string? publicationId = null, urlName = null;
        foreach (var publication in existing.EnumerateArray())
        {
            if (publication.GetProperty("title").GetString() != PublicationTitle) continue;
            publicationId = publication.GetProperty("id").GetString();
            urlName       = publication.GetProperty("urlName").GetString();
            break;
        }

        if (publicationId is null)
        {
            using var created = await http.PostAsJsonAsync(
                $"/api/organizations/{orgId}/publications",
                new
                {
                    title = PublicationTitle,
                    description = "What we found, written up properly — one case at a time.",
                    isPublic = true,
                });

            if (!created.IsSuccessStatusCode) return null;

            var record = await created.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            publicationId = record.GetProperty("id").GetString();
            urlName       = record.GetProperty("urlName").GetString();
        }

        // ── The post ──────────────────────────────────────────────────────────
        var posts = await http.GetFromJsonAsync<System.Text.Json.JsonElement>(
            $"/api/organizations/{orgId}/publications/{publicationId}/posts");

        string? postId = null;
        var alreadyPublished = false;
        foreach (var post in posts.EnumerateArray())
        {
            if (post.GetProperty("title").GetString() != PostTitle) continue;
            postId = post.GetProperty("id").GetString();
            alreadyPublished = post.TryGetProperty("publishedUtc", out var published)
                            && published.ValueKind != System.Text.Json.JsonValueKind.Null;
            break;
        }

        if (postId is null)
        {
            using var created = await http.PostAsJsonAsync(
                $"/api/organizations/{orgId}/publications/{publicationId}/posts",
                new
                {
                    title = PostTitle,
                    excerpt = "Four hours, two recorders and one thing none of us can explain — "
                            + "and three that turned out to be the boiler.",
                    bodyHtml =
                        "<p>We arrived at the Belmont house a little after nine. The owners had "
                        + "described footsteps on the landing, always between two and three in the "
                        + "morning, always moving away from the stairs.</p>"
                        + "<h2>What we set up</h2>"
                        + "<p>Two static cameras — one at the foot of the stairs, one at the far "
                        + "end of the landing — and a recorder in the box room, which is where the "
                        + "owners said the sound seemed to end.</p>"
                        + "<h2>What we heard</h2>"
                        + "<p>Three of the four events we logged have ordinary explanations. The "
                        + "boiler cycles at ten past the hour and the pipes under the landing "
                        + "floor knock as it does. That accounts for the timing, and very nearly "
                        + "for the direction.</p>"
                        + "<p>The fourth does not, and we are not going to pretend otherwise "
                        + "here. It is on both recorders, eleven seconds apart, which is roughly "
                        + "the walk between them.</p>",
                });

            if (!created.IsSuccessStatusCode) return null;

            var record = await created.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            postId = record.GetProperty("id").GetString();
        }

        if (!alreadyPublished)
        {
            using var _ = await http.PostAsJsonAsync(
                $"/api/organizations/{orgId}/publications/{publicationId}/posts/{postId}/publish?published=true",
                new { });
        }

        return (orgId, publicationId!, urlName!, postId!);
    }
}
