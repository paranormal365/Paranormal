using System.Text.Json;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests.Oct16;

/// <summary>
/// A private calendar event's guest list after fix/oct16-gaps (10/16/2026): an invitee can be taken
/// off (gap 3), a host can record any of the three answers (gap 4), and "Who's coming" keeps a long
/// name off the buttons on a phone (gap 14).
/// </summary>
/// <remarks>
/// <para><b>Before.</b> Unticking somebody and saving did nothing, and nothing on the page could
/// take an invitee off. A guest who had said maybe could only be marked as coming.</para>
///
/// <para>Sarah (an administrator) makes a private date at noon UTC today, so it is in the week the
/// calendar opens on, invites James through the API, and does the rest as a host does. The date is
/// deleted afterwards, which takes its guest list with it.</para>
/// </remarks>
[TestFixture]
[Category("Oct16")]
[Category("Calendar")]
[NonParallelizable]
public class Oct16GuestListTests : Oct16TestBase
{
    private IAPIRequestContext _sarah = null!;
    private string _orgId = "";
    private string? _eventId;
    private string _title = "";
    private string _jamesId = "";
    private string _jamesName = "";
    private bool _signedIn;

    [SetUp]
    public async Task APrivateDateWithJamesInvited()
    {
        await SkipIfFeatureOffAsync("features.events");
        _signedIn = false;   // each test has a fresh browser context
        _orgId = await OrgIdBySlugAsync(GroupSlug);
        _sarah = await ApiAsAsync(UserEmail, UserPassword);
        _jamesId = await MyUserIdAsync(await ApiAsAsync(MemberEmail, MemberPassword));

        var members = await OkJsonAsync(await _sarah.GetAsync($"/api/organizations/{_orgId}/roster"), "reading the group's members");
        _jamesName = members.EnumerateArray()
            .First(m => m.GetProperty("appUserId").GetString() == _jamesId)
            .GetProperty("displayName").GetString() ?? "James";

        _title = $"Oct16 guests {Guid.NewGuid():N}"[..21];
        // Noon on the calendar's own today, in Central time. UTC's date was used, and on a Saturday evening that is
        // already Sunday — next week, off the week the calendar opens on (10/24/2026, 10:42 PM CDT).
        var central = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
        var start = TimeZoneInfo.ConvertTimeToUtc(
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, central).Date.AddHours(12), central);
        var made = await OkJsonAsync(await _sarah.PostAsync($"/api/organizations/{_orgId}/calendar", new()
        {
            DataObject = new { title = _title, startDateTime = start, endDateTime = start.AddHours(2), isAllDay = false, isPublic = false },
        }), "making the private date");
        _eventId = made.GetProperty("id").GetString();

