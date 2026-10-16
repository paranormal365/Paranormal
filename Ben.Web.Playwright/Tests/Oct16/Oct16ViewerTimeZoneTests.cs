using System.Text.Json;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests.Oct16;

/// <summary>
/// A visit with no case, scheduled from Group → Investigations, is saved on the clock of the
/// person typing it and reads back exactly as typed (fix/viewer-time-zone-saves, 10/16/2026).
/// </summary>
/// <remarks>
/// <para><b>The bug.</b> <c>NewInvestigationWindow</c> saved <c>.ToUniversalTime()</c>. On Blazor
/// Server that runs on the server and converts through the SERVER's zone. Production's server is a
/// fixed UTC-6, so 6:00 AM typed in Central (UTC-5 in October) was stored an hour late and read back
/// as 7:00 AM.</para>
///
/// <para><b>What this fixture can catch.</b> The browser here runs in a zone chosen to differ from
/// this machine's own zone and from the house fallback (Chicago) — see <see cref="PickZone"/>. The
/// hosts run on this machine under run-e2e.sh, so this machine's zone IS the server's zone, and the
/// two clocks differ by at least an hour. A save that went through the server's clock lands at
/// least an hour off, and the test fails on both the screen and the stored UTC. A viewer zone that
/// was never detected falls back to Chicago, which is excluded too, so that fails as well.</para>
///
/// <para><b>What it cannot catch.</b> Pointed at a remote site (BEN_BASE_URL elsewhere), this
/// machine's zone is not necessarily the server's; the test then proves the round trip in the
/// viewer's zone but not that the server's zone stayed out of it. It also cannot see the old,
/// already-saved rows the branch notes say stay an hour late — nothing on any screen can edit a
/// case-less visit (see the report).</para>
///
/// <para>Sarah (an administrator of Paranormal365) schedules. Her profile's own zone is set for the
/// test and put back afterwards, because a saved zone outranks the browser's.</para>
/// </remarks>
[TestFixture]
[Category("Oct16")]
[Category("TimeZones")]
[NonParallelizable]
public class Oct16ViewerTimeZoneTests : Oct16TestBase
{
    /// <summary>US zones whose letters the site knows (Zones.Letters), in order of preference.</summary>
    private static readonly string[] Candidates =
        ["America/Los_Angeles", "America/New_York", "America/Denver", "America/Anchorage"];

    /// <summary>The browser's zone for every test here: not this machine's, not Chicago's.</summary>
    private static readonly string BrowserZone = PickZone();

    /// <summary>A profile zone that differs from the browser's too, for the "saved zone wins" test.</summary>
    private static readonly string ProfileZone = PickZone(BrowserZone);

    /// <summary>
    /// The first candidate whose offset, three days from now, differs from this machine's, from
    /// Chicago's, and from any zone already chosen.
    /// </summary>
    private static string PickZone(params string[] alsoUnlike)
    {
        var at = DateTime.UtcNow.AddDays(3);
        var avoid = new List<TimeSpan>
        {
            TimeZoneInfo.Local.GetUtcOffset(at),
            TimeZoneInfo.FindSystemTimeZoneById("America/Chicago").GetUtcOffset(at),
        };
        avoid.AddRange(alsoUnlike.Select(z => TimeZoneInfo.FindSystemTimeZoneById(z).GetUtcOffset(at)));

        return Candidates.First(z => !avoid.Contains(TimeZoneInfo.FindSystemTimeZoneById(z).GetUtcOffset(at)));
    }

    /// <summary>The site's letters for a zone at an instant — the same table as Zones.Letters.</summary>
    private static string Letters(string zone, DateTime utc)
    {
        var daylight = TimeZoneInfo.FindSystemTimeZoneById(zone)
            .IsDaylightSavingTime(DateTime.SpecifyKind(utc, DateTimeKind.Utc));
        return zone switch
        {
            "America/Los_Angeles" => daylight ? "PDT" : "PST",
            "America/New_York"    => daylight ? "EDT" : "EST",
            "America/Denver"      => daylight ? "MDT" : "MST",
            "America/Anchorage"   => daylight ? "AKDT" : "AKST",
            "America/Chicago"     => daylight ? "CDT" : "CST",
            _ => throw new ArgumentOutOfRangeException(nameof(zone), zone, "no letters known"),
        };
    }

    public override BrowserNewContextOptions ContextOptions()
    {
        var options = base.ContextOptions() ?? new BrowserNewContextOptions();
        options.TimezoneId = BrowserZone;
        return options;
    }

    private IAPIRequestContext _sarah = null!;
    private string _orgId = "";
    private string? _sarahsZoneBefore;
    private bool _zoneChanged;

