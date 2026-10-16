using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests.Oct16;

/// <summary>
/// A grey booking button always says, beside it, what it is waiting for (fix/public-event-page,
/// 10/16/2026), and each contact field says it is required.
/// </summary>
/// <remarks>
/// <para><b>The bug.</b> AskForPlaceForm disabled "Ask for a place" unless a night was ticked, the
/// contact was enough and no dietary note lacked a name — and only the night said why. A signed-in
/// guest whose account had a name and no phone saw a filled-in form and a dead button.</para>
///
/// <para>Each test walks the sentences in the order the form asks, and checks the button follows
/// the sentence at every step: grey while there is one (and pointing at it with
/// <c>aria-describedby</c>), live the moment there is none. Nothing is sent: no button is
/// pressed.</para>
///
/// <para>Seats: Daniel (the client seat) asks for a room as a signed-in guest; a stranger, signed
/// out, picks seats; a stranger gives an address on an ordinary public date. The public date is
/// made for the test and deleted.</para>
/// </remarks>
[TestFixture]
[Category("Oct16")]
[Category("HostedEvents")]
[NonParallelizable]
public class Oct16BookingReasonTests : Oct16TestBase
{
    private string _orgId = "";
    private string _roomsUrl = "";
    private string _seatsUrl = "";
    private IAPIRequestContext _sarah = null!;
    private string? _dateId;

    [SetUp]
    public async Task BothEventsOnTheSite()
    {
        _orgId = await OrgIdBySlugAsync(GroupSlug);
        _sarah = await ApiAsAsync(UserEmail, UserPassword);
        var admin = await ApiAsAsync(SuperAdminEmail, SuperAdminPassword);

        // The weekend asks, the evening picks — whatever an earlier fixture left.
        await admin.PostAsync($"/api/organizations/{_orgId}/events/{RoomsEventId}/booking-mode", new() { DataObject = new { mode = 0 } });
        await admin.PostAsync($"/api/organizations/{_orgId}/events/{SeatsEventId}/booking-mode", new() { DataObject = new { mode = 1 } });
        _roomsUrl = $"{BaseUrl}/o/{GroupSlug}/events/{await PublishSeededEventAsync(_orgId, RoomsEventId)}";
        _seatsUrl = $"{BaseUrl}/o/{GroupSlug}/events/{await PublishSeededEventAsync(_orgId, SeatsEventId)}";

        // Daniel must not already have asked, or the form is replaced by his booking's card.
        var daniel = await ApiAsAsync(ClientEmail, ClientPassword);
        var mine = await daniel.GetAsync("/api/public/hosted-events/mine");
        if (mine.Ok)
        {
            foreach (var booking in (await mine.JsonAsync())!.Value.EnumerateArray())
                if (booking.GetProperty("hostedEventId").GetString() is { } id)
                    await daniel.DeleteAsync($"/api/public/hosted-events/{id}/my-booking");
        }
    }

    [TearDown]
    public async Task TakeTheDateAway()
    {
        if (_dateId is not null) await _sarah.DeleteAsync($"/api/organizations/{_orgId}/calendar/{_dateId}");
        _dateId = null;
    }

    private async Task GreyWithAsync(ILocator button, ILocator line, string lineId, string sentence)
    {
        await Expect(line).ToHaveTextAsync(sentence, new() { Timeout = 10_000 });
        await Expect(button).ToBeDisabledAsync();
        await Expect(button).ToHaveAttributeAsync("aria-describedby", lineId);
    }

    private async Task LiveAsync(ILocator button, ILocator line)
    {
        await Expect(line).ToHaveCountAsync(0, new() { Timeout = 10_000 });
        await Expect(button).ToBeEnabledAsync();
        await Expect(button).Not.ToHaveAttributeAsync("aria-describedby", new System.Text.RegularExpressions.Regex(".+"));
    }

    // ── Asking for a room, signed in ────────────────────────────────────────

