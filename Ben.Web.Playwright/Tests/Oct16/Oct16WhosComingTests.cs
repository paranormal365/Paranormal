using System.Text.Json;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests.Oct16;

/// <summary>
/// The calendar editor's "Who's coming" list (fix/investigation-team-and-rsvp, 10/16/2026): a host
/// can record that somebody they invited has said yes, and take it back.
/// </summary>
/// <remarks>
/// <para><b>The gap it closes.</b> The RSVP endpoint and its adapter existed and no screen called
/// them, so a member invited to a private event stayed "Invited" for good — and "coming" is what
/// the reminder, the head count, capacity and a guest's Field Kit attach read.</para>
///
/// <para>Sarah (an administrator) makes a private date through the API, then does the rest as a
/// host does: opens it, ticks James in the invite list, saves, opens it again, marks him as coming,
/// and finds him still coming after a reload — and after pressing Save, which re-sends the invite
/// list and must not reset the answer. The date is deleted afterwards.</para>
/// </remarks>
[TestFixture]
[Category("Oct16")]
[Category("Calendar")]
[NonParallelizable]
public class Oct16WhosComingTests : Oct16TestBase
{
    private IAPIRequestContext _sarah = null!;
    private string _orgId = "";
    private string? _eventId;
    private string _title = "";
    private string _jamesId = "";
    private string _jamesName = "";

    [SetUp]
    public async Task APrivateDateThisWeek()
    {
        await SkipIfFeatureOffAsync("features.events");
        _orgId = await OrgIdBySlugAsync(GroupSlug);
        _sarah = await ApiAsAsync(UserEmail, UserPassword);
        _jamesId = await MyUserIdAsync(await ApiAsAsync(MemberEmail, MemberPassword));

        var members = await OkJsonAsync(await _sarah.GetAsync($"/api/organizations/{_orgId}/roster"), "reading the group's members");
        _jamesName = members.EnumerateArray()
            .First(m => m.GetProperty("appUserId").GetString() == _jamesId)
            .GetProperty("displayName").GetString() ?? "James";

        // Noon UTC today: in the week the calendar opens on, mid-day whatever the zone (as CalendarDragTests).
        _title = $"Oct16 coming {Guid.NewGuid():N}"[..21];
        var start = DateTime.UtcNow.Date.AddHours(12);
        var made = await OkJsonAsync(await _sarah.PostAsync($"/api/organizations/{_orgId}/calendar", new()
        {
            DataObject = new { title = _title, startDateTime = start, endDateTime = start.AddHours(2), isAllDay = false, isPublic = false },
        }), "making the private date");
        _eventId = made.GetProperty("id").GetString();
    }

    [TearDown]
    public async Task TakeTheDateAway()
    {
        if (_eventId is not null)
            await _sarah.DeleteAsync($"/api/organizations/{_orgId}/calendar/{_eventId}");
        _eventId = null;
    }

    private ILocator TheEvent => Page.Locator(".k-event", new() { HasTextString = _title });
    private ILocator Dialog => Page.Locator(".modal.show").First;
    private ILocator GuestList => Page.Locator("#calendar-guest-list");
    private ILocator JamesGuestRow => GuestList.Locator("tr", new() { HasTextString = _jamesName });

    private async Task OpenTheEditorAsync()
    {
        await Page.GotoAsync($"{BaseUrl}/organizations/{_orgId}/calendar");
        await WaitForTheCircuitAsync();
        await SkipAnyTourAsync();
        await Expect(TheEvent).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await TheEvent.ScrollIntoViewIfNeededAsync();
        for (var attempt = 0; attempt < 4; attempt++)
        {
            await TheEvent.DblClickAsync();
            try
            {
                await Expect(Dialog.Locator("#orgscheduler-title-d7a1")).ToHaveValueAsync(_title, new() { Timeout = 8_000 });
                return;
            }
            catch (PlaywrightException) when (attempt < 3) { }
        }
    }

