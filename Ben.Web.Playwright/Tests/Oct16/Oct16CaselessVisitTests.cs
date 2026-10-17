using System.Text.Json;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests.Oct16;

/// <summary>
/// A visit with no case can be changed, canceled and deleted from Group → Investigations
/// (fix/oct16-gaps, gaps 1, 2, 10 and 15, 10/16/2026).
/// </summary>
/// <remarks>
/// <para><b>The gap.</b> Before the branch a case-less visit had no Edit at all, so the visits saved
/// an hour late before that day's time-zone fix stayed an hour late. Every test here seeds that
/// mistake through the API (an 8:00 PM visit that should have been 7:00 PM) or a visit to act on,
/// and deletes it afterwards — the API can now.</para>
///
/// <para><b>Seats.</b> Sarah administers Paranormal365 and manages by role; the seeded Owner is the
/// SuperAdmin, who passes every check by being SuperAdmin and so would prove nothing. James is a
/// plain Member, who must not be offered Edit.</para>
///
/// <para><b>Clocks.</b> The browser is put on Central time and so is Sarah's profile (put back
/// afterwards), so "7:00 PM" means one thing on the screen, in the stored UTC and on the calendar.
/// The visits are today, so the calendar opens on their week.</para>
/// </remarks>
[TestFixture]
[Category("Oct16")]
[Category("CaselessVisits")]
[NonParallelizable]
public class Oct16CaselessVisitTests : Oct16TestBase
{
    private const string Central = "America/Chicago";

    public override BrowserNewContextOptions ContextOptions()
    {
        var options = base.ContextOptions() ?? new BrowserNewContextOptions();
        options.TimezoneId = Central;
        return options;
    }

    private IAPIRequestContext _sarah = null!;
    private string _orgId = "";
    private string? _sarahsZoneBefore;
    private bool _zoneChanged;
    private readonly List<string> _visits = [];

    [SetUp]
    public async Task SarahOnCentralTime()
    {
        _signedInAs = null;   // each test has a fresh browser context
        _orgId = await OrgIdBySlugAsync(GroupSlug);
        _sarah = await ApiAsAsync(UserEmail, UserPassword);

        var profile = await OkJsonAsync(await _sarah.GetAsync("/api/me/profile"), "reading Sarah's profile");
        _sarahsZoneBefore = profile.TryGetProperty("timeZoneId", out var z) && z.ValueKind == JsonValueKind.String
            ? z.GetString() : null;
        await OkJsonAsync(await _sarah.PutAsync("/api/me/profile", new() { DataObject = new { timeZoneId = Central } }),
            "putting Sarah on Central time");
        _zoneChanged = true;

        // The list on the reader's clock, so the row and the box read the same.
        await Context.AddInitScriptAsync("try { localStorage.setItem('ben-time-view', 'mine'); } catch (e) { }");
    }

    [TearDown]
    public async Task DeleteTheVisitsAndPutSarahsZoneBack()
    {
        foreach (var id in _visits)
        {
            try { await DeleteVisitAsync(_sarah, _orgId, id); } catch { /* reported by the test that made it */ }
        }
        _visits.Clear();
        if (!_zoneChanged) return;
        await _sarah.PutAsync("/api/me/profile", new() { DataObject = new { timeZoneId = _sarahsZoneBefore ?? "" } });
        _zoneChanged = false;
    }

    /// <summary>Today on Central time, at the hour given.</summary>
    private static DateTime TodayAt(int hour)
        => WallClockIn(DateTime.UtcNow, Central).Date.AddHours(hour);

    private async Task<(string Id, string Title)> AVisitAsync(string what, int startHour = 20, int endHour = 22)
    {
        var title = $"Oct16 {what} {Guid.NewGuid():N}"[..(7 + what.Length + 8)];
        var id = await CaselessVisitAsync(_sarah, _orgId, title,
            UtcFrom(TodayAt(startHour), Central), UtcFrom(TodayAt(endHour), Central));
        _visits.Add(id);
        return (id, title);
    }

