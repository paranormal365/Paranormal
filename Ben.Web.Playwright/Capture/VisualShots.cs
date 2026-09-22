using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Capture;

/// <summary>
/// Photographs the screens that need an id to reach, at phone and desktop width, for a person to look at.
/// </summary>
/// <remarks>
/// <para><b>Why beside <see cref="VisualAuditWalk"/> rather than inside it.</b> The audit walks
/// static routes and reads them with a script; a place's page, the guest-code sheet and
/// <c>/tonight/{code}</c> exist only once something has been founded, and what is wrong with them
/// is the kind of thing a script does not know to look for. So this founds what it needs, takes
/// the pictures, and leaves the looking to whoever reads the folder.</para>
///
/// <para>Opt-in with <c>BEN_VISUAL_SHOTS=1</c>. Writes PNGs to <c>BEN_VISUAL_SHOTS_OUT</c> or the
/// test's work directory, named <c>{screen}-{width}.png</c>.</para>
/// </remarks>
[TestFixture]
[Category("Capture")]
[NonParallelizable]
public sealed class VisualShots : BenTestBase
{
    private static readonly int[] Widths = [375, 1280];

    public override BrowserNewContextOptions ContextOptions() => new()
    {
        ViewportSize = new ViewportSize { Width = 1280, Height = 800 },
        ColorScheme = ColorScheme.Dark,
    };

    private string _out = "";

    [OneTimeSetUp]
    public void Gate()
    {
        if (Environment.GetEnvironmentVariable("BEN_VISUAL_SHOTS") != "1")
            Assert.Ignore("Set BEN_VISUAL_SHOTS=1 to photograph the id-bearing screens.");

        _out = Environment.GetEnvironmentVariable("BEN_VISUAL_SHOTS_OUT")
               ?? Path.Combine(TestContext.CurrentContext.WorkDirectory, "visual-shots");
        Directory.CreateDirectory(_out);
    }

    /// <summary>Dark before first paint, the same way the help captures do it.</summary>
    [SetUp]
    public async Task Dark()
        => await Context.AddInitScriptAsync("""
            try {
                localStorage.setItem('layoutSettings', JSON.stringify({ theme: 'dark' }));
                localStorage.setItem('ben-theme', 'dark');
            } catch (e) { }
            """);

    private async Task ShootAsync(string screen)
    {
        foreach (var width in Widths)
        {
            await Page.SetViewportSizeAsync(width, 812);
            await Page.WaitForTimeoutAsync(600);
            await Page.ScreenshotAsync(new()
            {
                Path = Path.Combine(_out, $"{screen}-{width}.png"),
                FullPage = true,
            });
        }
        await Page.SetViewportSizeAsync(1280, 800);
    }

    [Test]
    public async Task Photograph_the_screens_that_need_an_id()
    {
        // ── A public location with a description, a picture and voted evidence ──
        await LoginAsync(UserEmail, UserPassword);

        await Page.GotoAsync($"{BaseUrl}/places/new");
        await WaitForTheCircuitAsync();
        await WaitUntilLoadedAsync();
        await Page.Locator("#newplace-name").FillAsync("Cragfont");
        await Page.Locator("#newplace-street").FillAsync("200 Cragfont Road");
        await Page.Locator("#newplace-city").FillAsync("Castalian Springs");
        await Page.Locator("#newplace-state").FillAsync("TN");
        await ClickUntilUrlAsync(Page.Locator("#newplace-add"), @"/places/[0-9a-f\-]{36}");
        await WaitForTheCircuitAsync();
        await WaitUntilLoadedAsync();
        var placeAddress = new Uri(Page.Url).AbsolutePath;

        await ClickUntilAsync(Page.Locator("#place-about-edit"), Page.Locator("#place-about-text"));
        await Page.Locator("#place-about-text").FillAsync(
            "Built in 1802 by General James Winchester, on a bluff above the Cumberland.");
        await ClickUntilAsync(Page.Locator("#place-about-save"), Page.Locator("#place-about-edit"));

        foreach (var (caption, kind) in new[]
        {
            ("The upstairs corridor, about 11pm", "Evidence"),
            ("The frontage from the drive", "AboutThePlace"),
        })
        {
            await Page.Locator("#place-evidence-file").SetInputFilesAsync(
                Path.Combine(AppContext.BaseDirectory, "Fixtures", "room-photo-1.jpg"));
            await Page.Locator("#place-evidence-caption").FillAsync(caption);
            await Page.Locator("#place-evidence-kind").SelectOptionAsync(kind);
            await ClickUntilAsync(Page.Locator("#place-evidence-add"), Page.Locator("#place-evidence-says"));
        }

        await Page.ReloadAsync();
        await WaitForTheCircuitAsync();
        await WaitUntilLoadedAsync();
        await ShootAsync("place-signed-in");

        // ── The guest-code sheet, and the guest's end of it ──
        var orgId = await OrgIdBySlugAsync("paranormal365");
        await Page.GotoAsync($"{BaseUrl}/organizations/{orgId}?tab=investigations");
        await WaitForTheCircuitAsync();
        await WaitUntilLoadedAsync();
        await ShootAsync("investigations-tab");

        var codeButton = Main.GetByRole(AriaRole.Button, new() { Name = "Guest code" }).First;
        await ClickUntilUrlAsync(codeButton, @"/investigations/[0-9a-f\-]+/join-code");
        await WaitForTheCircuitAsync();
        await WaitUntilLoadedAsync();
        var make = Page.Locator("#join-code-make");
        if (await make.CountAsync() > 0)
            await ClickUntilAsync(make, Page.Locator("#join-code-typed"));
        var typed = (await Page.Locator("#join-code-typed").InnerTextAsync()).Trim();
        await ShootAsync("guest-code-sheet");

        // ── Everything a stranger sees ──
        await LogoutAsync();

        await Page.GotoAsync($"{BaseUrl}{placeAddress}");
        await WaitUntilLoadedAsync();
        await ShootAsync("place-visitor");

        await Page.GotoAsync($"{BaseUrl}/tonight");
        await WaitForTheCircuitAsync();
        await WaitUntilLoadedAsync();
        await ShootAsync("tonight-empty");

        await Page.Locator("#tonight-code").FillAsync(typed);
        await ClickUntilAsync(Page.Locator("#tonight-check"), Page.Locator("#tonight-invitation"));
        await ShootAsync("tonight-invited");

        await Page.GotoAsync($"{BaseUrl}/reset-password?email=someone%40example.com&code=abc&handover=1");
        await WaitUntilLoadedAsync();
        await ShootAsync("handover-reset");

        await Page.GotoAsync($"{BaseUrl}/");
        await WaitUntilLoadedAsync();
        await ShootAsync("home-visitor");

        TestContext.Out.WriteLine($"shots in {_out}");
    }
}