    [SetUp]
    public async Task SignInTheApi()
    {
        _orgId = await OrgIdBySlugAsync(GroupSlug);
        _sarah = await ApiAsAsync(UserEmail, UserPassword);
        var profile = await OkJsonAsync(await _sarah.GetAsync("/api/me/profile"), "reading Sarah's profile");
        _sarahsZoneBefore = profile.TryGetProperty("timeZoneId", out var z) && z.ValueKind == JsonValueKind.String
            ? z.GetString() : null;

        // "My time" on the list, so the row reads on the viewer's clock — remembered in the browser
        // (TimeView), so it is per context and needs no putting back.
        await Context.AddInitScriptAsync("try { localStorage.setItem('ben-time-view', 'mine'); } catch (e) { }");
    }

    [TearDown]
    public async Task PutSarahsZoneBack()
    {
        if (!_zoneChanged) return;
        await _sarah.PutAsync("/api/me/profile", new() { DataObject = new { timeZoneId = _sarahsZoneBefore ?? "" } });
        _zoneChanged = false;
    }

    private async Task SetSarahsZoneAsync(string zoneOrEmpty)
    {
        _zoneChanged = true;
        await OkJsonAsync(
            await _sarah.PutAsync("/api/me/profile", new() { DataObject = new { timeZoneId = zoneOrEmpty } }),
            "setting Sarah's profile zone");
    }

    // ── The round trip ──────────────────────────────────────────────────────

    /// <summary>
    /// No zone saved on the profile: the browser's zone is the viewer's. 7:00 PM to 7:00 AM typed is
    /// 7:00 PM to 7:00 AM read, and stored as those instants on that clock.
    /// </summary>
    [Test]
    public async Task An_overnight_visit_typed_on_the_browsers_clock_reads_back_exactly_as_typed()
    {
        await SetSarahsZoneAsync("");
        await ScheduleAndCheckAsync(BrowserZone);
    }

    /// <summary>
    /// A zone saved on the profile outranks the browser's. The browser here is on another clock
    /// again, so a save that used the browser's zone would land hours off.
    /// </summary>
    [Test]
    public async Task A_zone_saved_on_the_profile_is_the_clock_the_times_are_typed_on()
    {
        await SetSarahsZoneAsync(ProfileZone);
        await ScheduleAndCheckAsync(ProfileZone);
    }

    private async Task ScheduleAndCheckAsync(string viewerZone)
    {
        var title = $"Oct16 clock {Guid.NewGuid():N}"[..20];
        var zone = TimeZoneInfo.FindSystemTimeZoneById(viewerZone);
        var day = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone).Date.AddDays(3);
        var startUtc = UtcFrom(day.AddHours(19), viewerZone);
        var endUtc = UtcFrom(day.AddDays(1).AddHours(7), viewerZone);
        var letters = Letters(viewerZone, startUtc);

        var dialog = await OpenTheScheduleWindowAsync();

        // The line the branch added, naming the clock the boxes are typed on. It takes the letters
        // of today, not of the visit (see the report), so today's are what it is held to.
        await Expect(Page.GetByTestId("newinvestigationwindow-zone"))
            .ToHaveTextAsync($"Times are in your time zone ({Letters(viewerZone, DateTime.UtcNow)}).", new() { Timeout = 15_000 });

        await dialog.Locator("#newinvestigationwindow-title-48f3").FillAsync(title);
        await TypeIntoAsync(dialog.Locator("#newinvestigationwindow-when-60b5"), $"{day:MM/dd/yyyy} 07:00 PM");
        await Expect(dialog.Locator("#newinvestigationwindow-when-60b5")).ToHaveValueAsync($"{day:MM/dd/yyyy} 07:00 PM");
        await dialog.Locator("#newinvestigationwindow-ends-7c31").FillAsync("07:00");

        // An existing landmark, chosen from "this place may already be on file", so no run makes a
        // new place. The suggestion is the duplicate matcher, which wants the whole name.
        await dialog.Locator("#newinvestigationwindow-name-a-landmark-or-27ff").FillAsync("Bell Witch Cave");
        var useIt = dialog.Locator(".alert-info .d-flex", new() { HasTextString = "Bell Witch Cave" })
                          .GetByRole(AriaRole.Button, new() { Name = "Use this place" }).First;
        await Expect(useIt).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await useIt.ClickAsync();
        await Expect(dialog.Locator(".alert-success")).ToContainTextAsync("Bell Witch Cave");

        // Kept to the group where the plan allows it, so these test visits do not pile up on the
        // cave's public page. A plan that shares every landmark visit offers no narrower choice.
        var scope = dialog.Locator("#newinvestigationwindow-who-can-see-the-fea9");
        if (await scope.Locator("option[value='GroupOnly']").CountAsync() > 0)
            await scope.SelectOptionAsync(new[] { new SelectOptionValue { Value = "GroupOnly" } });