    private async Task<JsonElement> VisitAsync(string id)
        => await OkJsonAsync(await _sarah.GetAsync($"/api/organizations/{_orgId}/investigations/{id}"), "reading the visit");

    private async Task<JsonElement> CalendarEntryOfAsync(string visitId)
    {
        var visit = await VisitAsync(visitId);
        Assert.That(visit.TryGetProperty("orgCalendarEventId", out var cal) && cal.ValueKind == JsonValueKind.String, Is.True,
            "The visit has no calendar entry, so the calendar half cannot be checked.");
        return await OkJsonAsync(await _sarah.GetAsync($"/api/organizations/{_orgId}/calendar/{cal.GetString()}"),
            "reading the visit's calendar entry");
    }

    private ILocator Row(string id) => Page.Locator($"#inv-row-{id}");
    private ILocator Dialog => Page.Locator(".modal.show").First;
    private ILocator Footer => Dialog.Locator(".dialog-footer-actions");

    /// <summary>Who the browser is signed in as, so a loop over screens signs in once.</summary>
    private string? _signedInAs;

    private async Task OpenTheListAsync(string email, string password)
    {
        if (_signedInAs != email)
        {
            await LoginAsync(email, password);
            _signedInAs = email;
        }
        await Page.GotoAsync($"{BaseUrl}/organizations/{_orgId}?tab=investigations");
        await WaitForTheCircuitAsync();
        await WaitUntilLoadedAsync();
        await SkipAnyTourAsync();
    }

    /// <summary>The list, then Edit on one row, until the window has loaded the visit.</summary>
    private async Task OpenTheEditorAsync(string id, string title)
    {
        await OpenTheListAsync(UserEmail, UserPassword);
        await Expect(Row(id)).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await ClickUntilAsync(Page.Locator($"#inv-edit-{id}"), Dialog.Locator("#newinvestigationwindow-title-48f3"));
        await Expect(Dialog.Locator("#newinvestigationwindow-title-48f3")).ToHaveValueAsync(title, new() { Timeout = 15_000 });
        await Expect(Dialog.Locator("#newinvestigationwindow-save")).ToHaveTextAsync("Save changes");
        await Expect(Dialog.Locator("#newinvestigationwindow-save")).ToBeEnabledAsync(new() { Timeout = 15_000 });
    }

    private async Task WindowClosesAsync(string doing)
    {
        try
        {
            await Expect(Page.Locator(".modal.show")).ToHaveCountAsync(0, new() { Timeout = 20_000 });
        }
        catch (PlaywrightException)
        {
            var refusal = Page.Locator("#newinvestigationwindow-error");
            Assert.Fail($"{doing} did not finish: " + (await refusal.CountAsync() > 0
                ? await refusal.InnerTextAsync() : "the window stayed open and said nothing."));
        }
    }

    // ── Changing it ─────────────────────────────────────────────────────────

