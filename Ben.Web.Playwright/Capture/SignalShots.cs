using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Capture;

/// <summary>
/// Photographs the signed-in site in both themes while Signal is being built.
/// </summary>
/// <remarks>
/// <para><b>Why this exists rather than a browser and a password.</b> The interesting screens are
/// behind a sign-in, and the seeded passwords are never typed by hand — they reach the suite from
/// the environment (<c>scripts/run-e2e.sh</c> reads them out of the gitignored dev settings) and
/// <see cref="BenTestBase.LoginAsync"/> is the only thing that touches them. So the way to look at
/// a signed-in page is to have the harness open it.</para>
///
/// <para><b>Both themes, every time.</b> Every fault this redesign has produced so far has been
/// light-only — a kicker invisible on a photograph, a card gap behind an invisible dark border, a
/// wordmark white on a white bar. A shot of one theme is half an answer.</para>
///
/// <para>Explicit, and it writes files: it is an instrument, not a test. Run it with
/// <c>--filter TestCategory=SignalShots</c>.</para>
/// </remarks>
[TestFixture]
[Category("SignalShots")]
[Explicit("Writes screenshots; run deliberately while working on the redesign.")]
[NonParallelizable]
public class SignalShots : BenTestBase
{
    private static readonly (string Slug, string Url)[] Screens =
    [
        ("home",            "/"),
        ("my-cases",        "/my-cases"),
        ("my-requests",     "/my-requests"),
        ("organizations",   "/organizations"),
        ("notifications",   "/notifications"),
        ("my-equipment",    "/my-equipment"),
        ("store",           "/store"),
        ("pricing",         "/pricing"),
        ("my-videos",       "/my-videos"),
        ("styleguide",      "/styleguide"),
    ];

    /// <summary>Administration is SuperAdmin-only, so it is photographed as the SuperAdmin. The
    /// second is inside a group of the menu, so the rail shows that group with the way back.</summary>
    private static readonly (string Slug, string Url)[] AdminScreens =
    [
        ("admin-users",     "/admin/users"),
        ("admin-dashboard", "/admin/dashboard"),
        ("admin-system-drilled", "/admin/email-templates"),
    ];

    /// <summary>Phase 2's public pages, as a visitor sees them (one screen each).</summary>
    private static readonly (string Slug, string Url)[] PublicScreens =
    [
        ("public-find",         "/find"),
        ("public-events",       "/events"),
        ("public-group",        "/o/paranormal365"),
        ("public-group-cases",  "/o/paranormal365/cases"),
        ("public-event",        "/o/paranormal365/events/2026-10-15-bell-witch-cave-public-night-walk"),
        ("public-feed",         "/feed"),
        ("public-publications", "/publications"),
        ("public-pricing",      "/pricing"),
        ("public-help",         "/help"),
        ("public-catalog",      "/equipment-catalog"),
        ("public-place",        "/places/40000001-0000-0000-0000-000000000001"),
        ("public-store",        "/store"),
    ];

    [Test]
    public async Task Photograph_the_signed_in_site_in_both_themes()
    {
        var root = Path.Combine(RepoRoot(), "docs", "design", "preview", "signal-live");
        Directory.CreateDirectory(root);

        await ShootVisitorAsync(root);                                       // nobody signed in
        await ShootPublicAsync(root);
        await ShootAsync(root, UserEmail, UserPassword, Screens);            // Sarah
        await ShootGroupRailAsync(root);                                     // Sarah, inside her group
        await ShootAsync(root, SuperAdminEmail, SuperAdminPassword, AdminScreens);
    }

    /// <summary>The visitor's Home, signed out — the page that has to sell the site. Full page.</summary>
    private async Task ShootVisitorAsync(string root)
    {
        await Context.ClearCookiesAsync();
        foreach (var theme in new[] { "dark", "light" })
        {
            await Page.GotoAsync(BaseUrl);
            await Page.EvaluateAsync(
                "t => { try { localStorage.setItem('layoutSettings', JSON.stringify({ theme: t })); " +
                "localStorage.setItem('ben-theme', t); } catch (e) { } }", theme);
            await Page.GotoAsync(BaseUrl);
            await Expect(Page.Locator("[data-testid=home-pitch]")).ToBeVisibleAsync(new() { Timeout = 20_000 });
            await Page.WaitForTimeoutAsync(1500);
            // lazy screens below the fold only load once scrolled to
            await Page.EvaluateAsync("async () => { for (let y = 0; y < document.body.scrollHeight; y += 600) { window.scrollTo(0, y); await new Promise(r => setTimeout(r, 120)); } window.scrollTo(0, 0); }");
            await Page.WaitForTimeoutAsync(800);
            // The shell is pinned to the window and scrolls INSIDE .app-body (app.css, "App-shell
            // height"), so a full-page screenshot sees one screen. Unpinned for the photograph
            // only — this style never leaves the capture's own page.
            await Page.AddStyleTagAsync(new() { Content =
                ".app-wrap{height:auto!important} .app-body,.app-content{overflow:visible!important;height:auto!important}" });
            await Page.WaitForTimeoutAsync(300);
            await Page.ScreenshotAsync(new() { Path = Path.Combine(root, $"home-visitor-{theme}.png"), FullPage = true });
        }
    }

