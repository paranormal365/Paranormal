using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Capture;

/// <summary>
/// Captures the screenshots the investor overview document embeds (docs/investor-overview.html).
/// </summary>
/// <remarks>
/// <para>Separate from <see cref="HelpMediaCapture"/> because the audiences differ: help
/// screenshots teach a user where things are, so they show the whole screen; investor shots sell
/// the product, so each one is CROPPED to the part that makes its point, and the development
/// announcement banner is hidden with capture-time CSS — the page is untouched, only the shot.</para>
///
/// <para>Same opt-in as the help capture — this writes PNGs into the working tree:</para>
/// <code>
/// BEN_CAPTURE=1 dotnet test Ben.Web.Playwright -p:IsTestProject=true --no-build \
///   --filter FullyQualifiedName~InvestorMediaCapture
/// </code>
/// </remarks>
[TestFixture]
[Category("Capture")]
[NonParallelizable]
public sealed class InvestorMediaCapture : BenTestBase
{
    public override BrowserNewContextOptions ContextOptions() => new()
    {
        ViewportSize      = new ViewportSize { Width = 1440, Height = 900 },
        DeviceScaleFactor = 2,
        ColorScheme       = ColorScheme.Dark,
    };

    private const string DarkModeInitScript = """
        try {
            localStorage.setItem('layoutSettings', JSON.stringify({ theme: 'dark' }));
            localStorage.setItem('ben-theme', 'dark');
        } catch (e) { }
        """;

    /// <summary>
    /// Capture-time-only cosmetics: the development announcement has no place in a sales
    /// document, and the operator's own avatar has no place in any published picture.
    /// </summary>
    private const string InvestorShotCss = """
        #site-announcement { display: none !important; }
        .action-needed-banner { display: none !important; }
        header img.profile-image, .page-header img.profile-image {
            filter: grayscale(1) brightness(0) opacity(0.35) !important;
        }
        """;

    [SetUp]
    public async Task RequireOptInAndGoDark()
    {
        if (Environment.GetEnvironmentVariable("BEN_CAPTURE") != "1")
            Assert.Ignore("Set BEN_CAPTURE=1 to re-capture the investor screenshots.");

        await Context.AddInitScriptAsync(DarkModeInitScript);
    }

    private static string PathFor(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;
        Assert.That(dir, Is.Not.Null, "Could not find the repository root.");

        var media = Path.Combine(dir!.FullName, "docs", "investor-media");
        Directory.CreateDirectory(media);
        return Path.Combine(media, name);
    }

    /// <summary>Shoots one element, settled and banner-free.</summary>
    private async Task ShootAsync(string name, string selector, string? proves = null)
    {
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await WaitUntilLoadedAsync();

        if (proves is not null)
            await Expect(Page.GetByText(proves, new() { Exact = false }).First)
                .ToBeVisibleAsync(new() { Timeout = 15_000 });

        await Page.AddStyleTagAsync(new() { Content = InvestorShotCss });
        await Page.WaitForTimeoutAsync(600);

        var target = Page.Locator(selector).First;
        await Expect(target).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await target.ScrollIntoViewIfNeededAsync();
        await Page.WaitForTimeoutAsync(300);

        var path = PathFor(name);
        await target.ScreenshotAsync(new() { Path = path });
        Assert.That(new FileInfo(path).Length, Is.GreaterThan(0), $"{name} captured empty.");
        TestContext.Out.WriteLine($"captured {name} ({new FileInfo(path).Length / 1024} KB)");
    }

    private async Task GoAsync(string route)
    {
        await Page.GotoAsync($"{BaseUrl}{route}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    [Test]
    [Description("The signed-out shots: home hero and the public case map.")]
    public async Task Capture_PublicFace()
    {
        await LogoutAsync();

        await GoAsync("/");
        await ShootAsync("home-hero.png", ".home-hero");

        // The public investigations map with its worldwide cases — proof the public side is live.
        // Wait for the markers, not just the tiles: an empty map sells the opposite.
        await Expect(Page.Locator(".case-map-cluster, .case-map-single").First).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Page.WaitForTimeoutAsync(2_000);
        await ShootAsync("public-map.png", ".k-map");
    }

    [Test]
    [Description("A worked case, as the group sees it.")]
    public async Task Capture_CaseWork()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);

        if (!await OpenOrganizationAsync("Tennessee Ghost Hunters"))
            Assert.Ignore("Seed org 'Tennessee Ghost Hunters' not present.");
        await ShootAsync("org-hub.png", ".content-wrapper");

        if (!await OpenOrgCaseAsync("Tennessee Ghost Hunters", "Bell Witch"))
            Assert.Ignore("Seed case not present.");
        await ShootAsync("case-detail.png", ".content-wrapper", proves: "Case Summary");
    }

    [Test]
    [Description("The feed, with a real post in it.")]
    public async Task Capture_Feed()
    {
        // The feed flag is read and restored by the help capture; here we only capture when the
        // feed is already reachable, and skip harmlessly otherwise — an investor capture must
        // not flip site switches.
        await LoginAsync(UserEmail, UserPassword);
        await GoAsync("/feed");

        if (await Page.Locator("#feed-composer").CountAsync() == 0)
            Assert.Ignore("The feed is switched off; run the help capture's feed test first or enable features.public-feed.");

        await ShootAsync("feed.png", ".container.py-3", proves: "Post");
        // The raw shot is the whole column; the document wants the composer and the top post.
        // Cropped by the build step (docs/build-investor-pdf.sh) rather than here.
    }

