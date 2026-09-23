using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The letters a calendar sends — "confirm you're coming", a guide's "you're signed up", and a
/// walk's welcome with its pass — are read from the outbox and used the way their readers use them.
/// </summary>
/// <remarks>
/// <para><b>Why.</b> Until 2026-09-23 these letters were not queued at all when SMTP was not
/// configured, and the test hosts never configure it — so no browser test could follow one, and
/// nothing noticed. The audit that day moved them into the outbox whatever the mail setup. Each
/// test here reads its letter from the outbox rather than building a link from an API reply,
/// because a link the letter never carried, or carried wrong, is exactly the failure.</para>
///
/// <para><b>Every address is the test's own</b> (a Guid fragment), so the newest letter to it is
/// the one this test caused. Every date is made for the test and taken away afterwards; the
/// walking-tour business in the pass test is registered for it and purged.</para>
/// </remarks>
[TestFixture]
[Category("Mail")]
[NonParallelizable]
public class CalendarLettersAreFollowedTests : BenTestBase
{
    private static string Unique => Guid.NewGuid().ToString("N")[..8];

    private IAPIRequestContext? _api;
    private readonly List<(string OrgId, string EventId)> _dates = [];
    private (string Id, string Name)? _business;

    [SetUp]
    public async Task OpenTheApi()
    {
        await SkipIfFeatureOffAsync("features.events");
        _api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
    }

    [TearDown]
    public async Task TakeEverythingAwayAgain()
    {
        if (_api is null) return;

        if (await SuperAdminTokenAsync() is { } token)
        {
            var admin = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" };

            foreach (var (orgId, eventId) in _dates)
                await _api.DeleteAsync($"/api/organizations/{orgId}/calendar/{eventId}", new() { Headers = admin });

            // Purged, not deleted: the business has a tour, a date and a seat hanging off it, and
            // the ordinary delete refuses in words for exactly that reason.
            if (_business is { } business)
            {
                var purged = await _api.DeleteAsync($"/api/admin/organizations/{business.Id}/purge",
                    new() { Headers = admin, DataObject = new { confirmName = business.Name } });
                if (!purged.Ok)
                    await _api.DeleteAsync($"/api/organizations/{business.Id}", new() { Headers = admin });
            }
        }

        _dates.Clear();
        _business = null;
        await _api.DisposeAsync();
        _api = null;
    }

    // ── 1. a visitor asks to come ────────────────────────────────────────────