    /// <summary>The public pages, signed out, one screen each.</summary>
    private async Task ShootPublicAsync(string root)
    {
        await Context.ClearCookiesAsync();
        foreach (var theme in new[] { "dark", "light" })
        {
            await Page.GotoAsync(BaseUrl);
            await Page.EvaluateAsync(
                "t => { try { localStorage.setItem('layoutSettings', JSON.stringify({ theme: t })); " +
                "localStorage.setItem('ben-theme', t); } catch (e) { } }", theme);
            foreach (var (slug, url) in PublicScreens)
            {
                await Page.GotoAsync($"{BaseUrl}{url}");
                await Expect(Page.Locator(".app-topnav")).ToBeVisibleAsync(new() { Timeout = 20_000 });
                await Page.WaitForTimeoutAsync(1500);
                await Page.ScreenshotAsync(new() { Path = Path.Combine(root, $"{slug}-{theme}.png") });
            }
        }
    }

    /// <summary>
    /// Sarah's first group, from the inside: the rail is the group's menu (Ben, 2026-10-01: "the
    /// menu becomes the menu for that page with a back button at the top"). Run straight after
    /// <see cref="ShootAsync"/>, so she is still signed in.
    /// </summary>
    private async Task ShootGroupRailAsync(string root)
    {
        await Page.GotoAsync($"{BaseUrl}/organizations");
        var href = await Page.Locator(".content-wrapper a[href^='/organizations/']").EvaluateAllAsync<string[]>(
            "els => els.map(e => e.getAttribute('href')).filter(h => /^\\/organizations\\/[0-9a-f-]{36}$/.test(h))");
        if (href.Length == 0) { TestContext.Out.WriteLine("no group to photograph"); return; }

        foreach (var theme in new[] { "dark", "light" })
        {
            await Page.EvaluateAsync(
                "t => { try { localStorage.setItem('layoutSettings', JSON.stringify({ theme: t })); " +
                "localStorage.setItem('ben-theme', t); } catch (e) { } }", theme);
            foreach (var (slug, tab) in new[] { ("group-rail", "details"), ("group-rail-members", "members") })
            {
                await Page.GotoAsync($"{BaseUrl}{href[0]}?tab={tab}");
                await Expect(Page.Locator("[data-testid=rail-context]")).ToBeVisibleAsync(new() { Timeout = 20_000 });
                await Page.WaitForTimeoutAsync(1500);
                await Page.ScreenshotAsync(new() { Path = Path.Combine(root, $"{slug}-{theme}.png") });
            }
        }
    }

    private async Task ShootAsync(string root, string email, string password, (string Slug, string Url)[] screens)
    {
        await Context.ClearCookiesAsync();
        await LoginAsync(email, password);

        foreach (var theme in new[] { "dark", "light" })
        {
            // The site reads its own stored choice first, so set that rather than the device's
            // colour scheme — this is the same key ben-boot.js writes.
            await Page.EvaluateAsync(
                "t => { try { localStorage.setItem('layoutSettings', JSON.stringify({ theme: t })); " +
                "localStorage.setItem('ben-theme', t); } catch (e) { } }", theme);

            foreach (var (slug, url) in screens)
            {
                await Page.GotoAsync($"{BaseUrl}{url}");
                await Expect(Page.Locator(".app-topnav")).ToBeVisibleAsync(new() { Timeout = 20_000 });
                await Page.WaitForTimeoutAsync(1200);       // let the circuit finish its first data pass

                await Page.ScreenshotAsync(new()
                {
                    Path = Path.Combine(root, $"{slug}-{theme}.png"),
                    FullPage = false,
                });
                TestContext.Out.WriteLine($"shot {slug}-{theme}");
            }
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? Directory.GetCurrentDirectory();
    }
}