    [Test]
    public async Task Ask_for_a_place_says_why_it_is_grey_at_every_step_and_wakes_when_nothing_is_missing()
    {
        await LoginAsync(ClientEmail, ClientPassword);
        await OpenTheAskFormAsync();

        var send = Page.Locator("#ask-send");
        var line = Page.Locator("#ask-missing");

        // 1. a night
        await GreyWithAsync(send, line, "ask-missing", "Tick at least one night.");
        await Page.Locator("#hosted-ask input[type=checkbox][id^='ask-night-']").First.CheckAsync();

        // The account's answers fold the contact into one line; open it to change them.
        if (await Page.Locator("#ask-contact-change").IsVisibleAsync())
            await Page.Locator("#ask-contact-change").ClickAsync();
        await Expect(Page.Locator("#ask-phone")).ToBeVisibleAsync(new() { Timeout = 10_000 });

        // Every contact field says it is required.
        foreach (var id in new[] { "ask-first", "ask-last", "ask-phone" })
        {
            await Expect(Page.Locator($"label[for='{id}']")).ToContainTextAsync("(required)");
            await Expect(Page.Locator($"#{id}")).ToHaveAttributeAsync("aria-required", "true");
        }

        // 2. a dietary note with no name — the party comes before the contact.
        var leadName = Page.Locator("#ask-guest-name-0");
        if (await leadName.CountAsync() > 0)
        {
            var kept = await leadName.InputValueAsync();
            await leadName.FillAsync("");
            await Page.Locator("#ask-guest-notes-0").FillAsync("vegan");
            await GreyWithAsync(send, line, "ask-missing", "Give each dietary note a name.");
            await leadName.FillAsync(kept.Length > 0 ? kept : "Test Guest");
        }

        // 3. the name, then the phone — the one the guest on production never saw.
        if ((await Page.Locator("#ask-first").InputValueAsync()).Length == 0) await Page.Locator("#ask-first").FillAsync("Test");
        if ((await Page.Locator("#ask-last").InputValueAsync()).Length == 0) await Page.Locator("#ask-last").FillAsync("Guest");

        await Page.Locator("#ask-phone").FillAsync("");
        await GreyWithAsync(send, line, "ask-missing", "Add a phone number so Paranormal365 can reach you.");

        await Page.Locator("#ask-phone").FillAsync("555");
        await GreyWithAsync(send, line, "ask-missing", "Check your phone number. It needs 7 to 15 digits.");

        await Page.Locator("#ask-phone").FillAsync("615-555-0100");
        await LiveAsync(send, line);

        // And back: clearing the last name names it.
        await Page.Locator("#ask-last").FillAsync("");
        await GreyWithAsync(send, line, "ask-missing", "Add your last name.");
    }

    // ── Picking seats, signed out ───────────────────────────────────────────

    [Test]
    public async Task Picking_seats_signed_out_says_what_is_missing_beside_the_button()
    {
        await OpenThePickerWithASeatAsync();

        var hold = Page.Locator("#picker-hold");
        var line = Page.Locator("#picker-missing");

        await GreyWithAsync(hold, line, "picker-missing", "Add your first and last name.");
        await Page.Locator("#picker-first").FillAsync("Test");
        await GreyWithAsync(hold, line, "picker-missing", "Add your last name.");
        await Page.Locator("#picker-last").FillAsync("Guest");
        await GreyWithAsync(hold, line, "picker-missing", "Add your email address so we can send you the link.");
        await Expect(Page.Locator("label[for='picker-email']")).ToContainTextAsync("(required)");
        await Page.Locator("#picker-email").FillAsync("oct16-guest");
        await GreyWithAsync(hold, line, "picker-missing", "Check your email address. It needs an @.");
        await Page.Locator("#picker-email").FillAsync("oct16-guest@example.com");
        await GreyWithAsync(hold, line, "picker-missing", "Add a phone number so Paranormal365 can reach you.");
        await Page.Locator("#picker-phone").FillAsync("615-555-0100");
        await LiveAsync(hold, line);
    }

    // ── An ordinary public date's "Coming along?" ───────────────────────────