    private async Task<JsonElement?> JamesOnTheListAsync()
    {
        var list = await OkJsonAsync(await _sarah.GetAsync($"/api/organizations/{_orgId}/calendar/{_eventId}/attendees"), "reading the guest list");
        var row = list.EnumerateArray().FirstOrDefault(a => a.GetProperty("appUserId").GetString() == _jamesId);
        return row.ValueKind == JsonValueKind.Object ? row : null;
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

    [Test]
    public async Task A_host_invites_a_member_and_marks_them_as_coming_and_it_stays()
    {
        await LoginAsync(UserEmail, UserPassword);
        await OpenTheEditorAsync();

        // A new private date has nobody on it, so there is no list yet.
        await Expect(GuestList).ToHaveCountAsync(0);

        // ── invite James the way a host does ────────────────────────────────
        var chip = Dialog.Locator("div[style*='cursor:pointer']", new() { HasTextString = _jamesName });
        await Expect(chip).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await chip.ClickAsync();
        await Expect(chip.Locator("input[type=checkbox]")).ToBeCheckedAsync();
        await SaveAndCloseAsync();
        await Expect(Page.GetByText("could not be invited", new() { Exact = false })).ToHaveCountAsync(0);

        var invited = await JamesOnTheListAsync();
        Assert.That(invited, Is.Not.Null, "Ticking James and saving did not put him on the guest list.");
        Assert.That(IsEnum(invited!.Value.GetProperty("rsvpStatus"), 0, "Invited"), Is.True,
            $"A fresh invite should be Invited, and is {invited.Value.GetProperty("rsvpStatus")}.");

        // ── the list, and the host's answer for him ─────────────────────────
        await OpenTheEditorAsync();
        await Expect(GuestList).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Expect(GuestList).ToContainTextAsync("Who's coming");
        await Expect(JamesGuestRow).ToContainTextAsync("Hasn't answered");

        await JamesGuestRow.GetByRole(AriaRole.Button, new() { Name = "Mark as coming" }).ClickAsync();
        await Expect(JamesGuestRow.Locator(".badge")).ToHaveTextAsync("Coming", new() { Timeout = 15_000 });
        await Expect(JamesGuestRow.GetByRole(AriaRole.Button, new() { Name = "Mark as not answered" })).ToBeVisibleAsync();
        await Expect(GuestList.Locator("[role=alert]")).ToHaveCountAsync(0);

        // Saved at once, as the hint under the list says.
        Assert.That(IsEnum((await JamesOnTheListAsync())!.Value.GetProperty("rsvpStatus"), 1, "Accepted"), Is.True,
            "The list says Coming and the server still has him as not answered.");

        // Save re-sends the invite list. It must not turn his yes back into an invite.
        await SaveAndCloseAsync();
        Assert.That(IsEnum((await JamesOnTheListAsync())!.Value.GetProperty("rsvpStatus"), 1, "Accepted"), Is.True,
            "Pressing Save after marking him coming reset his answer.");

        // ── after a reload ──────────────────────────────────────────────────
        await Page.ReloadAsync();
        await OpenTheEditorAsync();
        await Expect(JamesGuestRow.Locator(".badge")).ToHaveTextAsync("Coming", new() { Timeout = 15_000 });

        // And back: "Mark as not answered".
        await JamesGuestRow.GetByRole(AriaRole.Button, new() { Name = "Mark as not answered" }).ClickAsync();
        await Expect(JamesGuestRow.Locator(".badge")).ToHaveTextAsync("Hasn't answered", new() { Timeout = 15_000 });
        Assert.That(IsEnum((await JamesOnTheListAsync())!.Value.GetProperty("rsvpStatus"), 0, "Invited"), Is.True,
            "The list says not answered and the server still has him as coming.");
    }

    /// <summary>The list in the editor, on a phone and a laptop.</summary>
    [Test]
    [TestCase("dark")]
    [TestCase("light")]
    public async Task The_whos_coming_list_fits_the_editor(string theme)
    {
        await UseThemeAsync(theme);
        await OkJsonAsync(await _sarah.PostAsync($"/api/organizations/{_orgId}/calendar/{_eventId}/attendees",
            new() { DataObject = new { appUserId = _jamesId, assignedTask = (string?)null } }), "inviting James");

        await LoginAsync(UserEmail, UserPassword);
        var problems = new List<string>();
        foreach (var (width, height) in Widths)
        {
            await Page.SetViewportSizeAsync(width, height);
            await OpenTheEditorAsync();
            await Expect(JamesGuestRow).ToBeVisibleAsync(new() { Timeout = 15_000 });

            problems.AddRange(await LookAtAsync(GuestList, "whos-coming-list", theme));
            problems.AddRange(await LookAtAsync(
                JamesGuestRow.GetByRole(AriaRole.Button, new() { Name = "Mark as coming" }), "whos-coming-button", theme,
                JamesGuestRow.Locator(".badge"), JamesGuestRow.Locator("td").First));

            await Dialog.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
            await Expect(Page.Locator(".modal.show")).ToHaveCountAsync(0, new() { Timeout = 10_000 });
        }

        NothingWrong(problems);
    }
}
