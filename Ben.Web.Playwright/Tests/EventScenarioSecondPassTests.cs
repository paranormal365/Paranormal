using Ben.Web.Playwright.Tests.Oct16;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The second pass on Jim's night, after Ben's answers (event-scenario-gaps, 10/21/2026): finding the event
/// on the map and by searching, its dates in a calendar file, who's at the classes, who came, a guest
/// writing to the organizers, and a sender's yes to their session being shown with the results.
/// </summary>
/// <remarks>
/// On the seeded séance weekend. A test that makes a booking takes it back. The guest's message thread is
/// left: it holds nothing and is only seen by the guest and the event's organizers.
/// </remarks>
[TestFixture]
[Category("EventScenario")]
[Category("HostedEvents")]
[NonParallelizable]
public class EventScenarioSecondPassTests : Oct16TestBase
{
    private IAPIRequestContext _admin = null!;
    private string _orgId = "";

    [SetUp]
    public async Task SignIn()
    {
        _orgId = await OrgIdBySlugAsync(GroupSlug);
        _admin = await ApiAsAsync(SuperAdminEmail, SuperAdminPassword);
    }

    [TearDown]
    public async Task StopSellingAtTheDoor()
        => await _admin.PutAsync($"/api/organizations/{_orgId}/events/{RoomsEventId}/door-tickets",
                                 new() { DataObject = new { on = false } });

    private string EventPage(string tail = "") => $"{BaseUrl}/organizations/{_orgId}/events/{RoomsEventId}{tail}";

    private async Task<(string Name, string PublicPage)> TheEventAsync()
    {
        var ev = await OkJsonAsync(await _admin.GetAsync($"/api/public/hosted-events/{RoomsEventId}"), "reading the event");
        return (ev.GetProperty("name").GetString()!, $"{BaseUrl}/o/{GroupSlug}/events/{ev.GetProperty("urlName").GetString()}");
    }

    // ── finding it ───────────────────────────────────────────────────────────

    [Test]
    public async Task A_published_event_is_on_the_map_at_an_approximate_point_and_found_by_its_name()
    {
        var (name, _) = await TheEventAsync();
        var pins = await OkJsonAsync(await _admin.GetAsync("/api/public/hosted-events/map"), "reading the map's events");
        var pin = pins.EnumerateArray().FirstOrDefault(p => p.GetProperty("id").GetString() == RoomsEventId);
        if (pin.ValueKind == System.Text.Json.JsonValueKind.Undefined)
            Assert.Ignore("The seeded weekend isn't published with a place today, so it has no pin.");
        Assert.That(pin.GetProperty("organizationUrlName").GetString(), Is.EqualTo(GroupSlug));

        var problems = new List<string>();
        foreach (var theme in new[] { "light", "dark" })
        {
            await UseThemeAsync(theme);
            foreach (var (width, height) in Widths)
            {
                await Page.SetViewportSizeAsync(width, height);
                await Page.GotoAsync($"{BaseUrl}/events");
                await Page.Locator("#events-search-words").FillAsync(name, new() { Timeout = 30_000 });
                await Page.Locator("#events-search-go").ClickAsync();
                await Expect(Page.GetByText(name).First).ToBeVisibleAsync(new() { Timeout = 30_000 });
                problems.AddRange(await LookAtAsync(Page.Locator("#events-search"), "scenario-events-search", theme));
            }
        }

        await Page.Locator("#events-search-words").FillAsync("zzqx no such event");
        await Page.Locator("#events-search-go").ClickAsync();
        await Expect(Page.GetByText("Nothing matches")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.Locator("#events-search-clear")).ToBeVisibleAsync();
        NothingWrong(problems);
    }

    [Test]
    public async Task The_event_page_gives_its_dates_as_a_calendar_file()
    {
        var (_, page) = await TheEventAsync();
        await Page.GotoAsync(page);
        var link = Page.Locator("#event-calendar-file");
        await Expect(link).ToBeVisibleAsync(new() { Timeout = 30_000 });

        var href = await link.GetAttributeAsync("href");
        var file = await Page.APIRequest.GetAsync($"{BaseUrl}{href}");
        Assert.That(file.Status, Is.EqualTo(200), $"{href} answered {file.Status}.");
        var text = await file.TextAsync();
        Assert.That(text, Does.StartWith("BEGIN:VCALENDAR"));
        Assert.That(text, Does.Contain("BEGIN:VEVENT"));
    }