    /// <summary>
    /// Saved an hour late (8:00 PM), corrected to 7:00 PM with Edit. The row reads 7:00 PM, the
    /// stored instant is 7:00 PM Central, and the calendar entry moved with it.
    /// </summary>
    [Test]
    public async Task An_administrator_corrects_a_visit_saved_an_hour_late_and_the_calendar_follows()
    {
        var (id, title) = await AVisitAsync("late");
        var day = TodayAt(0);
        var corrected = UtcFrom(TodayAt(19), Central);

        await OpenTheEditorAsync(id, title);
        await Expect(Dialog.Locator("#newinvestigationwindow-when-60b5")).ToHaveValueAsync(BoxDateTime(TodayAt(20)));

        await TypeIntoDateFieldAsync(Dialog.Locator("#newinvestigationwindow-when-60b5"), BoxDateTime(TodayAt(19)));
        await Expect(Dialog.Locator("#newinvestigationwindow-when-60b5")).ToHaveValueAsync(BoxDateTime(TodayAt(19)));
        await Dialog.Locator("#newinvestigationwindow-ends-7c31").FillAsync("21:00");
        await Dialog.Locator("#newinvestigationwindow-save").ClickAsync();
        await WindowClosesAsync("Save changes");

        // The row.
        var when = Row(id).GetByTestId("investigation-when");
        await Expect(when).ToContainTextAsync($"{BoxDate(day)} 7:00 PM", new() { Timeout = 20_000 });
        await Expect(when).Not.ToContainTextAsync("8:00 PM");

        // What is stored.
        var visit = await VisitAsync(id);
        Assert.That(AsUtc(visit.GetProperty("scheduledDateTime")), Is.EqualTo(corrected),
            "The row says 7:00 PM and the stored start is not 7:00 PM Central.");
        Assert.That(AsUtc(visit.GetProperty("endDateTime")), Is.EqualTo(UtcFrom(TodayAt(21), Central)));

        // The calendar entry, stored and on the calendar itself.
        var entry = await CalendarEntryOfAsync(id);
        Assert.That(AsUtc(entry.GetProperty("startDateTime")), Is.EqualTo(corrected),
            "The visit moved and its calendar entry stayed at the old time.");
        var zone = entry.TryGetProperty("timeZoneId", out var tz) && tz.ValueKind == JsonValueKind.String ? tz.GetString()! : Central;

        var editor = await OpenCalendarEntryAsync(_orgId, $"Investigation: {title}");
        var start = editor.Locator("div.col-md-6", new()
        {
            Has = Page.Locator("label", new() { HasTextRegex = new System.Text.RegularExpressions.Regex(@"^\s*Start\s*$") }),
        }).Locator("input").First;
        await Expect(start).ToHaveValueAsync(BoxDateTime(WallClockIn(corrected, zone)), new() { Timeout = 10_000 });
        await editor.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
    }

    // ── Canceling it ────────────────────────────────────────────────────────

    [Test]
    public async Task Canceling_a_visit_marks_it_canceled_and_says_so_on_the_calendar()
    {
        var (id, title) = await AVisitAsync("cancel");

        await OpenTheEditorAsync(id, title);
        await Dialog.Locator("#newinvestigationwindow-cancel-visit").ClickAsync();
        var ask = Dialog.Locator("#newinvestigationwindow-confirm");
        await Expect(ask).ToContainTextAsync("Cancel this visit? It stays on record as canceled");
        await Expect(Dialog.Locator("#newinvestigationwindow-confirm-yes")).ToHaveTextAsync("Yes, cancel it");
        await Dialog.Locator("#newinvestigationwindow-confirm-yes").ClickAsync();
        await WindowClosesAsync("Cancel this visit");

        await Expect(Row(id).Locator(".inv-title .badge")).ToHaveTextAsync("Canceled", new() { Timeout = 20_000 });
        Assert.That(IsEnum((await VisitAsync(id)).GetProperty("status"), 3, "Cancelled"), Is.True,
            "The badge says Canceled and the server does not.");

        var entry = await CalendarEntryOfAsync(id);
        Assert.That(entry.GetProperty("title").GetString(), Does.StartWith("Canceled: Investigation:"),
            "The visit is canceled and its calendar entry does not say so.");

        await Page.GotoAsync($"{BaseUrl}/organizations/{_orgId}/calendar");
        await WaitForTheCircuitAsync();
        await SkipAnyTourAsync();
        await Expect(Page.Locator(".k-event", new() { HasTextString = $"Canceled: Investigation: {title}" }).First)
            .ToBeVisibleAsync(new() { Timeout = 30_000 });
    }

    // ── Deleting it ─────────────────────────────────────────────────────────