        await OkJsonAsync(await _sarah.PostAsync($"/api/organizations/{_orgId}/calendar/{_eventId}/attendees",
            new() { DataObject = new { appUserId = _jamesId, assignedTask = (string?)null } }), "inviting James");
    }

    [TearDown]
    public async Task TakeTheDateAway()
    {
        if (_eventId is not null)
            await _sarah.DeleteAsync($"/api/organizations/{_orgId}/calendar/{_eventId}");
        _eventId = null;
    }

    private string AttendeesApi => $"/api/organizations/{_orgId}/calendar/{_eventId}/attendees";

    private async Task<JsonElement?> JamesOnTheListAsync()
    {
        var list = await OkJsonAsync(await _sarah.GetAsync(AttendeesApi), "reading the guest list");
        var row = list.EnumerateArray().FirstOrDefault(a => a.GetProperty("appUserId").GetString() == _jamesId);
        return row.ValueKind == JsonValueKind.Object ? row : null;
    }

    private async Task<string> JamesGuestIdAsync()
        => (await JamesOnTheListAsync())?.GetProperty("id").GetString()
           ?? throw new AssertionException("James is not on the guest list.");

    private async Task AnswerForJamesAsync(int rsvp)
        => await OkJsonAsync(await _sarah.PutAsync($"{AttendeesApi}/{await JamesGuestIdAsync()}/rsvp",
            new() { DataObject = new { rsvpStatus = rsvp } }), "recording James's answer");

    private ILocator Dialog => Page.Locator(".modal.show").First;
    private ILocator GuestList => Page.Locator("#calendar-guest-list");
    private ILocator Row(string guestId) => Page.Locator($"#guest-row-{guestId}");
    private ILocator JamesChip => Dialog.Locator("div[style*='cursor:pointer']", new() { HasTextString = _jamesName });

    private async Task<ILocator> OpenTheEditorAsync()
    {
        if (!_signedIn)
        {
            await LoginAsync(UserEmail, UserPassword);
            _signedIn = true;
        }
        return await OpenCalendarEntryAsync(_orgId, _title);
    }

    private async Task SaveAndCloseAsync()
    {
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        try
        {
            await Expect(Page.Locator(".modal.show")).ToHaveCountAsync(0, new() { Timeout = 20_000 });
        }
        catch (PlaywrightException)
        {
            var refusal = Dialog.Locator(".alert-danger");
            Assert.Fail("The editor did not save: " + (await refusal.CountAsync() > 0
                ? await refusal.First.InnerTextAsync() : "it stayed open and said nothing."));
        }
    }

    private async Task CancelTheEditorAsync()
    {
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
        await Expect(Page.Locator(".modal.show")).ToHaveCountAsync(0, new() { Timeout = 10_000 });
    }

    // ── Gap 3: taking an invitee off ────────────────────────────────────────

    [Test]
    public async Task Unticking_an_invitee_and_saving_takes_them_off_the_guest_list()
    {
        var guestId = await JamesGuestIdAsync();
        await OpenTheEditorAsync();
        await Expect(Dialog.Locator("#calendar-untick-note")).ToHaveTextAsync(
            "Untick somebody to take them off the guest list when you save.");
        await Expect(JamesChip.Locator("input[type=checkbox]")).ToBeCheckedAsync(new() { Timeout = 10_000 });
        await Expect(Row(guestId)).ToBeVisibleAsync();

        await JamesChip.ClickAsync();
        await Expect(JamesChip.Locator("input[type=checkbox]")).Not.ToBeCheckedAsync();
        await SaveAndCloseAsync();
        await Expect(Page.GetByText("the guest list didn't change as asked", new() { Exact = false })).ToHaveCountAsync(0);

        Assert.That(await JamesOnTheListAsync(), Is.Null, "Unticking James and saving left him on the guest list.");

        await OpenTheEditorAsync();
        await Expect(JamesChip.Locator("input[type=checkbox]")).Not.ToBeCheckedAsync(new() { Timeout = 10_000 });
        await Expect(Row(guestId)).ToHaveCountAsync(0);
        await Expect(GuestList.Locator(".guest-row", new() { HasTextString = _jamesName })).ToHaveCountAsync(0);
        await CancelTheEditorAsync();
    }

    [Test]
    public async Task Remove_asks_first_Keep_changes_nothing_and_Remove_takes_them_off()
    {
        var guestId = await JamesGuestIdAsync();
        await OpenTheEditorAsync();
        await Expect(Row(guestId)).ToBeVisibleAsync(new() { Timeout = 15_000 });

        var remove = Page.Locator($"#guest-remove-{guestId}");
        await Expect(remove).ToHaveTextAsync("Remove");
        await remove.ClickAsync();
        await Expect(Row(guestId)).ToContainTextAsync($"Take {_jamesName} off the guest list?");
        await Expect(Page.Locator($"#guest-remove-yes-{guestId}")).ToHaveTextAsync("Remove");
        await Expect(Page.Locator($"#guest-remove-no-{guestId}")).ToHaveTextAsync("Keep");
        Assert.That(await JamesOnTheListAsync(), Is.Not.Null, "Pressing Remove took James off before anybody said yes.");

        // Keep: the question goes, the answers come back, nothing changed.
        await Page.Locator($"#guest-remove-no-{guestId}").ClickAsync();
        await Expect(Page.Locator($"#guest-remove-yes-{guestId}")).ToHaveCountAsync(0, new() { Timeout = 10_000 });
        await Expect(Page.Locator($"#guest-coming-{guestId}")).ToBeVisibleAsync();
        Assert.That(await JamesOnTheListAsync(), Is.Not.Null, "\"Keep\" took James off the guest list.");

        // Remove, and yes.
        await remove.ClickAsync();
        await Page.Locator($"#guest-remove-yes-{guestId}").ClickAsync();
        await Expect(Row(guestId)).ToHaveCountAsync(0, new() { Timeout = 15_000 });
        await Expect(GuestList.Locator("[role=alert]")).ToHaveCountAsync(0);
        Assert.That(await JamesOnTheListAsync(), Is.Null, "The row went and the server still has James on the list.");

        // His box is cleared too, so Save does not invite him straight back.
        await Expect(JamesChip.Locator("input[type=checkbox]")).Not.ToBeCheckedAsync();
        await SaveAndCloseAsync();
        Assert.That(await JamesOnTheListAsync(), Is.Null, "Save after Remove put James back on the list.");
    }

    /// <summary>
    /// A refusal shows under the list, and the row stays. Produced honestly: James is taken off
    /// through the API (another tab) between the question and the yes, so the server answers 404.
    /// That answer has no sentence of its own, so the page's own one is what shows.
    /// </summary>
    [Test]
    public async Task A_refused_remove_says_so_under_the_list_and_keeps_the_row()
    {
        var guestId = await JamesGuestIdAsync();
        await OpenTheEditorAsync();
        await Page.Locator($"#guest-remove-{guestId}").ClickAsync();
        await Expect(Page.Locator($"#guest-remove-yes-{guestId}")).ToBeVisibleAsync(new() { Timeout = 10_000 });

        var gone = await _sarah.DeleteAsync($"{AttendeesApi}/{guestId}");
        Assert.That(gone.Ok, Is.True, "Taking James off from the other tab failed: " + await gone.TextAsync());

        await Page.Locator($"#guest-remove-yes-{guestId}").ClickAsync();
        var refusal = GuestList.Locator("[role=alert]");
        await Expect(refusal).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(refusal).ToHaveTextAsync("That guest couldn't be taken off the list just now. Nothing was changed.");
        await Expect(Row(guestId)).ToBeVisibleAsync();
        await CancelTheEditorAsync();
    }

    // ── Gap 4: the three answers ────────────────────────────────────────────

    [Test]
    public async Task A_guest_who_said_maybe_is_offered_coming_and_not_coming()
    {
        await AnswerForJamesAsync(3);   // Tentative
        var guestId = await JamesGuestIdAsync();
        await OpenTheEditorAsync();

        await Expect(Row(guestId).Locator(".badge")).ToHaveTextAsync("Maybe", new() { Timeout = 15_000 });
        await Expect(Page.Locator($"#guest-coming-{guestId}")).ToHaveTextAsync("Mark as coming");
        await Expect(Page.Locator($"#guest-not-coming-{guestId}")).ToHaveTextAsync("Mark as not coming");
        await Expect(Page.Locator($"#guest-maybe-{guestId}")).ToHaveCountAsync(0);
        await Expect(Page.Locator($"#guest-not-answered-{guestId}")).ToHaveCountAsync(0);
        await CancelTheEditorAsync();
    }

    [Test]
    public async Task A_guest_who_is_coming_is_offered_not_coming_and_not_answered()
    {
        await AnswerForJamesAsync(1);   // Accepted
        var guestId = await JamesGuestIdAsync();
        await OpenTheEditorAsync();

        await Expect(Row(guestId).Locator(".badge")).ToHaveTextAsync("Coming", new() { Timeout = 15_000 });
        await Expect(Page.Locator($"#guest-not-coming-{guestId}")).ToHaveTextAsync("Mark as not coming");
        await Expect(Page.Locator($"#guest-not-answered-{guestId}")).ToHaveTextAsync("Mark as not answered");
        await Expect(Page.Locator($"#guest-coming-{guestId}")).ToHaveCountAsync(0);
        await CancelTheEditorAsync();
    }

    /// <summary>
    /// Only the pressed button says "Saving…", and the badge changes with no Save.
    /// </summary>
    /// <remarks>
    /// The site is Blazor Server: the page never calls the API itself, the server does, so a
    /// Playwright route cannot delay that call. Instead a MutationObserver records every text the
    /// two answer buttons show from the press until the answer is saved, with each text node's
    /// previous value, so a "Saving…" that lasted a few milliseconds is still caught.
    /// </remarks>
    [Test]
    public async Task Only_the_pressed_answer_says_Saving_and_the_badge_changes_without_Save()
    {
        var guestId = await JamesGuestIdAsync();
        await OpenTheEditorAsync();
        var coming = Page.Locator($"#guest-coming-{guestId}");
        var notComing = Page.Locator($"#guest-not-coming-{guestId}");
        await Expect(notComing).ToHaveTextAsync("Mark as not coming", new() { Timeout = 15_000 });
        await Expect(coming).ToHaveTextAsync("Mark as coming");

        await Page.EvaluateAsync(@"([pressedId, otherId]) => {
            const pressed = document.getElementById(pressedId), other = document.getElementById(otherId);
            const saw = window.__oct16Saw = { pressed: [], other: [] };
            const whose = node => {
                const el = node && (node.nodeType === 3 ? node.parentElement : node);
                const button = el && el.closest ? el.closest('button') : null;
                return button === pressed ? 'pressed' : button === other ? 'other' : null;
            };
            window.__oct16Watch = new MutationObserver(records => {
                for (const r of records) {
                    const who = whose(r.target);
                    if (!who) continue;
                    if (r.type === 'characterData' && r.oldValue) saw[who].push(r.oldValue.trim());
                    for (const n of [...r.addedNodes, ...r.removedNodes]) if (n.textContent) saw[who].push(n.textContent.trim());
                }
                saw.pressed.push(pressed.textContent.trim());
                saw.other.push(other.textContent.trim());
            });
            window.__oct16Watch.observe(document.getElementById('calendar-guest-list'),
                { subtree: true, childList: true, characterData: true, characterDataOldValue: true });
        }", new[] { $"guest-not-coming-{guestId}", $"guest-coming-{guestId}" });

        await notComing.ClickAsync();

        // The badge, with no Save pressed: the editor is still open.
        await Expect(Row(guestId).Locator(".badge")).ToHaveTextAsync("Not coming", new() { Timeout = 15_000 });
        await Expect(Dialog).ToBeVisibleAsync();
        await Expect(GuestList.Locator("[role=alert]")).ToHaveCountAsync(0);

        var saw = await Page.EvaluateAsync<JsonElement>("() => { window.__oct16Watch.disconnect(); return window.__oct16Saw; }");
        var pressedSaw = saw.GetProperty("pressed").EnumerateArray().Select(t => t.GetString()).ToList();
        var otherSaw = saw.GetProperty("other").EnumerateArray().Select(t => t.GetString()).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(pressedSaw, Has.Some.EqualTo("Saving…"),
                "The pressed button never said Saving…. It showed: " + string.Join(" | ", pressedSaw));
            Assert.That(otherSaw, Has.None.EqualTo("Saving…"),
                "The button that was not pressed said Saving… too. It showed: " + string.Join(" | ", otherSaw));
        });

        Assert.That(IsEnum((await JamesOnTheListAsync())!.Value.GetProperty("rsvpStatus"), 2, "Declined"), Is.True,
            "The badge says Not coming and the server does not.");

        // Leaving without Save keeps it: it was saved when pressed.
        await CancelTheEditorAsync();
        Assert.That(IsEnum((await JamesOnTheListAsync())!.Value.GetProperty("rsvpStatus"), 2, "Declined"), Is.True,
            "Closing the editor without Save lost the answer.");
    }

    // ── Gap 14: on every screen ─────────────────────────────────────────────

    /// <summary>
    /// Below 576px a guest's buttons sit on a line below the name; from 576px they share its line.
    /// The row fits the screen at every width, and below 992px the buttons are big enough to touch.
    /// </summary>
    [Test]
    [TestCase("dark")]
    [TestCase("light")]
    public async Task Whos_coming_puts_the_buttons_under_the_name_on_a_phone_and_beside_it_from_576px(string theme)
    {
        await UseThemeAsync(theme);
        var guestId = await JamesGuestIdAsync();
        var problems = new List<string>();

        foreach (var (width, height) in Widths)
        {
            await Page.SetViewportSizeAsync(width, height);
            await OpenTheEditorAsync();

            var row = Row(guestId);
            var name = row.Locator(".guest-row__who > :first-child");
            var actions = row.Locator(".guest-row__actions");
            await Expect(actions).ToBeVisibleAsync(new() { Timeout = 15_000 });

            problems.AddRange(await LookAtAsync(row, "guest-row", theme));
            problems.AddRange(await LookAtAsync(actions, "guest-row-actions", theme, name, row.Locator(".badge")));

            await name.ScrollIntoViewIfNeededAsync();
            var nameBox = await name.BoundingBoxAsync();
            var actionsBox = await actions.BoundingBoxAsync();
            if (nameBox is { } n && actionsBox is { } a)
            {
                var tag = $"Who's coming at {width}px ({theme})";
                if (width < 576 && a.Y < n.Y + n.Height - 1)
                    problems.Add($"{tag}: the buttons (top {a.Y:0}) are not below the name (bottom {n.Y + n.Height:0}).");
                if (width >= 576 && (a.Y >= n.Y + n.Height || a.Y + a.Height <= n.Y))
                    problems.Add($"{tag}: the buttons (y {a.Y:0}–{a.Y + a.Height:0}) are not on the name's line (y {n.Y:0}–{n.Y + n.Height:0}).");
            }

            foreach (var (id, what) in new[]
                     {
                         ($"guest-coming-{guestId}", "guest-mark-coming"),
                         ($"guest-not-coming-{guestId}", "guest-mark-not-coming"),
                         ($"guest-remove-{guestId}", "guest-remove"),
                     })
                problems.AddRange(await TouchTargetAsync(Page.Locator($"#{id}"), what, theme));

            // Remove's question and its answers.
            await Page.Locator($"#guest-remove-{guestId}").ClickAsync();
            var yes = Page.Locator($"#guest-remove-yes-{guestId}");
            var no = Page.Locator($"#guest-remove-no-{guestId}");
            await Expect(yes).ToBeVisibleAsync(new() { Timeout = 10_000 });
            problems.AddRange(await LookAtAsync(row, "guest-row-remove-ask", theme));
            problems.AddRange(await LookAtAsync(yes, "guest-remove-yes", theme, no));
            problems.AddRange(await TouchTargetAsync(yes, "guest-remove-yes", theme));
            problems.AddRange(await TouchTargetAsync(no, "guest-remove-no", theme));
            await no.ClickAsync();

            await CancelTheEditorAsync();
        }

        NothingWrong(problems);
    }
}