    [Test]
    [Description("A published publication post, as a visitor reads it.")]
    public async Task Capture_Publication()
    {
        await LogoutAsync();
        await GoAsync("/publications");

        if (await Page.GetByText("Field Notes").CountAsync() == 0)
            Assert.Ignore("Publications are off or the demo publication is missing.");

        // The reader page for the seeded post, reached by its address the way a shared link is.
        await GoAsync("/publications/field-notes");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var postLink = Page.GetByText("A quiet night at the Belmont house").First;
        if (await postLink.CountAsync() > 0)
        {
            await postLink.ClickAsync();
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        }
        await ShootAsync("publication.png", ".content-wrapper", proves: "Belmont");
    }

    [Test]
    [Description("The billing machinery: price bands as the site administrator manages them.")]
    public async Task Capture_Billing()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await GoAsync("/admin/subscription-tiers");
        await ShootAsync("admin-tiers.png", ".content-wrapper", proves: "Price Bands");
    }

    [Test]
    [Description("The video editor with footage on the timeline.")]
    public async Task Capture_Editor()
    {
        await LoginAsync(UserEmail, UserPassword);
        await GoAsync("/my-videos");
        await Expect(Page.Locator(".bv-editor")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Page.WaitForTimeoutAsync(1_500);

        // An empty editor sells nothing. Same two-click server import the help capture proved
        // out (phase 150: first click caches, second click places).
        await InitializeFfmpegAsync();
        await ImportFromServerAsync("porch-camera");
        await ImportFromServerAsync("hallway-camera");
        await Page.WaitForTimeoutAsync(2_000);

        // Imports kick off a background segment render; "Processing… 65%" in the toolbar is not
        // the picture to print. Wait for Ready, tolerating that it can take a while.
        var status = Page.Locator(".bv-toolbar__status").First;
        var deadline = DateTime.UtcNow.AddSeconds(240);
        while (DateTime.UtcNow < deadline
               && !(await status.InnerTextAsync()).Contains("Ready", StringComparison.OrdinalIgnoreCase))
            await Page.WaitForTimeoutAsync(3_000);

        await ShootAsync("editor.png", ".bv-editor");
    }

    // ── Editor import machinery (condensed from HelpMediaCapture) ────────────

    private async Task InitializeFfmpegAsync()
    {
        var status = Page.Locator(".bv-toolbar__status");
        if ((await status.InnerTextAsync()).Contains("Ready", StringComparison.OrdinalIgnoreCase))
            return;

        await Page.GetByRole(AriaRole.Button, new() { Name = "Initialize" }).First.ClickAsync();
        await Expect(status).ToHaveTextAsync(
            new System.Text.RegularExpressions.Regex("Ready"), new() { Timeout = 120_000 });
    }

    private async Task ImportFromServerAsync(string namePart)
    {
        await OpenMediaTabAsync("Server");

        var card = Page.Locator(".bv-clip-card").Filter(new() { HasTextString = namePart }).First;
        await Expect(card).ToBeVisibleAsync(new() { Timeout = 15_000 });

        // The name/meta block, not the thumbnail — the thumbnail's preview button stops
        // propagation and the click starts nothing.
        var target = card.Locator(".bv-clip-card__info").First;
        var chipsBefore = await Page.Locator(".bv-clip-chip").CountAsync();

        if (await card.Locator(".bv-clip-card__cached-badge").CountAsync() == 0)
        {
            await target.ClickAsync();
            await WaitForAsync(
                async () => await card.Locator(".bv-clip-card__cached-badge").CountAsync() > 0,
                60, $"{namePart} never finished caching");
        }

        await WaitForAsync(
            async () => (await card.GetAttributeAsync("class") ?? "").Contains("busy") == false,
            30, $"{namePart}'s card stayed busy");

        await target.ClickAsync();

        var done = Page.GetByRole(AriaRole.Button, new() { Name = "Done", Exact = true });
        await WaitForAsync(
            async () => await done.CountAsync() > 0
                     || await Page.Locator(".bv-clip-chip").CountAsync() > chipsBefore,
            120, $"{namePart} never reached the timeline");

        if (await done.CountAsync() > 0)
        {
            await done.First.EvaluateAsync("el => el.click()");
            await WaitForAsync(async () => await done.CountAsync() == 0,
                               20, "the import dialog would not close");
        }

        // Landing on occupied track asks Insert/Overwrite; Insert is non-destructive.
        var insert = Page.GetByRole(AriaRole.Button, new() { Name = "Insert (Make Room)" });
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline
               && await insert.CountAsync() == 0
               && await Page.Locator(".bv-clip-chip").CountAsync() <= chipsBefore)
            await Page.WaitForTimeoutAsync(500);
        if (await insert.CountAsync() > 0)
        {
            await insert.First.EvaluateAsync("el => el.click()");
            await WaitForAsync(async () => await insert.CountAsync() == 0,
                               20, "the placement prompt would not close");
        }

        await WaitForAsync(
            async () => await Page.Locator(".bv-clip-chip").CountAsync() > chipsBefore,
            60, $"{namePart} imported but never appeared on the timeline");
    }

    private async Task OpenMediaTabAsync(string name)
    {
        var done = Page.GetByRole(AriaRole.Button, new() { Name = "Done", Exact = true });
        if (await done.CountAsync() > 0)
        {
            await done.First.EvaluateAsync("el => el.click()");
            await WaitForAsync(async () => await done.CountAsync() == 0,
                               20, "the import dialog would not close");
        }

        var tab = Page.GetByRole(AriaRole.Tab, new() { Name = name, Exact = true })
                      .Or(Page.Locator(".k-tabstrip-item", new() { HasTextString = name }))
                      .First;
        await Expect(tab).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await tab.ClickAsync();
        await Page.WaitForTimeoutAsync(800);
    }

    private async Task WaitForAsync(Func<Task<bool>> condition, int seconds, string whatFailed)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition()) return;
            await Page.WaitForTimeoutAsync(500);
        }
        Assert.Fail(whatFailed);
    }
}
