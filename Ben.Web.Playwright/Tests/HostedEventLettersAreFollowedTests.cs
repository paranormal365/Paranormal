using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The hosted-event letters are read from the outbox and used the way their readers use them:
/// the pass a confirmed guest carries to the door, a host's invitation, and a helper's.
/// </summary>
/// <remarks>
/// <para><b>Why.</b> The 2026-09-23 audit of letters that carry something a person has to act on
/// found these three could never be followed by a browser test, because each was skipped outright
/// when the site had no SMTP set up — and the test hosts never set it up. So the pass letter, the
/// one thing a guest shows at a door, had never been opened by anything but a unit test, and
/// <see cref="EventStaffTests"/> said in so many words that the helper's letter "cannot be
/// followed here". They are queued regardless now, and wait in the outbox like every other
/// letter.</para>
///
/// <para><b>Everything the recipient uses comes out of the letter</b>
/// (<see cref="BenTestBase.TryLetterFromTheOutboxAsync"/>), never from an API reply. A link the
/// letter never carried, or carried wrong, is precisely the failure these exist to catch.</para>
///
/// <para><b>Every address is new to this run</b>, so the letter found is the one this test caused,
/// and every person who accepts something is a stranger with no account — the case these letters
/// are written for. The host's side is driven through the API as the SuperAdmin, as the other
/// hosted-event fixtures do; the recipient's side is the browser.</para>
/// </remarks>
[TestFixture]
[Category("Mail")]
[Category("HostedEvents")]
public class HostedEventLettersAreFollowedTests : BenTestBase
{
    /// <summary>The seeded weekend in the rooms — sells day passes, so a host may invite by email.</summary>
    private const string RoomsEventId = "40000002-0000-0000-0000-000000000002";

    /// <summary>The seeded 260-seat evening — places are picked off the plan.</summary>
    private const string SeatsEventId = "40000002-0000-0000-0000-000000000003";

    /// <summary>
    /// The pass's picture link, as <c>HostedEventBookingController.PassImageUrl</c> builds it. Hex,
    /// because that is what <c>EventPasses.NewToken</c> issues.
    /// </summary>
    private static readonly Regex PassLink = new(@"/api/public/event-passes/(?<token>[0-9A-Fa-f]+)\.png");

    /// <summary>The pass drawn into the letter itself, so a mail client that blocks pictures still shows it.</summary>
    private static readonly Regex InlinePass = new(@"src=""data:image/png;base64,(?<png>[A-Za-z0-9+/=]+)""");

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static string Unique => Guid.NewGuid().ToString("N")[..8];

    private IAPIRequestContext _api = null!;
    private string _orgId = string.Empty;

    // What each test left behind, put back in teardown so the seeded events stay as they were found.
    private readonly List<(string EventId, string BookingId)> _bookings = [];
    private readonly List<(string EventId, string StaffId)> _staff = [];

    [SetUp]
    public async Task OpenTheApi()
    {
        _api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        _orgId = await OrgIdBySlugAsync("paranormal365");
    }

    [TearDown]
    public async Task PutTheEventsBack()
    {
        try
        {
            var admin = await SignedInAsync(SuperAdminEmail, SuperAdminPassword);

            // Released as the venue would. A confirmed seat left behind is one the next run's
            // guest cannot pick.
            foreach (var (eventId, bookingId) in _bookings)
                await admin.PostAsync(
                    $"/api/organizations/{_orgId}/events/{eventId}/bookings/{bookingId}/cancel",
                    new() { DataObject = new { decisionNote = "Clearing up after a test." } });

            foreach (var (eventId, staffId) in _staff)
                await admin.DeleteAsync($"/api/organizations/{_orgId}/events/{eventId}/staff/{staffId}");

            await admin.DisposeAsync();
        }
        finally
        {
            await _api.DisposeAsync();
        }
    }

    // ── the pass, in the venue's yes ─────────────────────────────────────────