    /// <summary>
    /// Somebody was there, so deleting would lose their attendance. The server's sentence shows in
    /// the window's error line and the visit stays.
    /// </summary>
    [Test]
    public async Task Deleting_a_visit_somebody_attended_is_refused_in_words_and_the_row_stays()
    {
        var (id, title) = await AVisitAsync("attended");
        var jamesId = await MyUserIdAsync(await ApiAsAsync(MemberEmail, MemberPassword));
        var roster = await OkJsonAsync(await _sarah.PostAsync($"/api/organizations/{_orgId}/investigations/{id}/attendees",
            new() { DataObject = new { appUserId = jamesId } }), "putting James on the team");
        var attendeeId = roster.EnumerateArray().First(r => r.GetProperty("appUserId").GetString() == jamesId)
            .GetProperty("attendeeId").GetString();
        await OkJsonAsync(await _sarah.PutAsync($"/api/organizations/{_orgId}/investigations/{id}/attendees/{attendeeId}/attendance",
            new() { DataObject = new { didAttend = true, statedArrivalTime = DateTime.UtcNow.AddHours(-1) } }), "marking James as there");

        await OpenTheEditorAsync(id, title);
        await Dialog.Locator("#newinvestigationwindow-delete").ClickAsync();
        await Expect(Dialog.Locator("#newinvestigationwindow-confirm-yes")).ToHaveTextAsync("Yes, delete it");
        await Dialog.Locator("#newinvestigationwindow-confirm-yes").ClickAsync();

        await Expect(Page.Locator("#newinvestigationwindow-error")).ToHaveTextAsync(
            "This visit has 1 person marked as there, and deleting it would lose them. Cancel it instead. "
          + "A canceled visit keeps them on record.", new() { Timeout = 15_000 });

        await Footer.GetByRole(AriaRole.Button, new() { Name = "Close", Exact = true }).ClickAsync();
        await Expect(Page.Locator(".modal.show")).ToHaveCountAsync(0, new() { Timeout = 10_000 });
        await Expect(Row(id)).ToBeVisibleAsync();
        Assert.That((await _sarah.GetAsync($"/api/organizations/{_orgId}/investigations/{id}")).Ok, Is.True,
            "The refusal showed and the visit was deleted anyway.");
    }

    [Test]
    public async Task Deleting_an_empty_visit_takes_its_row_away()
    {
        var (id, title) = await AVisitAsync("empty");

        await OpenTheEditorAsync(id, title);
        await Dialog.Locator("#newinvestigationwindow-delete").ClickAsync();
        await Expect(Dialog.Locator("#newinvestigationwindow-confirm")).ToContainTextAsync("Delete this visit?");
        await Dialog.Locator("#newinvestigationwindow-confirm-yes").ClickAsync();
        await WindowClosesAsync("Delete");

        await Expect(Row(id)).ToHaveCountAsync(0, new() { Timeout = 20_000 });
        Assert.That((await _sarah.GetAsync($"/api/organizations/{_orgId}/investigations/{id}")).Status, Is.EqualTo(404),
            "The row went and the visit is still on the server.");
    }

    // ── Who is offered it ───────────────────────────────────────────────────

    [Test]
    public async Task A_plain_member_is_not_offered_Edit()
    {
        var (id, _) = await AVisitAsync("member");

        await OpenTheListAsync(MemberEmail, MemberPassword);
        await Expect(Row(id)).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.Locator($"#inv-edit-{id}")).ToHaveCountAsync(0);