    [Test]
    public async Task Coming_along_by_email_says_what_is_missing_beside_the_button()
    {
        var url = await AnOrdinaryPublicDateAsync();
        await Page.GotoAsync(url);
        await WaitForTheCircuitAsync();

        var act = Page.Locator("#event-act");
        // The box has no id (see the report); it is the one email input in the act card.
        var box = act.Locator("input[type='email']");
        var send = act.GetByRole(AriaRole.Button, new() { Name = "Send me the link" });
        var line = Page.Locator("#attend-email-missing");
        await Expect(box).ToBeVisibleAsync(new() { Timeout = 30_000 });

        await GreyWithAsync(send, line, "attend-email-missing", "Add your email address so we can send you the link.");
        await box.PressSequentiallyAsync("ghost", new() { Delay = 20 });
        await GreyWithAsync(send, line, "attend-email-missing", "Check your email address. It needs an @.");
        await box.PressSequentiallyAsync("@example.com", new() { Delay = 20 });
        await LiveAsync(send, line);
    }

    // ── Looking at it ───────────────────────────────────────────────────────

    [Test]
    [TestCase("dark")]
    [TestCase("light")]
    public async Task The_missing_lines_sit_beside_their_buttons_without_covering_them(string theme)
    {
        await UseThemeAsync(theme);
        var problems = new List<string>();
        var dateUrl = await AnOrdinaryPublicDateAsync();

        foreach (var (width, height) in Widths)
        {
            await Page.SetViewportSizeAsync(width, height);

            // Signed out first: the seat picker and the ordinary date.
            await OpenThePickerWithASeatAsync();
            problems.AddRange(await LookAtAsync(Page.Locator("#picker-missing"), "picker-missing", theme,
                Page.Locator("#picker-hold"), Page.Locator("#picker-clear")));

            await Page.GotoAsync(dateUrl);
            await WaitForTheCircuitAsync();
            var act = Page.Locator("#event-act");
            problems.AddRange(await LookAtAsync(Page.Locator("#attend-email-missing"), "attend-email-missing", theme,
                act.Locator("input[type='email']"), act.GetByRole(AriaRole.Button, new() { Name = "Send me the link" })));
        }

        await LoginAsync(ClientEmail, ClientPassword);
        foreach (var (width, height) in Widths)
        {
            await Page.SetViewportSizeAsync(width, height);
            await OpenTheAskFormAsync();
            // The phone's fixed "Ask for a place" is a neighbour too: it must not sit over the line
            // or the button (it steps aside while the form is on screen).
            problems.AddRange(await LookAtAsync(Page.Locator("#ask-missing"), "ask-missing", theme,
                Page.Locator("#ask-send"), Page.Locator("#hosted-sticky-ask")));
            problems.AddRange(await LookAtAsync(Page.Locator("#ask-send"), "ask-send", theme,
                Page.Locator("#hosted-sticky-ask")));
        }

        NothingWrong(problems);
    }

    // ── plumbing ────────────────────────────────────────────────────────────

    private async Task OpenTheAskFormAsync()
    {
        await Page.GotoAsync(_roomsUrl);
        await WaitForTheCircuitAsync();
        await WaitUntilLoadedAsync();
        await Expect(Page.Locator("#hosted-ask")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.Locator("#ask-missing")).ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    private async Task OpenThePickerWithASeatAsync()
    {
        await Page.GotoAsync(_seatsUrl);
        await WaitForTheCircuitAsync();
        await Expect(Page.Locator(".plan__grid")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        var free = Page.Locator(".plan__unit[data-state='free']");
        await Expect(free.First).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await ClickUntilAsync(free.First, Page.Locator("#picker-contact"));
        await Expect(Page.Locator("#picker-missing")).ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    /// <summary>A public date ten days out, made for this test.</summary>
    private async Task<string> AnOrdinaryPublicDateAsync()
    {
        if (_dateId is not null) await _sarah.DeleteAsync($"/api/organizations/{_orgId}/calendar/{_dateId}");
        var start = DateTime.UtcNow.Date.AddDays(10).AddHours(1);
        var (id, slug) = await PublicCalendarEventAsync(_sarah, _orgId, $"Oct16 reasons {Guid.NewGuid():N}"[..22], start, start.AddHours(3));
        _dateId = id;
        return $"{BaseUrl}/o/{GroupSlug}/events/{slug}";
    }
}
