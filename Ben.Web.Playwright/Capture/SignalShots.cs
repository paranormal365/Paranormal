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
        ("organizations",   "/organizations"),
        ("styleguide",      "/styleguide"),
    ];

    [Test]
    public async Task Photograph_the_signed_in_site_in_both_themes()
    {
        var root = Path.Combine(RepoRoot(), "docs", "design", "preview", "signal-live");
        Directory.CreateDirectory(root);

        await LoginAsync(UserEmail, UserPassword);          // Sarah — an ordinary administrator

        foreach (var theme in new[] { "dark", "light" })
        {
            // The site reads its own stored choice first, so set that rather than the device's
            // colour scheme — this is the same key ben-boot.js writes.
            await Page.EvaluateAsync(
                "t => { try { localStorage.setItem('layoutSettings', JSON.stringify({ theme: t })); " +
                "localStorage.setItem('ben-theme', t); } catch (e) { } }", theme);

            foreach (var (slug, url) in Screens)
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