        await dialog.GetByRole(AriaRole.Button, new() { Name = "Schedule", Exact = true }).ClickAsync();
        try
        {
            await Expect(Page.Locator(".modal.show")).ToHaveCountAsync(0, new() { Timeout = 20_000 });
        }
        catch (PlaywrightException)
        {
            var refusal = dialog.Locator(".alert-danger");
            Assert.Fail("The visit was not scheduled: " + (await refusal.CountAsync() > 0
                ? await refusal.First.InnerTextAsync() : "the window stayed open and said nothing."));
        }

        // ── what is stored ──────────────────────────────────────────────────
        var rows = await OkJsonAsync(await _sarah.GetAsync($"/api/organizations/{_orgId}/investigations"), "reading the list");
        var row = rows.EnumerateArray().FirstOrDefault(r => r.GetProperty("title").GetString() == title);
        Assert.That(row.ValueKind, Is.EqualTo(JsonValueKind.Object), $"No visit called \"{title}\" was saved.");

        var storedStart = AsUtc(row.GetProperty("scheduledDateTime"));
        var storedEnd = AsUtc(row.GetProperty("endDateTime"));
        Assert.Multiple(() =>
        {
            Assert.That(storedStart, Is.EqualTo(startUtc),
                $"7:00 PM {letters} on {day:MM/dd} should be stored as {startUtc:u}. It was stored as {storedStart:u}, "
              + $"{(storedStart - startUtc).TotalHours:+0.#;-0.#} hours off. This machine's zone is {TimeZoneInfo.Local.Id}.");
            Assert.That(storedEnd, Is.EqualTo(endUtc),
                $"7:00 AM {letters} the next morning should be stored as {endUtc:u}. It was {storedEnd:u}.");
        });

        // ── what is read ────────────────────────────────────────────────────
        var shown = Page.Locator("tr", new() { HasTextString = title }).GetByTestId("investigation-when");
        await Expect(shown).ToBeVisibleAsync(new() { Timeout = 20_000 });
        var mine = Page.GetByTestId("time-zone-mine");
        if (await mine.CountAsync() > 0 && await mine.GetAttributeAsync("aria-pressed") != "true")
            await mine.ClickAsync();

        await Expect(shown).ToHaveTextAsync(
            $"{day:MM/dd/yyyy} 7:00 PM – 7:00 AM next day {letters}", new() { Timeout = 15_000 });
    }

    // ── Looking at it ───────────────────────────────────────────────────────

    /// <summary>The zone line, in the window, on a phone and a laptop.</summary>
    [Test]
    [TestCase("dark")]
    [TestCase("light")]
    public async Task The_zone_line_fits_under_the_start_box(string theme)
    {
        await UseThemeAsync(theme);
        var problems = new List<string>();

        foreach (var (width, height) in Widths)
        {
            await Page.SetViewportSizeAsync(width, height);
            var dialog = await OpenTheScheduleWindowAsync();
            var line = Page.GetByTestId("newinvestigationwindow-zone");

            problems.AddRange(await LookAtAsync(line, "zone-line", theme,
                dialog.Locator("#newinvestigationwindow-when-60b5"),
                dialog.Locator("#newinvestigationwindow-ends-7c31"),
                dialog.Locator("label[for='newinvestigationwindow-ends-7c31']")));

            await dialog.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
            await Expect(Page.Locator(".modal.show")).ToHaveCountAsync(0, new() { Timeout = 10_000 });
        }

        NothingWrong(problems);
    }

    // ── plumbing ────────────────────────────────────────────────────────────

    private async Task<ILocator> OpenTheScheduleWindowAsync()
    {
        if (!Page.Url.Contains("/organizations/"))
            await LoginAsync(UserEmail, UserPassword);

        await Page.GotoAsync($"{BaseUrl}/organizations/{_orgId}?tab=investigations");
        await WaitForTheCircuitAsync();
        await WaitUntilLoadedAsync();
        await SkipAnyTourAsync();

        var open = Main.GetByRole(AriaRole.Button, new() { Name = "Schedule an investigation" });
        var dialog = Page.Locator(".modal.show").First;
        await ClickUntilAsync(open, dialog.Locator("#newinvestigationwindow-title-48f3"));
        return dialog;
    }

    /// <summary>Real keystrokes into a BenDateField, then Tab, which is when it reads the text.</summary>
    private async Task TypeIntoAsync(ILocator box, string text)
    {
        await box.ClickAsync();
        await box.PressAsync("ControlOrMeta+a");
        await box.PressAsync("Backspace");
        await box.PressSequentiallyAsync(text, new() { Delay = 15 });
        await box.PressAsync("Tab");
    }
}