    /// <summary>
    /// A signed-out visitor gives an address on a public event's page; the letter's link is what
    /// makes them a guest, and it works once.
    /// </summary>
    [Test]
    [Description("A visitor asks to come by email, confirms from the letter, and the link does not work twice.")]
    public async Task A_visitor_confirms_from_the_letter_and_the_link_works_once()
    {
        var tag = Unique;
        var email = $"coming{tag}@example.com";
        var title = $"Letter night {tag}";

        var owner = await BearerAsync(UserEmail, UserPassword);
        var orgId = await OrgIdBySlugAsync("paranormal365");
        var (eventId, slug) = await CalendarDateAsync(owner, orgId, title, tourId: null, capacity: null);
        _dates.Add((orgId, eventId));

        // Asked on the page a stranger actually finds, signed out.
        await Page.GotoAsync($"{BaseUrl}/o/paranormal365/events/{slug}");
        await AskToComeByEmailAsync(title, email);

        var link = await LinkFromLetterAsync(email, "/attending/", html => html.Contains(title));

        await ConfirmFromTheLinkAsync(link, title, email);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "You're coming", Exact = true }))
            .ToBeVisibleAsync();
        Assert.That(await AcceptedGuestsAsync(owner, orgId, eventId), Is.EqualTo(1),
            "The page said they were coming, and the event's own list does not have them.");

        // The token is cleared in the same save that records them, so a forwarded letter cannot
        // sign anybody else up. The page says so in its own words rather than offering the button.
        await Page.GotoAsync($"{BaseUrl}{link}");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "That link doesn't work" }))
            .ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Expect(Page.GetByText("already been used", new() { Exact = false })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Yes, I'm coming" })).ToHaveCountAsync(0);
    }

    // ── 2. a guide signs somebody up ─────────────────────────────────────────

    /// <summary>
    /// The walk-up at the meeting point: the guide types an address nobody has an account for,
    /// and the letter that address receives is what puts them on the list.
    /// </summary>
    /// <remarks>
    /// Driven through the calendar's own editor, because that one box is where a guide does it: an
    /// address an account has published goes straight on the list, anything else is sent this
    /// letter. The fallback is the thing under test, so the box is what gets typed into.
    /// </remarks>
    [Test]
    [Description("A guide signs up an address with no account, and the letter's link puts them on the list.")]
    public async Task A_guest_a_guide_signed_up_confirms_from_the_letter()
    {
        var tag = Unique;
        var email = $"walkup{tag}@example.com";
        var title = $"Walk-up night {tag}";

        var orgId = await OrgIdBySlugAsync("paranormal365");

        await LoginAsync(UserEmail, UserPassword);
        await Page.GotoAsync($"{BaseUrl}/organizations/{orgId}/calendar");
        await WaitForTheCircuitAsync();
        await WaitUntilLoadedAsync();
        await SkipAnyTourAsync();

        var dialog = Page.Locator(".modal.show");
        const string titleBox = ".modal.show #orgscheduler-title-d7a1";
        const string inviteBox = ".modal.show input[placeholder='Invite someone else by email']";

        await ClickUntilAsync(Page.Locator("#calendar-new-event"), Page.Locator(titleBox));
        await FillAndConfirmAsync(titleBox, title);

        // The invite list is offered only on a private event — a public one is open to everybody
        // by definition. An investigation group's dates start private, but the box is the point,
        // so the checkbox is not left to a default somebody could change.
        var isPublic = dialog.Locator("#ev-public");
        if (await isPublic.IsCheckedAsync()) await isPublic.UncheckAsync();

        await Expect(Page.Locator(inviteBox)).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await FillAndConfirmAsync(inviteBox, email);
        await ClickUntilAsync(
            Page.Locator($"{inviteBox} + button"),
            dialog.Locator(".badge", new() { HasTextString = email }));

        await dialog.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        try
        {
            await Expect(dialog).ToHaveCountAsync(0, new() { Timeout = 20_000 });
        }
        catch (PlaywrightException)
        {
            var refusal = dialog.Locator(".alert-danger");
            Assert.Fail("The event did not save: " + (await refusal.CountAsync() > 0
                ? await refusal.First.InnerTextAsync()
                : "the editor stayed open and said nothing."));
        }

        // A failed invite is reported on the calendar after a successful save, not in the editor.
        await Expect(Page.GetByText("could not be invited", new() { Exact = false })).ToHaveCountAsync(0);

        var owner = await BearerAsync(UserEmail, UserPassword);
        var eventId = await EventIdByTitleAsync(owner, orgId, title);
        _dates.Add((orgId, eventId));

        var link = await LinkFromLetterAsync(email, "/attending/",
            html => html.Contains(title) && html.Contains("signed you up"));

        // The guest is somebody else, on their own phone.
        await LogoutAsync();
        await ConfirmFromTheLinkAsync(link, title, email);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "You're coming", Exact = true }))
            .ToBeVisibleAsync();
        Assert.That(await AcceptedGuestsAsync(owner, orgId, eventId), Is.EqualTo(1),
            "The guest confirmed the guide's link, and the event's own list does not have them.");
    }

    // ── 3. a walk's welcome, with its pass ───────────────────────────────────

    /// <summary>
    /// A stranger asks for a place on a walk, confirms the address, the business approves the
    /// seat — and the welcome that approval sends carries the meeting point and a pass that opens.
    /// </summary>
    /// <remarks>
    /// <para><b>Why this path.</b> The pass is minted in one place only — approving a seat
    /// (<c>OrgCalendarEventController.ApproveSeat</c>) — and the welcome is sent from there. The
    /// other two senders (a confirmed link, a signed-in "I'm coming") do not send it on a tour
    /// date at all: a tour date asks rather than comes, and "here is where to stand" is untrue of
    /// a request nobody has looked at.</para>
    ///
    /// <para>The business is registered here, the same way <see cref="TourSeatTests"/> does: no
    /// seeded group runs a tour, and a tour is what the welcome is written from.</para>
    /// </remarks>
    [Test]
    [Description("An approved seat's welcome carries the meeting point and a pass whose link opens as a PNG.")]
    public async Task An_approved_seat_is_sent_the_walk_and_a_pass_that_opens()
    {
        var tag = Unique;
        var email = $"walker{tag}@example.com";
        var dateTitle = $"Letter walk {tag}";

        var owner = await BearerAsync(UserEmail, UserPassword);
        var (orgId, orgSlug, tourId, tourName) = await AWalkingTourAsync(owner, tag);
        var (eventId, slug) = await CalendarDateAsync(owner, orgId, dateTitle, tourId, capacity: 6);

        // ── the stranger asks, and proves the address ────────────────────────
        await Page.GotoAsync($"{BaseUrl}/o/{orgSlug}/events/{slug}");
        await AskToComeByEmailAsync(dateTitle, email);

        var link = await LinkFromLetterAsync(email, "/attending/", html => html.Contains(dateTitle));

        // Deliberately no assertion on the heading. On a tour date the click only ASKS (item
        // 234), but the confirmation page draws the ordinary "You're coming" card for anything
        // that is not a hosted event — so asserting it would pin a sentence that may be wrong.
        await ConfirmFromTheLinkAsync(link, dateTitle, email);

        // ── the business decides ─────────────────────────────────────────────
        await LoginAsync(UserEmail, UserPassword);
        await Page.GotoAsync($"{BaseUrl}/organizations/{orgId}/tours/{tourId}/dates/{eventId}");
        await WaitForTheCircuitAsync();
        await WaitUntilLoadedAsync();
        await SkipAnyTourAsync();

        // Their row, by the address they gave — the business sees who is asking before it decides.
        var request = Page.Locator("tr", new() { HasTextString = email });
        await Expect(request).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await ClickUntilAsync(
            request.GetByRole(AriaRole.Button, new() { Name = "Approve" }),
            Page.GetByText("Nothing waiting", new() { Exact = false }));

        // ── the letter ───────────────────────────────────────────────────────
        var letter = await TourLetterAsync(email);
        Assert.That(letter, Is.Not.Null,
            $"No tour-sign-up letter to {email} reached the outbox after the seat was approved. It is "
          + "queued whether or not mail is set up — look at /admin/mail as the SuperAdmin.");

        var text = System.Net.WebUtility.HtmlDecode(letter!.Value.Html);
        var passLink = Regex.Match(text, @"(?:https?://[^\s""'<>/]+)?/api/public/tour-passes/[0-9A-Fa-f]+\.png");

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain(tourName), "The welcome does not name the walk.");
            Assert.That(text, Does.Contain("1 Printers Alley"), "The welcome does not say where to meet.");
            Assert.That(text, Does.Contain("data:image/png;base64,"),
                "The pass is not drawn into the letter itself — a blocked linked image leaves the guest with nothing to show.");
            Assert.That(passLink.Success, Is.True, "The welcome carries no link to open the pass.");
            Assert.That(letter.Value.Attachments, Is.GreaterThanOrEqualTo(1),
                "The welcome went without the walk as a calendar file.");
        });

        // Opened the way the guest's mail client would. A link written absolute is fetched as
        // written; a relative one (the development API has no site base URL) can only mean the
        // API host, since that is the only host serving /api.
        var passUrl = passLink.Value.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? passLink.Value
            : $"{ApiUrl}{passLink.Value}";
        var pass = await Page.APIRequest.GetAsync(passUrl);
        Assert.That(pass.Status, Is.EqualTo(200), $"The pass link in the letter ({passUrl}) did not open.");
        Assert.That(pass.Headers.TryGetValue("content-type", out var type) ? type : "",
            Does.StartWith("image/png"), "The pass link opened, but not as a picture.");

        var bytes = await pass.BodyAsync();
        Assert.That(bytes.Take(4).ToArray(), Is.EqualTo(new byte[] { 0x89, 0x50, 0x4E, 0x47 }),
            "The pass link answered image/png with something that is not a PNG.");
    }

    // ── plumbing ─────────────────────────────────────────────────────────────

    private async Task<Dictionary<string, string>> BearerAsync(string email, string password)
    {
        var login = await _api!.PostAsync("/login", new() { DataObject = new { email, password } });
        Assert.That(login.Ok, Is.True, $"{email} could not sign in: {await login.TextAsync()}");
        var token = (await login.JsonAsync())!.Value.GetProperty("accessToken").GetString();
        return new() { ["Authorization"] = $"Bearer {token}" };
    }

    /// <summary>A public date ten days out, and the slug its public page lives at.</summary>
    private async Task<(string Id, string Slug)> CalendarDateAsync(
        Dictionary<string, string> owner, string orgId, string title, string? tourId, int? capacity)
    {
        // Well ahead, so sign-ups are open for the whole test whatever the clock says.
        var start = DateTime.UtcNow.AddDays(10);
        var made = await _api!.PostAsync($"/api/organizations/{orgId}/calendar", new()
        {
            Headers = owner,
            DataObject = new
            {
                title, startDateTime = start, endDateTime = start.AddMinutes(90),
                isAllDay = false, isPublic = true, tourId, attendeeCapacity = capacity,
            },
        });
        Assert.That(made.Ok, Is.True, "the date was refused: " + await made.TextAsync());

        // The slug from the record just written, not the public list — a brand-new business is
        // not in that list yet (TourSeatTests found this the hard way).
        var created = (await made.JsonAsync())!.Value;
        return (created.GetProperty("id").GetString()!, created.GetProperty("urlName").GetString()!);
    }

    /// <summary>
    /// A walking-tour business of Sarah's, with a meeting point and one tour — made through the
    /// same endpoints the Start a Group wizard and the tour editor use.
    /// </summary>
    private async Task<(string OrgId, string OrgSlug, string TourId, string TourName)> AWalkingTourAsync(
        Dictionary<string, string> owner, string tag)
    {
        var name = $"Playwright Letter Walks {tag}";
        var slug = $"pw-letters-{tag}";
        var register = await _api!.PostAsync("/api/security/organizations/register", new()
        {
            Headers = owner,
            DataObject = new { name, urlName = slug, kind = 1 },   // GhostWalkingTour
        });
        Assert.That(register.Ok, Is.True, "the walking-tour business was refused: " + await register.TextAsync());
        var orgId = (await register.JsonAsync())!.Value.GetProperty("organizationId").GetString()!;
        _business = (orgId, name);

        // The address type is a per-site lookup row, and the FK refuses an invented id.
        var types = await _api.GetAsync("/api/organization-address-types", new() { Headers = owner });
        Assert.That(types.Ok, Is.True, await types.TextAsync());
        var typeId = (await types.JsonAsync())!.Value.EnumerateArray().First().GetProperty("id").GetString();

        var address = await _api.PostAsync($"/api/organizations/{orgId}/addresses", new()
        {
            Headers = owner,
            DataObject = new
            {
                organizationAddressTypeId = typeId,
                streetAddress1 = "1 Printers Alley", city = "Nashville", state = "TN",
                zipCode = "37201", country = "US", isPrimary = true,
            },
        });
        Assert.That(address.Ok, Is.True, await address.TextAsync());
        var addressId = (await address.JsonAsync())!.Value.GetProperty("id").GetString();

        var tourName = $"Letter Walk {tag}";
        var tour = await _api.PostAsync($"/api/organizations/{orgId}/tours", new()
        {
            Headers = owner,
            DataObject = new
            {
                name = tourName, description = "<p>A walk.</p>",
                startOrganizationAddressId = addressId, durationMinutes = 90,
                defaultCapacity = 6, timeZoneId = "America/Chicago",
            },
        });
        Assert.That(tour.Ok, Is.True, "the tour was refused: " + await tour.TextAsync());
        var tourId = (await tour.JsonAsync())!.Value.GetProperty("id").GetString()!;

        return (orgId, slug, tourId, tourName);
    }

    /// <summary>Gives an address in a public event page's "Coming along?" box, signed out.</summary>
    private async Task AskToComeByEmailAsync(string title, string email)
    {
        await WaitForTheCircuitAsync();
        await Expect(Page.GetByText(title).First).ToBeVisibleAsync(new() { Timeout = 20_000 });

        var act = Page.Locator("#event-act");
        var box = act.Locator("input[type='email']");
        var send = act.GetByRole(AriaRole.Button, new() { Name = "Send me the link" });
        await Expect(box).ToBeVisibleAsync(new() { Timeout = 20_000 });

        // The button is enabled by the field's @oninput, so it is the proof the address reached
        // the component — a value typed before the circuit took over is erased, not merely ignored.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await box.FillAsync(email);
            try
            {
                await Expect(send).ToBeEnabledAsync(new() { Timeout = 2_000 });
                break;
            }
            catch (PlaywrightException) { }
        }

        await ClickUntilAsync(send, act.GetByText("Check your email"));
    }

    /// <summary>Opens the letter's link, checks it is about this event and this address, and says yes.</summary>
    private async Task ConfirmFromTheLinkAsync(string link, string title, string email)
    {
        await Page.GotoAsync($"{BaseUrl}{link}");
        await WaitForTheCircuitAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Confirm you're coming" }))
            .ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Expect(Page.GetByText(title).First).ToBeVisibleAsync();
        await Expect(Page.GetByText($"Confirming as {email}.")).ToBeVisibleAsync();

        // A click, not a page load, confirms — a prefetching mail client must not sign anybody up.
        // The sentence about the account is drawn only on the confirmed card.
        var confirmed = Page.GetByText("We've made you an account with this email address", new() { Exact = false });
        var refused = Page.Locator(".alert-danger");
        await ClickUntilAsync(
            Page.GetByRole(AriaRole.Button, new() { Name = "Yes, I'm coming" }),
            confirmed.Or(refused));

        if (await refused.CountAsync() > 0)
            Assert.Fail("Confirming from the letter was refused: " + await refused.First.InnerTextAsync());
    }

    /// <summary>How many on the event's own list have said yes — the organiser's view of the outcome.</summary>
    private async Task<int> AcceptedGuestsAsync(Dictionary<string, string> owner, string orgId, string eventId)
    {
        var got = await _api!.GetAsync($"/api/organizations/{orgId}/calendar/{eventId}/attendees", new() { Headers = owner });
        Assert.That(got.Ok, Is.True, "the event's list could not be read: " + await got.TextAsync());

        // Accepted = 1. Read either way it may be written, so a serializer change is not a failure here.
        return (await got.JsonAsync())!.Value.EnumerateArray().Count(a =>
            a.GetProperty("rsvpStatus") is var status
            && (status.ValueKind == JsonValueKind.Number
                    ? status.GetInt32() == 1
                    : string.Equals(status.GetString(), "Accepted", StringComparison.OrdinalIgnoreCase)));
    }

    private async Task<string> EventIdByTitleAsync(Dictionary<string, string> owner, string orgId, string title)
    {
        var list = await _api!.GetAsync($"/api/organizations/{orgId}/calendar", new() { Headers = owner });
        Assert.That(list.Ok, Is.True, await list.TextAsync());

        var found = (await list.JsonAsync())!.Value.EnumerateArray()
            .FirstOrDefault(e => e.GetProperty("title").GetString() == title);
        Assert.That(found.ValueKind, Is.EqualTo(JsonValueKind.Object), $"No event called \"{title}\" was saved.");
        return found.GetProperty("id").GetString()!;
    }

    /// <summary>The link a letter to <paramref name="to"/> carried, from the letter <paramref name="which"/> picks.</summary>
    private async Task<string> LinkFromLetterAsync(string to, string linkPath, Func<string, bool> which)
    {
        var shape = new Regex(Regex.Escape(linkPath) + "[^\"'<>\\s]*");

        var html = await TryLetterFromTheOutboxAsync(to, body =>
            which(System.Net.WebUtility.HtmlDecode(body)) && shape.IsMatch(System.Net.WebUtility.HtmlDecode(body)));
        Assert.That(html, Is.Not.Null,
            $"No letter to {to} carrying a {linkPath} link reached the outbox. It is queued whether or "
          + "not mail is set up — look at /admin/mail as the SuperAdmin.");

        return shape.Match(System.Net.WebUtility.HtmlDecode(html!)).Value;
    }

    /// <summary>
    /// The newest tour-sign-up letter to <paramref name="to"/>, with how many files it carried —
    /// or null after ten seconds of looking.
    /// </summary>
    /// <remarks>
    /// Read by KIND, which <see cref="BenTestBase.TryLetterFromTheOutboxAsync"/> cannot see: the
    /// welcome's words may be a published template's, so its kind is the one stable thing that
    /// says which letter it is. The attachment count is the calendar file.
    /// </remarks>
    private async Task<(string Html, int Attachments)?> TourLetterAsync(string to)
    {
        var token = await SuperAdminTokenAsync();
        Assert.That(token, Is.Not.Null, "the SuperAdmin reads the outbox, and could not sign in");
        var admin = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" };

        for (var attempt = 0; attempt < 20; attempt++)
        {
            var list = await _api!.GetAsync("/api/admin/mail/outbox?kind=tour-sign-up&take=100", new() { Headers = admin });
            Assert.That(list.Ok, Is.True, "the outbox could not be read: " + await list.TextAsync());

            foreach (var row in (await list.JsonAsync())!.Value.EnumerateArray())
            {
                if (!string.Equals(row.GetProperty("to").GetString(), to, StringComparison.OrdinalIgnoreCase)) continue;
                if (!row.GetProperty("hasBody").GetBoolean()) continue;

                var body = await _api.GetAsync(
                    $"/api/admin/mail/outbox/{row.GetProperty("id").GetString()}/body", new() { Headers = admin });
                if (!body.Ok) continue;

                if ((await body.JsonAsync())!.Value.GetProperty("html").GetString() is { } html)
                    return (html, row.GetProperty("attachmentCount").GetInt32());
            }

            await Task.Delay(500);
        }

        return null;
    }
}