    /// <summary>
    /// The letter a guest gets when the venue says yes is the one that carries the pass — and
    /// until today it was never written on a site with no mail server, so no test had opened it.
    /// </summary>
    [Test]
    [Description("A confirmed guest's letter carries the pass, its picture link opens, and it is the pass the guest's own page shows.")]
    public async Task A_confirmed_guest_gets_a_pass_that_opens()
    {
        var email = $"passholder{Unique}@example.com";
        var password = NewTestPassword();
        await MakeAccountAsync(email, "Pass Holder", password);
        await MarkOnboardedAsync(email, password);

        var admin = await SignedInAsync(SuperAdminEmail, SuperAdminPassword);
        await admin.PostAsync($"/api/organizations/{_orgId}/events/{SeatsEventId}/booking-mode",
            new() { DataObject = new { mode = 1 } });
        var published = await admin.PostAsync($"/api/organizations/{_orgId}/events/{SeatsEventId}/publish",
            new() { DataObject = new { } });
        Assert.That(published.Ok, Is.True, "the evening could not be published: " + await published.TextAsync());

        var guest = await SignedInAsync(email, password);
        var (bookingId, night, seat) = await HoldAFreeSeatAsync(guest);
        await guest.DisposeAsync();
        _bookings.Add((SeatsEventId, bookingId));

        // The host's yes, which is what writes the letter.
        var confirmed = await admin.PostAsync(
            $"/api/organizations/{_orgId}/events/{SeatsEventId}/bookings/{bookingId}/confirm",
            new()
            {
                DataObject = new
                {
                    nights = new[] { new { hostedEventNightId = night, hostedEventLayoutUnitId = seat } },
                    decisionNote = "See you on the night.",
                },
            });
        Assert.That(confirmed.Ok, Is.True, "the venue could not confirm the booking: " + await confirmed.TextAsync());

        var letter = await TryLetterFromTheOutboxAsync(email, html => PassLink.IsMatch(Decoded(html)));
        Assert.That(letter, Is.Not.Null,
            $"No letter to {email} carrying a pass reached the outbox. Confirming a booking should queue "
          + "the decision letter with the pass in it, mail server or not.");

        var text = Decoded(letter!);
        var token = PassLink.Match(text).Groups["token"].Value;

        // Drawn into the letter, because a mail client that blocks linked pictures is the common
        // case and a guest whose pass did not load has no pass.
        var inline = InlinePass.Match(text);
        Assert.That(inline.Success, Is.True, "The letter links to the pass but does not carry it inline.");
        Assert.That(Convert.FromBase64String(inline.Groups["png"].Value).Take(8), Is.EqualTo(PngSignature),
            "The picture drawn into the letter is not a PNG.");

        // The link is followed against the API host: the harness serves it on its own port, where
        // a deployed site puts both behind one origin. What is proved is that the path the letter
        // carries answers, anonymously — the way a mail client asks for it.
        var image = await _api.GetAsync($"/api/public/event-passes/{token}.png");
        Assert.That(image.Status, Is.EqualTo(200), "The pass link in the letter does not open: " + await image.TextAsync());
        Assert.That(image.Headers.TryGetValue("content-type", out var type) ? type : "", Does.StartWith("image/png"),
            "The pass link in the letter does not answer with a picture.");
        Assert.That((await image.BodyAsync()).Take(8), Is.EqualTo(PngSignature),
            "The pass link in the letter says image/png and is not one.");

        // The pass in the letter is the pass on the guest's own screen. A door that scans one and
        // a guest who reads out the other would be two different tickets.
        await LoginAsync(email, password);
        await Page.GotoAsync($"{BaseUrl}/my-events/{SeatsEventId}/pass");
        await WaitUntilLoadedAsync();
        await Expect(Page.Locator("#pass-code")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        var shortCode = (await Page.Locator("#pass-code").InnerTextAsync()).Trim();
        Assert.That(token.EndsWith(shortCode, StringComparison.OrdinalIgnoreCase), Is.True,
            $"The pass page reads out {shortCode}, which is not the end of the pass the letter carried.");

        // "Email the pass again" is refused where no mail is set up, and queues nothing. Unlike the
        // confirmation — which waits in the outbox whatever the mail — this button exists only to
        // send the letter now, and a second copy waiting there would tell the host nothing.
        var again = await admin.PostAsync(
            $"/api/organizations/{_orgId}/events/{SeatsEventId}/bookings/{bookingId}/pass/email",
            new() { DataObject = new { } });
        Assert.That(again.Status, Is.EqualTo(409), "Emailing the pass again was not refused: " + await again.TextAsync());
        Assert.That(await again.TextAsync(), Does.Contain("no outgoing mail"));
        await admin.DisposeAsync();

        var letters = await LettersFromTheOutboxAsync(email, html => PassLink.IsMatch(Decoded(html)), atLeast: 1);
        Assert.That(letters, Has.Count.EqualTo(1), "A refused resend still queued a letter.");
    }

    // ── a host's invitation ──────────────────────────────────────────────────

    /// <summary>
    /// A host asks somebody with no account to come for the day. The click is a request the venue
    /// then decides, not a place — the page has to say so, and the link has to work once.
    /// </summary>
    [Test]
    [Description("A host's invitation is accepted from its letter, the venue sees the request, and the link then says it has been used.")]
    public async Task A_host_invitation_is_accepted_from_its_letter()
    {
        await SkipIfFeatureOffAsync("features.events");

        var email = $"invited{Unique}@example.com";

        var admin = await SignedInAsync(SuperAdminEmail, SuperAdminPassword);
        var published = await admin.PostAsync($"/api/organizations/{_orgId}/events/{RoomsEventId}/publish",
            new() { DataObject = new { } });
        Assert.That(published.Ok, Is.True, "the weekend could not be published: " + await published.TextAsync());

        var invited = await admin.PostAsync(
            $"/api/organizations/{_orgId}/events/{RoomsEventId}/bookings/on-behalf/invite",
            new() { DataObject = new { email, displayName = "Invited Guest", partySize = 2 } });
        Assert.That(invited.Ok, Is.True, "the host could not invite by email: " + await invited.TextAsync());

        var link = await LinkFromTheOutboxAsync(email, "/attending/");

        // Signed out, as somebody with no account opening a letter is.
        await Page.GotoAsync($"{BaseUrl}{link}");
        var yes = Page.GetByRole(AriaRole.Button, new() { Name = "Yes, I'm coming" });
        await Expect(yes).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.GetByText($"Confirming as {email}")).ToBeVisibleAsync();

        var asked = Page.GetByRole(AriaRole.Heading, new() { Name = "You've asked for a place" });
        await ClickUntilAsync(yes, asked);

        // The party size travelled in the invitation, not asked again at the link; and a hosted
        // event is asked for, so the page must not say the place is theirs.
        var card = Page.Locator(".card").Filter(new() { Has = asked });
        await Expect(card).ToContainTextAsync("2 people");
        await Expect(card).ToContainTextAsync("Nothing is held until they do");

        // An account made by clicking a link has no password; the page says how to get one.
        await Expect(card.GetByRole(AriaRole.Link, new() { Name = "Set a password" })).ToBeVisibleAsync();

        // The venue sees it where they decide things: a request on the board, from this address.
        var board = await admin.GetAsync($"/api/organizations/{_orgId}/events/{RoomsEventId}/bookings");
        Assert.That(board.Ok, Is.True, await board.TextAsync());
        var request = (await board.JsonAsync())!.Value.GetProperty("bookings").EnumerateArray()
            .FirstOrDefault(b => string.Equals(b.GetProperty("leadEmail").GetString(), email, StringComparison.OrdinalIgnoreCase));
        await admin.DisposeAsync();

        Assert.That(request.ValueKind, Is.EqualTo(System.Text.Json.JsonValueKind.Object),
            "Accepting the invitation put nothing on the venue's board.");
        _bookings.Add((RoomsEventId, request.GetProperty("id").GetString()!));
        Assert.That(request.GetProperty("status").GetInt32(), Is.EqualTo(0),
            "The accepted invitation should wait on the venue as a request (Requested = 0).");
        Assert.That(request.GetProperty("partySize").GetInt32(), Is.EqualTo(2));

        // The link works once: forwarded on, it must not ask for another place in their name.
        await Page.GotoAsync($"{BaseUrl}{link}");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "That link doesn't work" }))
            .ToBeVisibleAsync(new() { Timeout = 30_000 });
    }

    // ── a helper's invitation ────────────────────────────────────────────────

    /// <summary>
    /// The letter <see cref="EventStaffTests"/> said could not be followed: a venue asks a
    /// stranger to help at one event, and nothing is granted until they say yes.
    /// </summary>
    [Test]
    [Description("A helper invited by address says yes from the letter, the page says they are helping, and the venue sees them accepted.")]
    public async Task A_helper_says_yes_from_the_letter()
    {
        var email = $"steward{Unique}@example.com";
        var name = $"Steward {Unique}";

        var admin = await SignedInAsync(SuperAdminEmail, SuperAdminPassword);
        var staffUrl = $"/api/organizations/{_orgId}/events/{RoomsEventId}/staff";

        var added = await admin.PutAsync(staffUrl, new()
        {
            DataObject = new { email, displayName = name, roleLabel = "Door", runsTheDoor = true },
        });
        Assert.That(added.Ok, Is.True, "the helper could not be added: " + await added.TextAsync());

        var row = (await added.JsonAsync())!.Value.GetProperty("staff").EnumerateArray()
            .First(s => string.Equals(s.GetProperty("email").GetString(), email, StringComparison.OrdinalIgnoreCase));
        var staffId = row.GetProperty("id").GetString()!;
        _staff.Add((RoomsEventId, staffId));

        var link = await LinkFromTheOutboxAsync(email, "/helping/");

        await Page.GotoAsync($"{BaseUrl}{link}");
        var ask = Page.Locator("#helping-ask");
        await Expect(ask).ToBeVisibleAsync(new() { Timeout = 30_000 });

        // What they are being trusted with, said before the button — the same words the letter used.
        await Expect(ask).ToContainTextAsync("They have you down as Door");
        await Expect(ask).ToContainTextAsync("scan passes at the door and mark people in");

        await ClickUntilAsync(Page.Locator("#helping-accept"), Page.Locator("#helping-yes"));

        var yes = Page.Locator("#helping-yes");
        await Expect(yes).ToContainTextAsync("You're helping");

        // Accepting made them an account with no password, and the door is behind signing in.
        await Expect(yes.GetByRole(AriaRole.Link, new() { Name = "Set a password" })).ToBeVisibleAsync();

        // The venue's side: the invitation became a helper with an account behind it.
        var list = await admin.GetAsync(staffUrl);
        Assert.That(list.Ok, Is.True, await list.TextAsync());
        var accepted = (await list.JsonAsync())!.Value.GetProperty("staff").EnumerateArray()
            .FirstOrDefault(s => s.GetProperty("id").GetString() == staffId);
        await admin.DisposeAsync();

        Assert.That(accepted.ValueKind, Is.EqualTo(System.Text.Json.JsonValueKind.Object),
            "The helper's row is gone from the venue's list after they accepted.");
        Assert.Multiple(() =>
        {
            Assert.That(accepted.GetProperty("accepted").GetBoolean(), Is.True,
                "The page says they are helping and the venue's list says they have not accepted.");
            Assert.That(accepted.GetProperty("appUserId").ValueKind, Is.Not.EqualTo(System.Text.Json.JsonValueKind.Null),
                "Accepting attached no account, so the row still grants nothing.");
        });

        // Single use: a forwarded letter must not enrol a second person into this job.
        await Page.GotoAsync($"{BaseUrl}{link}");
        await Expect(Page.Locator("#helping-gone")).ToBeVisibleAsync(new() { Timeout = 30_000 });
    }

    // ── plumbing ─────────────────────────────────────────────────────────────

    private static string Decoded(string html) => System.Net.WebUtility.HtmlDecode(html);

    /// <summary>
    /// Holds one seat on the first night that the public plan shows free, as <paramref name="guest"/>.
    /// </summary>
    /// <remarks>
    /// Chosen from the plan rather than clearing the house first, so this does not release other
    /// guests' bookings. From the end, like <see cref="HostedEventEmailPickTests"/>, so a run that
    /// left seats behind does not collide with this one; a few tries, because the plan is read
    /// before the hold and somebody may take a seat in between.
    /// </remarks>
    private async Task<(string BookingId, string Night, string Seat)> HoldAFreeSeatAsync(IAPIRequestContext guest)
    {
        var plan = await _api.GetAsync($"/api/public/hosted-events/{SeatsEventId}/plan");
        Assert.That(plan.Ok, Is.True, "the evening's plan could not be read: " + await plan.TextAsync());
        var json = (await plan.JsonAsync())!.Value;

        var night = json.GetProperty("nights").EnumerateArray().First().GetProperty("id").GetString()!;

        // The plan returns only the squares that are NOT free.
        var taken = json.GetProperty("cells").EnumerateArray()
            .Where(c => c.GetProperty("hostedEventNightId").GetString() == night)
            .Select(c => c.GetProperty("hostedEventLayoutUnitId").GetString())
            .ToHashSet();

        var free = json.GetProperty("units").EnumerateArray()
            .Select(u => u.GetProperty("id").GetString()!)
            .Where(id => !taken.Contains(id))
            .Reverse()
            .Take(3)
            .ToList();
        Assert.That(free, Is.Not.Empty, "the evening has no free seat on its first night to hold");

        var refusal = "";
        foreach (var seat in free)
        {
            var held = await guest.PostAsync($"/api/public/hosted-events/{SeatsEventId}/holds", new()
            {
                DataObject = new
                {
                    nights = new[] { new { hostedEventNightId = night, hostedEventLayoutUnitId = seat } },
                    partySize = 1,
                    // The organizer has to be able to reach whoever holds (slice 11d).
                    firstName = "Pass", lastName = "Holder", phone = "615-555-0100",
                },
            });
            if (held.Status == 200)
                return ((await held.JsonAsync())!.Value.GetProperty("id").GetString()!, night, seat);

            refusal = await held.TextAsync();
        }

        Assert.Fail("the guest could not hold a free seat: " + refusal);
        throw new InvalidOperationException("unreachable — Assert.Fail throws");
    }

    /// <summary>
    /// Every letter to <paramref name="to"/> that <paramref name="matches"/>, newest first, once
    /// there are at least <paramref name="atLeast"/> — or what there is after ten seconds.
    /// </summary>
    /// <remarks>
    /// <see cref="BenTestBase.TryLetterFromTheOutboxAsync"/> answers with the newest letter only,
    /// and "sent again" is a question about how many there are.
    /// </remarks>
    private async Task<List<string>> LettersFromTheOutboxAsync(string to, Func<string, bool> matches, int atLeast)
    {
        var admin = await SignedInAsync(SuperAdminEmail, SuperAdminPassword);
        var found = new List<string>();

        for (var attempt = 0; attempt < 20; attempt++)
        {
            found.Clear();

            var list = await admin.GetAsync("/api/admin/mail/outbox?take=100");
            Assert.That(list.Ok, Is.True, "the outbox could not be read: " + await list.TextAsync());

            foreach (var row in (await list.JsonAsync())!.Value.EnumerateArray())
            {
                if (!string.Equals(row.GetProperty("to").GetString(), to, StringComparison.OrdinalIgnoreCase)) continue;
                if (!row.GetProperty("hasBody").GetBoolean()) continue;

                var body = await admin.GetAsync($"/api/admin/mail/outbox/{row.GetProperty("id").GetString()}/body");
                if (!body.Ok) continue;

                var html = (await body.JsonAsync())!.Value.GetProperty("html").GetString();
                if (html is not null && matches(html)) found.Add(html);
            }

            if (found.Count >= atLeast) break;
            await Task.Delay(500);
        }

        await admin.DisposeAsync();
        return found;
    }

    private async Task<IAPIRequestContext> SignedInAsync(string email, string password)
    {
        var login = await _api.PostAsync("/login", new() { DataObject = new { email, password } });
        Assert.That(login.Ok, Is.True, $"{email} could not sign in: {await login.TextAsync()}");
        var token = (await login.JsonAsync())!.Value.GetProperty("accessToken").GetString();

        return await Playwright.APIRequest.NewContextAsync(new()
        {
            BaseURL = ApiUrl,
            ExtraHTTPHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" },
        });
    }

    /// <summary>A confirmed throwaway account, made by the SuperAdmin, with a password generated for this run.</summary>
    private async Task MakeAccountAsync(string email, string displayName, string password)
    {
        var token = await SuperAdminTokenAsync();
        Assert.That(token, Is.Not.Null, "the SuperAdmin could not sign in to make a test account");

        using var http = new HttpClient { BaseAddress = new Uri(ApiUrl) };
        http.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var made = await http.PostAsJsonAsync("/api/admin/app-users", new
        {
            email, password, displayName, userName = (string?)null,
            isEmailConfirmed = true, isSuperAdmin = false,
        });
        Assert.That(made.IsSuccessStatusCode, Is.True, "the test account was refused: " + await made.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Past the welcome wizard, which a brand-new account is otherwise sent to on sign-in — a
    /// different journey from the one under test.
    /// </summary>
    private async Task MarkOnboardedAsync(string email, string password)
    {
        var who = await SignedInAsync(email, password);
        var done = await who.PostAsync("/api/me/onboarding/complete");
        Assert.That(done.Ok, Is.True, "could not mark the test account onboarded: " + await done.TextAsync());
        await who.DisposeAsync();
    }
}