        // And the door behind the button is shut too.
        var james = await ApiAsAsync(MemberEmail, MemberPassword);
        var cancel = await james.PostAsync($"/api/organizations/{_orgId}/investigations/{id}/cancel");
        Assert.That(cancel.Ok, Is.False, "A plain member could cancel somebody else's visit through the API.");
    }

    // ── The window's clock (gaps 2 and 15) ──────────────────────────────────

    [Test]
    public async Task A_new_window_offers_7_PM_tomorrow_and_a_November_start_reads_CST()
    {
        await OpenTheListAsync(UserEmail, UserPassword);
        await ClickUntilAsync(Main.GetByRole(AriaRole.Button, new() { Name = "Schedule an investigation" }),
            Dialog.Locator("#newinvestigationwindow-title-48f3"));

        var tomorrowAt7 = TodayAt(19).AddDays(1);
        var when = Dialog.Locator("#newinvestigationwindow-when-60b5");
        var line = Page.GetByTestId("newinvestigationwindow-zone");
        await Expect(when).ToHaveValueAsync(BoxDateTime(tomorrowAt7), new() { Timeout = 15_000 });
        var tomorrowLetters = TimeZoneInfo.FindSystemTimeZoneById(Central)
            .IsDaylightSavingTime(UtcFrom(tomorrowAt7, Central)) ? "CDT" : "CST";
        await Expect(line).ToHaveTextAsync($"Times are in your time zone ({tomorrowLetters}).");

        // The next November 20th, which is always standard time in Central.
        var today = TodayAt(0);
        var november = new DateTime(today.Month > 11 || (today.Month == 11 && today.Day >= 20) ? today.Year + 1 : today.Year, 11, 20, 19, 0, 0);
        await TypeIntoDateFieldAsync(when, BoxDateTime(november));
        await Expect(when).ToHaveValueAsync(BoxDateTime(november));
        await Expect(line).ToHaveTextAsync("Times are in your time zone (CST).", new() { Timeout = 10_000 });

        await Footer.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
        await Expect(Page.Locator(".modal.show")).ToHaveCountAsync(0, new() { Timeout = 10_000 });
    }

    // ── Places we already have (gap 10) ─────────────────────────────────────

    /// <summary>
    /// "Bell Witch" is part of a name, which the duplicate matcher never matched. It is now listed
    /// under "Places we already have", and choosing it books the visit at the seeded cave rather
    /// than making a second one.
    /// </summary>
    [Test]
    public async Task Part_of_a_name_lists_places_we_already_have_and_choosing_one_makes_no_second_place()
    {
        async Task<int> CavesAsync()
        {
            var found = await OkJsonAsync(await _sarah.GetAsync("/api/places/search?q=" + Uri.EscapeDataString("Bell Witch Cave")),
                "searching for Bell Witch Cave");
            return found.EnumerateArray().Count(p => string.Equals(p.GetProperty("name").GetString()?.Trim(),
                "Bell Witch Cave", StringComparison.OrdinalIgnoreCase));
        }
        var cavesBefore = await CavesAsync();
        var title = $"Oct16 known place {Guid.NewGuid():N}"[..26];

        await OpenTheListAsync(UserEmail, UserPassword);
        await ClickUntilAsync(Main.GetByRole(AriaRole.Button, new() { Name = "Schedule an investigation" }),
            Dialog.Locator("#newinvestigationwindow-title-48f3"));
        await Dialog.Locator("#newinvestigationwindow-title-48f3").FillAsync(title);

        var kind = Dialog.Locator("#newinvestigationwindow-kind-of-place-a45d");
        await kind.SelectOptionAsync(new[] { new SelectOptionValue { Value = "PublicLocation" } });
        var name = Dialog.Locator("#newinvestigationwindow-name-a-landmark-or-27ff");
        await name.ClickAsync();
        await name.PressSequentiallyAsync("Bell Witch", new() { Delay = 20 });

        var matches = Dialog.Locator("#newinvestigationwindow-public-matches");
        await Expect(matches).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Expect(matches).ToContainTextAsync("Places we already have");
        var cave = matches.Locator("div.d-flex", new() { HasTextString = "Bell Witch Cave" })
            .GetByRole(AriaRole.Button, new() { Name = "Use this place" }).First;
        await cave.ClickAsync();
        await Expect(Dialog.Locator(".alert-success")).ToContainTextAsync("Bell Witch Cave");

        var scope = Dialog.Locator("#newinvestigationwindow-who-can-see-the-fea9");
        if (await scope.Locator("option[value='GroupOnly']").CountAsync() > 0)
            await scope.SelectOptionAsync(new[] { new SelectOptionValue { Value = "GroupOnly" } });

        await Dialog.Locator("#newinvestigationwindow-save").ClickAsync();
        await WindowClosesAsync("Schedule");

        var rows = await OkJsonAsync(await _sarah.GetAsync($"/api/organizations/{_orgId}/investigations"), "reading the visits");
        var row = rows.EnumerateArray().FirstOrDefault(r => r.GetProperty("title").GetString() == title);
        Assert.That(row.ValueKind, Is.EqualTo(JsonValueKind.Object), $"No visit called \"{title}\" was saved.");
        _visits.Add(row.GetProperty("id").GetString()!);

        var cavesAfter = await CavesAsync();
        Assert.Multiple(() =>
        {
            Assert.That(row.GetProperty("placeId").GetString(), Is.EqualTo(BellWitchCaveId),
                "The visit was not booked at the cave that was chosen.");
            Assert.That(cavesAfter, Is.EqualTo(cavesBefore),
                "Choosing a place we already have made another Bell Witch Cave.");
        });
    }

    // ── Looking at it ───────────────────────────────────────────────────────

    /// <summary>
    /// The change window's footer (Delete, Cancel this visit, Close, Save changes) and its inline
    /// question, on every screen: inside the screen, overlapping nothing, and big enough to touch
    /// below 992px.
    /// </summary>
    [Test]
    [TestCase("dark")]
    [TestCase("light")]
    public async Task The_change_windows_buttons_fit_every_screen_and_are_big_enough_to_touch(string theme)
    {
        await UseThemeAsync(theme);
        var (id, title) = await AVisitAsync("window");

        var problems = new List<string>();
        foreach (var (width, height) in Widths)
        {
            await Page.SetViewportSizeAsync(width, height);
            await OpenTheEditorAsync(id, title);

            var delete = Dialog.Locator("#newinvestigationwindow-delete");
            var cancelVisit = Dialog.Locator("#newinvestigationwindow-cancel-visit");
            var close = Footer.GetByRole(AriaRole.Button, new() { Name = "Close", Exact = true });
            var save = Dialog.Locator("#newinvestigationwindow-save");

            problems.AddRange(await LookAtAsync(Footer, "change-window-footer", theme));
            problems.AddRange(await LookAtAsync(delete, "change-window-delete", theme, cancelVisit, close, save));
            problems.AddRange(await LookAtAsync(save, "change-window-save", theme, close, cancelVisit));
            problems.AddRange(await TouchTargetAsync(delete, "change-window-delete", theme));
            problems.AddRange(await TouchTargetAsync(cancelVisit, "change-window-cancel-visit", theme));
            problems.AddRange(await TouchTargetAsync(close, "change-window-close", theme));
            problems.AddRange(await TouchTargetAsync(save, "change-window-save", theme));

            await delete.ClickAsync();
            var yes = Dialog.Locator("#newinvestigationwindow-confirm-yes");
            var no = Dialog.Locator("#newinvestigationwindow-confirm-no");
            problems.AddRange(await LookAtAsync(Dialog.Locator("#newinvestigationwindow-confirm"), "change-window-confirm", theme));
            problems.AddRange(await LookAtAsync(yes, "change-window-confirm-yes", theme, no));
            problems.AddRange(await TouchTargetAsync(yes, "change-window-confirm-yes", theme));
            problems.AddRange(await TouchTargetAsync(no, "change-window-confirm-no", theme));
            await no.ClickAsync();
            await Expect(Dialog.Locator("#newinvestigationwindow-confirm")).ToHaveCountAsync(0);

            await close.ClickAsync();
            await Expect(Page.Locator(".modal.show")).ToHaveCountAsync(0, new() { Timeout = 10_000 });
        }

        NothingWrong(problems);
    }
}