    // ── at the door and after ────────────────────────────────────────────────

    [Test]
    public async Task Whos_at_the_classes_fits_every_screen_and_its_switches_are_big_enough()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        var problems = new List<string>();

        foreach (var theme in new[] { "light", "dark" })
        {
            await UseThemeAsync(theme);
            foreach (var (width, height) in Widths)
            {
                await Page.SetViewportSizeAsync(width, height);
                await Page.GotoAsync(EventPage("/door"));
                await Page.Locator("#door-classes-link").ClickAsync(new() { Timeout = 30_000 });
                var some = Page.Locator(".door-class, #classes-none");
                await Expect(some.First).ToBeVisibleAsync(new() { Timeout = 30_000 });
                problems.AddRange(await LookAtAsync(some.First, "scenario-door-classes", theme));
            }
        }

        var firstSwitch = Page.Locator(".door-class input[type=checkbox]").First;
        if (await firstSwitch.CountAsync() > 0)
            problems.AddRange(await TouchTargetAsync(firstSwitch, "door-class-switch", "dark"));
        NothingWrong(problems);
    }

    [Test]
    public async Task Who_came_lists_the_parties_with_a_switch_each_on_every_screen()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync(EventPage("/after"));
        await Expect(Page.Locator("#after-save")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        if (await Page.Locator("#after-who-came").CountAsync() == 0)
            Assert.Ignore("The seeded weekend has no confirmed party to mark.");

        var problems = new List<string>();
        foreach (var theme in new[] { "light", "dark" })
        {
            await UseThemeAsync(theme);
            foreach (var (width, height) in Widths)
            {
                await Page.SetViewportSizeAsync(width, height);
                await Page.GotoAsync(EventPage("/after"));
                var card = Page.Locator("#after-who-came");
                await Expect(card).ToBeVisibleAsync(new() { Timeout = 30_000 });
                problems.AddRange(await LookAtAsync(card, "scenario-who-came", theme));
            }
        }
        problems.AddRange(await TouchTargetAsync(Page.Locator("#after-who-came input[type=checkbox]").First, "who-came-switch", "dark"));
        NothingWrong(problems);
    }

    // ── a guest writing to the organizers ────────────────────────────────────

    [Test]
    public async Task A_guest_writes_to_the_organizers_and_reads_their_answer()
    {
        // The thread needs a booking. The seeded guest already holds a confirmed place at the weekend,
        // and that one is used as it is: withdrawing a confirmed booking only records a request to cancel
        // it, so taking it "back" here would leave the seeded party asking to cancel. Only when he holds
        // none is one made, by asking at the door, and that one is withdrawn afterwards.
        var guest = await ApiAsAsync(ClientEmail, ClientPassword);
        var asked = false;
        if (!await HoldsABookingAsync(guest))
        {
            var on = await _admin.PutAsync($"/api/organizations/{_orgId}/events/{RoomsEventId}/door-tickets",
                                           new() { DataObject = new { on = true } });
            if (on.Status == 400) Assert.Ignore("The seeded weekend sells no day passes, so a guest can't ask at the door.");
            var url = (await OkJsonAsync(on, "turning on tickets at the door")).GetProperty("url").GetString()!;
            var token = url[(url.LastIndexOf('/') + 1)..];
            await OkJsonAsync(await guest.PostAsync($"/api/public/door-tickets/{token}",
                new() { DataObject = new { partySize = 1, firstName = "Daniel", lastName = "Park", phone = "615-555-0100" } }),
                "asking at the door");
            asked = true;
        }

        try
        {
            var (_, page) = await TheEventAsync();
            var words = $"Is there parking at the loading dock? {Guid.NewGuid():N}"[..48];
            var problems = new List<string>();

            await LoginAsync(ClientEmail, ClientPassword);
            await Page.GotoAsync($"{page}#event-organizer-thread");
            await Page.Locator("#organizer-thread-body").FillAsync(words, new() { Timeout = 30_000 });
            problems.AddRange(await TouchTargetAsync(Page.Locator("#organizer-thread-send"), "organizer-thread-send", "light"));
            await Page.Locator("#organizer-thread-send").ClickAsync();
            await Expect(Page.Locator("#organizer-thread-note")).ToContainTextAsync("Sent.", new() { Timeout = 30_000 });

            // The organizers read it on Guest messages and answer.
            var guestId = await MyUserIdAsync(guest);
            await LoginAsync(SuperAdminEmail, SuperAdminPassword);
            foreach (var theme in new[] { "light", "dark" })
            {
                await UseThemeAsync(theme);
                foreach (var (width, height) in Widths)
                {
                    await Page.SetViewportSizeAsync(width, height);
                    await Page.GotoAsync(EventPage($"/guest-messages?guest={guestId}"));
                    await Expect(Page.Locator("#guest-message-thread")).ToContainTextAsync(words, new() { Timeout = 30_000 });
                    // Where the guest stands, for whoever answers: he holds a place, so never "Not signed up".
                    await Expect(Page.Locator("#guest-message-standing")).Not.ToHaveTextAsync("Not signed up");
                    problems.AddRange(await LookAtAsync(Page.Locator("#guest-messages-list"), "scenario-guest-messages", theme,
                                                        Page.Locator("#guest-message-thread")));
                }
            }
            await Page.Locator("#guest-message-answer").FillAsync("Yes, around the back.");
            await Page.Locator("#guest-message-send").ClickAsync();
            await Expect(Page.Locator("#guest-messages-note")).ToContainTextAsync("Sent.", new() { Timeout = 30_000 });

            // The guest's bell and the event's page both have it.
            var bell = await OkJsonAsync(await guest.GetAsync("/api/me/notification-summary"), "reading the guest's bell");
            Assert.That(bell.GetProperty("eventOrganizerReplies").GetProperty("count").GetInt32(), Is.GreaterThan(0));

            // The theme loop above ended in dark; this last look is in light, so the page is put back in light.
            await UseThemeAsync("light");
            await LoginAsync(ClientEmail, ClientPassword);
            await Page.GotoAsync($"{page}#event-organizer-thread");
            await Expect(Page.Locator("#event-organizer-thread [data-from-organizer=true]").Last)
                .ToContainTextAsync("Yes, around the back.", new() { Timeout = 30_000 });
            problems.AddRange(await LookAtAsync(Page.Locator("#event-organizer-thread"), "scenario-organizer-thread", "light"));
            NothingWrong(problems);
        }
        finally
        {
            if (asked) await guest.DeleteAsync($"/api/public/hosted-events/{RoomsEventId}/my-booking");
        }
    }

    /// <summary>Whether the API's caller holds a live booking at the seeded weekend.</summary>
    private static async Task<bool> HoldsABookingAsync(IAPIRequestContext who)
    {
        var mine = await who.GetAsync($"/api/public/hosted-events/{RoomsEventId}/my-booking");
        return mine.Ok && (await mine.TextAsync()).Trim().Length > 2;
    }

    /// <summary>
    /// Ben, 10/23/2026: "People can write questions to the event organizers before the event." Somebody with
    /// no booking asks on the event's page, and the organizers see that they are not signed up.
    /// </summary>
    [Test]
    public async Task Somebody_with_no_booking_can_ask_before_the_event_and_the_organizers_see_they_are_not_signed_up()
    {
        var (_, page) = await TheEventAsync();
        var stranger = await ApiAsAsync(MemberEmail, MemberPassword);
        if (await HoldsABookingAsync(stranger)) Assert.Ignore("The member has a booking at the seeded weekend today.");
        var offered = await stranger.GetAsync($"/api/public/hosted-events/{RoomsEventId}/organizer-thread");
        if (offered.Status == 204) Assert.Ignore("The seeded weekend has started, so questions without a booking are closed.");
        Assert.That(offered.Status, Is.EqualTo(200));

        var words = $"Is it suitable for a twelve-year-old? {Guid.NewGuid():N}"[..52];
        var problems = new List<string>();
        await LoginAsync(MemberEmail, MemberPassword);
        await Page.GotoAsync($"{page}#event-organizer-thread");
        await Page.Locator("#organizer-thread-body").FillAsync(words, new() { Timeout = 30_000 });
        await Page.Locator("#organizer-thread-send").ClickAsync();
        await Expect(Page.Locator("#organizer-thread-note")).ToContainTextAsync("Sent.", new() { Timeout = 30_000 });
        problems.AddRange(await LookAtAsync(Page.Locator("#event-organizer-thread"), "stranger-organizer-thread", "light"));

        var strangerId = await MyUserIdAsync(stranger);
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        foreach (var (width, height) in Widths)
        {
            await Page.SetViewportSizeAsync(width, height);
            await Page.GotoAsync(EventPage($"/guest-messages?guest={strangerId}"));
            await Expect(Page.Locator("#guest-message-thread")).ToContainTextAsync(words, new() { Timeout = 30_000 });
            await Expect(Page.Locator("#guest-message-standing")).ToHaveTextAsync("Not signed up");
            await Expect(Page.Locator($"#guest-messages-list [data-guest='{strangerId}'] [data-testid=guest-standing]"))
                .ToHaveTextAsync("Not signed up");
            problems.AddRange(await LookAtAsync(Page.Locator("#guest-message-thread"), "stranger-guest-messages", "light"));
        }
        NothingWrong(problems);
    }

    /// <summary>Somebody signed out is told they can sign in and ask, before the event starts.</summary>
    [Test]
    public async Task Signed_out_the_event_page_says_you_can_sign_in_and_ask_the_organizers()
    {
        var (_, page) = await TheEventAsync();
        await Page.GotoAsync(page);
        await Expect(Page.Locator("h1").First).ToBeVisibleAsync(new() { Timeout = 30_000 });
        var started = await Page.Locator("#event-organizer-ask-sign-in").CountAsync() == 0;
        if (started) Assert.Ignore("The seeded weekend has started, so the line is rightly not shown.");
        await Expect(Page.Locator("#event-organizer-ask-sign-in a[href^='/login?returnUrl=']")).ToBeVisibleAsync();
        await Expect(Page.Locator("#event-organizer-ask-sign-in")).ToContainTextAsync("You don't need a booking.");
    }

    // ── sessions in the results ──────────────────────────────────────────────

    [Test]
    public async Task A_senders_yes_to_the_results_is_a_switch_that_answers_in_words()
    {
        // A session sent to the weekend, from the seeded guest's confirmed place there. Sent by the API
        // the way the phone sends it; a session filed with an event stays filed, so a second run finds
        // the same one rather than making another.
        var guest = await ApiAsAsync(ClientEmail, ClientPassword);
        if (!await HoldsABookingAsync(guest))
            Assert.Ignore("The seeded guest holds no place at the seeded weekend, so he has nothing to send to it.");
        var bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "field-session-stage.ben"));
        string deviceSessionId;
        using (var zip = new System.IO.Compression.ZipArchive(new MemoryStream(bytes)))
        using (var seal = zip.GetEntry("seal.json")!.Open())
            deviceSessionId = System.Text.Json.JsonDocument.Parse(seal).RootElement.GetProperty("session_id").GetString()!;
        var form = Context.APIRequest.CreateFormData();
        form.Append("file", new FilePayload { Name = "field-session.ben", MimeType = "application/vnd.ishaunted.field-session", Buffer = bytes });
        form.Append("deviceSessionId", deviceSessionId);
        form.Append("hostedEventId", RoomsEventId);
        var sent = await OkJsonAsync(await guest.PostAsync("/api/field-sessions/bundle", new() { Multipart = form }),
                                     "sending a session to the weekend");
        Assert.That(sent.GetProperty("hostedEventId").GetString(), Is.EqualTo(RoomsEventId),
            "The guest's session went somewhere other than the weekend.");
        var sessionId = Guid.Parse(sent.GetProperty("id").GetString()!).ToString("N");

        // The page names itself with a level-three heading, like the other pages of the Media section.
        await LoginAsync(ClientEmail, ClientPassword);
        await Page.GotoAsync($"{BaseUrl}/my-field-sessions");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "My field sessions" })).ToBeVisibleAsync(new() { Timeout = 30_000 });

        var consent = Page.Locator($"#show-with-results-{sessionId}");
        await Expect(consent).ToBeVisibleAsync(new() { Timeout = 30_000 });

        var was = await consent.IsCheckedAsync();
        await consent.ClickAsync();
        await Expect(Page.Locator("[data-testid=consent-error]")).ToHaveCountAsync(0, new() { Timeout = 15_000 });
        await Expect(consent).ToBeCheckedAsync(new() { Checked = !was, Timeout = 15_000 });
        await consent.ClickAsync();
        await Expect(consent).ToBeCheckedAsync(new() { Checked = was, Timeout = 15_000 });

        // The switch is the sender's word, kept: a reload reads it back.
        await Page.ReloadAsync();
        await Expect(Page.Locator($"#show-with-results-{sessionId}")).ToBeCheckedAsync(new() { Checked = was, Timeout = 30_000 });
    }
}
