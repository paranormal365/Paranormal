using Microsoft.Playwright;
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

    [SetUp]
    public async Task RequireOptInAndGoDark()
    {
        if (Environment.GetEnvironmentVariable("BEN_CAPTURE") != "1")
            Assert.Ignore("Set BEN_CAPTURE=1 to re-capture the help screenshots.");

        await Context.AddInitScriptAsync(DarkModeInitScript);
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
    /// The seeded client. The requests and cases the client documents describe belong to Daniel,
    /// not to the suite's default user — capturing them as Sarah produced screenshots of empty
    /// lists, which is what the <c>proves</c> assertions now catch.
    /// </summary>
    private const string ClientEmail    = "daniel.park@benco.dev";
    private const string ClientPassword = "D@niel!Park2026";

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

    // ── Group members ─────────────────────────────────────────────────────────

    [Test]
    [Description("working-a-case: the group's case list, and a case with its tabs.")]
    public async Task Capture_WorkingACase()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);

        if (!await OpenOrganizationAsync("Tennessee Ghost Hunters"))
            Assert.Ignore("Seed org 'Tennessee Ghost Hunters' not present.");

        await ShootAsync("working-a-case", "org-hub.png");

        // The document is about a case, not about the hub that lists them, so the picture beside
        // "The case tabs" has to be a case that is actually open.
        if (!await OpenOrgCaseAsync("Tennessee Ghost Hunters", "Bell Witch"))
            Assert.Ignore("Seed case not present in Tennessee Ghost Hunters.");

        await ShootAsync("working-a-case", "case-detail.png");
    }

    // ── Group administrators (gated) ──────────────────────────────────────────

    [Test]
    [Description("organization-administration: settings, members and the calendar. Gated.")]
    public async Task Capture_OrganizationAdministration()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);

        if (!await OpenOrganizationAsync("Tennessee Ghost Hunters"))
            Assert.Ignore("Seed org 'Tennessee Ghost Hunters' not present.");

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
}
