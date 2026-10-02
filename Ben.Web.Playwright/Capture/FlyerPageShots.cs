using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Capture;

/// <summary>
/// The six pictures the advertisements take from the hosted-events walk, taken without the walk.
/// </summary>
/// <remarks>
/// <para><b>Why a second way in (Ben, 2026-10-02: "capture the six shots another way").</b> The
/// flyers put the public tour, venue and event pages in a laptop and a phone, and an organizer
/// writing to their guests beside them. Those came from <see cref="HostedEventPersonaWalk"/>, which
/// is a whole story — bookings confirmed, letters sent through a local mail catcher, run against
/// a tour that one particular earlier setup created. Re-running it to restyle six pictures is a
/// lot of machinery and data to stand up again; so these are taken directly.</para>
///
/// <para><b>Same frame, same files.</b> 1440×900 at 2×, dark, and 390px for the phone — the walk's
/// own sizes — written to the walk's own filenames in <c>docs/media/hosted-events/walk/</c>, so
/// <c>docs/ads/build-ads.py</c> picks them up with no change.</para>
///
/// <para><b>Found, not assumed.</b> The tour is whichever the site lists first; the event and the
/// venue are the seeded ones every database has. Nothing is sent: the letter is written and
/// photographed, never posted.</para>
///
/// <para>Explicit: it writes files. <c>scripts/run-e2e.sh --filter "TestCategory=FlyerShots"</c>.</para>
/// </remarks>
[TestFixture]
[Category("FlyerShots")]
[Explicit("Writes the flyers' screenshots; run deliberately.")]
[NonParallelizable]
public sealed class FlyerPageShots : BenTestBase
{
    private const string RoomsEventId = "40000002-0000-0000-0000-000000000002";
    private const string VenuePlaceId = "40000002-0000-0000-0000-000000000001";
    private const int Wide = 1440;
    private const int Tall = 900;

    public override BrowserNewContextOptions ContextOptions() => new()
    {
        ViewportSize = new ViewportSize { Width = Wide, Height = Tall },
        DeviceScaleFactor = 2,
        ColorScheme = ColorScheme.Dark,
    };

    private const string DarkFirst = """
        try {
          localStorage.setItem('layoutSettings', JSON.stringify({ theme: 'dark' }));
          localStorage.setItem('ben-theme', 'dark');
        } catch (e) { }
        """;

    [SetUp]
    public async Task Dark() => await Context.AddInitScriptAsync(DarkFirst);

    [Test]
    public async Task Photograph_the_public_pages_the_flyers_show()
    {
        await LogoutAsync();

        // The first tour the site lists — whichever group runs it.
        var tours = await Page.APIRequest.GetAsync($"{ApiUrl}/api/public/tours");
        Assert.That(tours.Ok, Is.True, await tours.TextAsync());
        var list = (await tours.JsonAsync())!.Value;
        Assert.That(list.GetArrayLength(), Is.GreaterThan(0), "no public tour to photograph");
        var tour = list[0];
        var tourRoute = $"/o/{tour.GetProperty("organizationUrlName").GetString()}/tours/{tour.GetProperty("urlName").GetString()}";

        await BothWidthsAsync("41-public-tour", "42-public-tour-phone", tourRoute, "[data-testid=tour-hero]");
        await BothWidthsAsync("43-public-venue", "44-public-venue-phone", $"/o/paranormal365/venues/{VenuePlaceId}", "#venue-title");
        await BothWidthsAsync("45-public-event", null, "/o/paranormal365/events/thomas-house-seance-weekend", "[data-testid=event-hero]");
    }

    [Test]
    public async Task Photograph_an_organizer_writing_to_their_guests()
    {
        var orgId = await OrgIdBySlugAsync("paranormal365");
        await LoginAsync(UserEmail, UserPassword);

        await GoAsync($"/organizations/{orgId}/events/{RoomsEventId}/bookings");
        await ClickUntilAsync(Page.Locator("#letter-start"), Page.Locator("#letter-subject"));
        await Page.Locator("#letter-subject").FillAsync("Parking for Friday night");
        await Page.Locator("#letter-body").FillAsync(
            "The hotel parking lot is closed on Friday for resurfacing.\nPark behind the Methodist church on Main Street — it's a two-minute walk, and we'll have a lantern at the corner.");

        // Written and photographed, never sent.
        var board = Page.Locator("#board-letters").First;
        await board.ScrollIntoViewIfNeededAsync();
        await SettledAsync();
        var box = await board.BoundingBoxAsync() ?? throw new InvalidOperationException("#board-letters has no box");
        const int pad = 16;
        var x = Math.Max(0, box.X - pad);
        var y = Math.Max(0, box.Y - pad);
        await Page.ScreenshotAsync(new()
        {
            Path = Path.Combine(Folder(), "10-organizer-writing-to-guests.png"),
            Clip = new() { X = x, Y = y, Width = Math.Min(Wide - x, box.Width + 2 * pad), Height = Math.Min(Tall - y, box.Height + 2 * pad) },
        });
    }

    // ── photographing ────────────────────────────────────────────────────────

    private async Task BothWidthsAsync(string desktopName, string? phoneName, string route, string subject)
    {
        await GoAsync(route);
        await Expect(Page.Locator(subject).First).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await SettledAsync();
        await Page.ScreenshotAsync(new() { Path = Path.Combine(Folder(), desktopName + ".png") });

        if (phoneName is null) return;
        await Page.SetViewportSizeAsync(390, 844);
        try
        {
            await GoAsync(route);
            await Expect(Page.Locator(subject).First).ToBeVisibleAsync(new() { Timeout = 30_000 });
            await SettledAsync();
            await Page.ScreenshotAsync(new() { Path = Path.Combine(Folder(), phoneName + ".png") });
        }
        finally { await Page.SetViewportSizeAsync(Wide, Tall); }
    }

    private async Task GoAsync(string route)
    {
        await Page.GotoAsync($"{BaseUrl}{route}");
        await WaitForTheCircuitAsync();
        await SettledAsync();
    }

    /// <summary>The network quiet, the loading markers gone, every image decoded and the fonts in.</summary>
    private async Task SettledAsync()
    {
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await WaitUntilLoadedAsync();
        await Page.EvaluateAsync("""
            async () => {
              await document.fonts.ready;
              await Promise.all([...document.images].map(img => img.complete ? null : img.decode().catch(() => null)));
            }
            """);
        await Page.WaitForTimeoutAsync(800);   // the hero's photograph eases in
    }

    private static string Folder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx"))) dir = dir.Parent;
        Assert.That(dir, Is.Not.Null, "Could not find the repository root.");
        var folder = Path.Combine(dir!.FullName, "docs", "media", "hosted-events", "walk");
        Directory.CreateDirectory(folder);
        return folder;
    }
}
