using System.Text.RegularExpressions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Capture;

/// <summary>
/// Walks a hosted event end to end as every kind of person involved in one, photographing each step for the Hosted
/// Events documents and advertisements (item 235 phase 17d).
/// </summary>
/// <remarks>
/// <para><b>Asked for by Ben on 2026-09-13:</b> <i>"visually run end to end each type of person for the event and just
/// simulate the sending of emails. Use the develop database … Take images with content to use in the PDFs and help
/// documentation."</i></para>
///
/// <para><b>Against the running hosts, not the e2e database.</b> It reads <c>BEN_BASE_URL</c> and <c>BEN_API_URL</c>
/// like every fixture, and is meant for the local hosts on <c>IsHauntedDb_player</c>, where the demo content and the
/// photographs of phase 17c are. The API is started with its mail pointed at a local catcher, so every letter the walk
/// causes is written to <c>BEN_MAIL_CATCHER_DIR</c> and nothing leaves the machine. The helper's invitation is followed
/// from that folder, as the helper would follow it from their inbox.</para>
///
/// <para><b>Opt-in:</b> <c>BEN_CAPTURE_WALK=1</c>. It changes data — a guest's booking is confirmed and left in place,
/// a helper is invited, a letter goes to the guests — which is the point of a walk and the reason it never runs on
/// its own.</para>
///
/// <para><b>Every picture waits for what it is of.</b> Each shot names the element it shows and waits for that element,
/// for the network to go quiet, for every image on the page to finish decoding and for the fonts, and never for a
/// length of time. Signed-in screens are photographed as the page's own content, without the navigation around it: the
/// testing copy's personas belong to many leftover test groups, and none of that is the event.</para>
///
/// <para><b>In story order, stopping at the first failure,</b> because each person picks up where the last left off —
/// a picture taken after a step failed would show the wrong state.</para>
/// </remarks>
[TestFixture]
[Category("Capture")]
[NonParallelizable]
public sealed class HostedEventPersonaWalk : BenTestBase
{
    private const string RoomsEventId = "40000002-0000-0000-0000-000000000002";
    private const string SeatsEventId = "40000002-0000-0000-0000-000000000003";
    private const string VenuePlaceId = "40000002-0000-0000-0000-000000000001";

    private const int Wide = 1440;
    private const int Tall = 900;

    /// <summary>The page's own content, without the header and the navigation.</summary>
    private const string Content = ".app-content";

    private string _orgId = string.Empty;
    private int _shot;

    public override BrowserNewContextOptions ContextOptions() => new()
    {
        ViewportSize = new ViewportSize { Width = Wide, Height = Tall },
        DeviceScaleFactor = 2,
        ColorScheme = ColorScheme.Dark,
    };

    [SetUp]
    public async Task RequireOptIn()
    {
        if (Environment.GetEnvironmentVariable("BEN_CAPTURE_WALK") != "1")
            Assert.Ignore("Set BEN_CAPTURE_WALK=1 (and point the hosts at IsHauntedDb_player) to walk the personas.");

        await Context.AddInitScriptAsync("""
            try {
                localStorage.setItem('layoutSettings', JSON.stringify({ theme: 'dark' }));
                localStorage.setItem('ben-theme', 'dark');
            } catch (e) { }
            """);
    }

    [Test]
    public async Task Walk_every_person_through_a_hosted_event()
    {
        _orgId = await OrgIdBySlugAsync("paranormal365");
        _shot = 0;
        await ArrangeAsync();

        // ── 1. A guest with an account finds the weekend and asks for a room ───
        await LoginAsync(ClientEmail, ClientPassword);

        await GoAsync("/events");
        await ShotAsync("guest-whats-on", Content, subject: Page.GetByText("Thomas House Séance Weekend").First);

        await GoAsync("/o/paranormal365/events/thomas-house-seance-weekend");
        await ShotAsync("guest-event-page", subject: Page.Locator("#hosted-gallery"));
        await ShotAsync("guest-getting-in", "#event-access-notes", pad: 16);

        await Page.Locator("#ask-kind-overnight").ClickAsync();
        foreach (var night in await Page.Locator("#hosted-ask input[type=checkbox]").AllAsync())
            await night.CheckAsync();
        await Page.Locator("#ask-party").FillAsync("2");
        await Page.Locator("#ask-phone").FillAsync("(615) 555-0142");
        await Page.Locator("#ask-note").FillAsync("Our first ghost hunt — could we have a room on the haunted floor?");
        await ShotAsync("guest-asking-for-a-room", "#hosted-ask", pad: 16);

        await ClickUntilAsync(Page.Locator("#ask-send"), Page.Locator("#hosted-booking"));
        await ShotAsync("guest-asked", "#hosted-booking", pad: 16);

        // ── 2. The organizer runs the weekend ─────────────────────────────────
        await LoginAsync(UserEmail, UserPassword);

        await GoAsync($"/organizations/{_orgId}/events/{RoomsEventId}");
        await ShotAsync("organizer-event-at-a-glance", "#event-glance", pad: 16);

        await GoAsync($"/organizations/{_orgId}/events/{RoomsEventId}/bookings");
        await ShotAsync("organizer-booking-board", Content, subject: Page.Locator("#board-queue li", new() { HasTextString = "Daniel Park" }));

        var party = Page.Locator("#board-queue li", new() { HasTextString = "Daniel Park" });
        await ClickUntilAsync(party.Locator("button", new() { HasTextString = "Confirm" }), Page.Locator("#sheet-confirm"));
        var blueRoom = Page.Locator("select[id^='sheet-night-']");
        for (var i = 0; i < await blueRoom.CountAsync(); i++)
            await blueRoom.Nth(i).SelectOptionAsync(new SelectOptionValue { Label = "The Blue Room · sleeps 2" });
        await Page.Locator("#sheet-note").FillAsync("The Blue Room is on the second floor, where the footsteps are. Doors at seven.");
        await ShotAsync("organizer-confirming-a-party", subject: Page.Locator("#sheet-confirm"));

        await Page.Locator("#sheet-confirm").ClickAsync();
        await ShotAsync("organizer-confirmed", Content, subject: Page.Locator("#board-note"));

        await ClickUntilAsync(Page.Locator("#letter-start"), Page.Locator("#letter-subject"));
        await Page.Locator("#letter-subject").FillAsync("Parking for Friday night");
        await Page.Locator("#letter-body").FillAsync(
            "The hotel car park is closed on Friday for resurfacing.\nPark behind the Methodist church on Main Street — it's a two-minute walk, and we'll have a lantern at the corner.");
        await Expect(Page.Locator("#letter-audience")).ToContainTextAsync("This reaches 1 party", new() { Timeout = 15_000 });
        await ShotAsync("organizer-writing-to-guests", "#board-letters", pad: 16);

        await ClickUntilAsync(Page.Locator("#letter-review"), Page.Locator("#letter-confirm"));
        await Page.Locator("#letter-send").ClickAsync();
        await ShotAsync("organizer-letter-sent", "#board-letters", pad: 16, subject: Page.Locator("#letters-sent"));

        await GoAsync($"/organizations/{_orgId}/events/{RoomsEventId}/staff");
        await Page.Locator("#staff-email").FillAsync(MemberEmail);
        await Page.Locator("#staff-role").FillAsync("Front door, Friday and Saturday");
        var invitedAt = DateTime.UtcNow;
        await ClickUntilAsync(Page.Locator("#staff-add"), Page.Locator("#staff-note"));
        await ShotAsync("organizer-inviting-the-door", Content, subject: Page.Locator("#staff-list"));

        await GoAsync($"/organizations/{_orgId}/events/{RoomsEventId}/dietary");
        await ShotAsync("organizer-kitchen", Content, subject: Page.Locator("#kitchen-include-unconfirmed"));

        // ── 3. The guest's pass ────────────────────────────────────────────────
        await LoginAsync(ClientEmail, ClientPassword);

        await GoAsync("/my-events");
        await ShotAsync("guest-my-events", Content, subject: Page.Locator("#my-events"));

        await GoAsync($"/my-events/{RoomsEventId}/pass");
        await ShotAsync("guest-pass", "#pass-card", pad: 24);

        await AtPhoneWidthAsync(async () =>
        {
            await GoAsync($"/my-events/{RoomsEventId}/pass");
            await ShotAsync("guest-pass-phone", subject: Page.Locator("#pass-card"));
        });

        // ── 4. The helper on the door accepts, and finds the party ────────────
        var invite = await MailLinkAsync(MemberEmail, "/helping/", invitedAt, TimeSpan.FromMinutes(7));
        await LoginAsync(MemberEmail, MemberPassword);

        await Page.GotoAsync(invite);
        await ShotAsync("door-invitation", Content, subject: Page.Locator("#helping-accept"));
        await ClickUntilAsync(Page.Locator("#helping-accept"), Page.Locator("#helping-yes"));

        await AtPhoneWidthAsync(async () =>
        {
            await GoAsync($"/organizations/{_orgId}/events/{RoomsEventId}/door");
            await ShotAsync("door-on-a-phone", subject: Page.Locator("#door-search"));
            await Page.Locator("#door-search").FillAsync("Park");
            await ShotAsync("door-finding-a-party", subject: Page.Locator("#door-expected", new() { HasTextString = "Daniel Park" }));
        });

        // ── 5. Somebody with no account picks seats for the evening ───────────
        await LogoutAsync();

        await GoAsync("/o/paranormal365/events/an-evening-of-evidence");
        await ShotAsync("stranger-evening-page", subject: Page.Locator("#hosted-gallery"));

        var free = Page.Locator(".plan__unit[data-state='free']");
        await free.Nth(8).ClickAsync();
        await free.Nth(9).ClickAsync();
        await Page.Locator("#picker-first").FillAsync("Ada");
        await Page.Locator("#picker-last").FillAsync("Whitlock");
        await Page.Locator("#picker-email").FillAsync($"ada.whitlock.{Guid.NewGuid():N}@example.test");
        await Page.Locator("#picker-phone").FillAsync("(615) 555-0147");
        await ShotAsync("stranger-picking-seats", "#hosted-places", pad: 16);

        await Page.Locator("#picker-hold").ClickAsync();
        await ShotAsync("stranger-check-your-email", "#hosted-places", pad: 16, subject: Page.Locator("#picker-emailed"));

        // ── 6. The venue ───────────────────────────────────────────────────────
        await LoginAsync(UserEmail, UserPassword);

        await GoAsync($"/organizations/{_orgId}/venue");
        await ShotAsync("venue-profile", Content, subject: Page.Locator("#venue-requests-link"));

        await GoAsync($"/organizations/{_orgId}/venue-requests");
        await ShotAsync("venue-hosting-requests", Content, subject: Page.GetByRole(AriaRole.Heading, new() { Level = 1 }));

        // ── 7. IsHaunted's own people ─────────────────────────────────────────
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);

        await GoAsync("/admin/dashboard?tab=events");
        await ShotAsync("superadmin-events-dashboard", "#events-dashboard", pad: 16);
        await ShotAsync("superadmin-events-charts", "#events-charts", pad: 16);

        await GoAsync("/admin/events");
        await ShotAsync("superadmin-every-event", Content, subject: Page.Locator(".admin-events-grid"));

    }

    /// <summary>
    /// IsHaunted removing an event, the organizer's appeal, and the answer. Its own story, on its own draft, so it can
    /// be walked without the booking story before it.
    /// </summary>
    [Test]
    public async Task Walk_removing_an_event_and_the_appeal()
    {
        _orgId = await OrgIdBySlugAsync("paranormal365");
        _shot = 27;
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);

        var throwaway = await ADraftToRemoveAsync();
        try
        {
            await GoAsync($"/admin/events/{throwaway}/remove");
            await Page.Locator("#remove-note").FillAsync("Listed a trespass at a closed hospital as a public ghost hunt.");
            await ShotAsync("superadmin-removing-an-event", Content, subject: Page.Locator("#remove-effect"));
            await Page.Locator("#remove-confirm").ClickAsync();
            await Expect(Page.Locator("#remove-done")).ToBeVisibleAsync(new() { Timeout = 20_000 });

            await GoAsync($"/organizations/{_orgId}/events/{throwaway}");
            await Page.Locator("#appeal-message").FillAsync(
                "This is a sanctioned night at the Old Mill with the owner's written permission. We've renamed it and added the owner's letter to the event's files.");
            await ShotAsync("organizer-appealing", "#event-removed", pad: 16);
            await ClickUntilAsync(Page.Locator("#appeal-send"), Page.Locator("#appeal-waiting"));

            await GoAsync("/admin/events");
            var appeal = Page.Locator("#appeals li", new() { HasTextString = "Old Mill Lock-In" });
            await appeal.Locator("textarea").FillAsync("Thanks for the owner's letter. It's back as a draft.");
            await ShotAsync("superadmin-answering-an-appeal", "#appeals", pad: 16, subject: appeal);
            await ClickUntilAsync(appeal.Locator("button", new() { HasTextString = "Uphold" }), Page.Locator("#events-note"));
        }
        finally
        {
            var admin = await ApiAsync(SuperAdminEmail, SuperAdminPassword);
            await admin.PostAsync($"/api/organizations/{_orgId}/events/{throwaway}/archive", new() { DataObject = new { } });
            await admin.DisposeAsync();
        }
    }

    /// <summary>
    /// The public pages at desktop and phone width, signed out — the pictures the advertisements put in a laptop and a
    /// phone. Reads only, so it can run on its own.
    /// </summary>
    [Test]
    public async Task Photograph_the_public_pages()
    {
        _shot = 40;
        await LogoutAsync();

        var pages = new (string Name, string Route, string Subject)[]
        {
            ("tour", "/o/pw-tour-1789070429/tours/church-street-walk", "#tour-slideshow"),
            ("venue", $"/o/paranormal365/venues/{VenuePlaceId}", "#venue-title"),
            ("event", "/o/paranormal365/events/thomas-house-seance-weekend", "#hosted-gallery"),
            ("evening", "/o/paranormal365/events/an-evening-of-evidence", "#hosted-gallery"),
        };

        foreach (var (name, route, subject) in pages)
        {
            await GoAsync(route);
            await ShotAsync($"public-{name}", subject: Page.Locator(subject));

            await AtPhoneWidthAsync(async () =>
            {
                await GoAsync(route);
                await ShotAsync($"public-{name}-phone", subject: Page.Locator(subject));
            });
        }
    }

    // ── the story's starting point ───────────────────────────────────────────

    /// <summary>The guest's earlier asks let go, both events' live bookings released, and the weekend's helpers cleared.</summary>
    private async Task ArrangeAsync()
    {
        var guest = await ApiAsync(ClientEmail, ClientPassword);
        var mine = await guest.GetAsync("/api/public/hosted-events/mine");
        Assert.That(mine.Ok, Is.True, await mine.TextAsync());
        foreach (var booking in (await mine.JsonAsync())!.Value.EnumerateArray())
            await guest.DeleteAsync($"/api/public/hosted-events/{booking.GetProperty("hostedEventId").GetString()}/my-booking");
        await guest.DisposeAsync();

        var admin = await ApiAsync(SuperAdminEmail, SuperAdminPassword);
        foreach (var ev in new[] { RoomsEventId, SeatsEventId })
        {
            var board = await admin.GetAsync($"/api/organizations/{_orgId}/events/{ev}/bookings");
            Assert.That(board.Ok, Is.True, await board.TextAsync());
            foreach (var b in (await board.JsonAsync())!.Value.GetProperty("bookings").EnumerateArray())
                if (b.GetProperty("status").GetInt32() is 0 or 1 or 4)
                    await admin.PostAsync($"/api/organizations/{_orgId}/events/{ev}/bookings/{b.GetProperty("id").GetString()}/cancel",
                        new() { DataObject = new { decisionNote = "Clearing the house before the walk." } });
        }

        var staff = await admin.GetAsync($"/api/organizations/{_orgId}/events/{RoomsEventId}/staff");
        Assert.That(staff.Ok, Is.True, await staff.TextAsync());
        foreach (var person in (await staff.JsonAsync())!.Value.GetProperty("staff").EnumerateArray())
            await admin.DeleteAsync($"/api/organizations/{_orgId}/events/{RoomsEventId}/staff/{person.GetProperty("id").GetString()}");
        await admin.DisposeAsync();
    }

    private async Task<string> ADraftToRemoveAsync()
    {
        var admin = await ApiAsync(SuperAdminEmail, SuperAdminPassword);
        try { return await DraftHostedEventAsync(admin, _orgId, "Old Mill Lock-In", VenuePlaceId); }
        finally { await admin.DisposeAsync(); }
    }

    // ── photographing ────────────────────────────────────────────────────────

    private static string Folder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx"))) dir = dir.Parent;
        Assert.That(dir, Is.Not.Null, "Could not find the repository root.");
        var folder = Path.Combine(dir!.FullName, "docs", "media", "hosted-events", "walk");
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>
    /// Photographs the viewport, or one element with a margin of page around it, once the picture's subject is showing
    /// and the page has settled.
    /// </summary>
    /// <param name="selector">The element to photograph; the viewport when null.</param>
    /// <param name="subject">What the picture is of, when that is not the element itself.</param>
    private async Task ShotAsync(string name, string? selector = null, int pad = 0, ILocator? subject = null)
    {
        var target = selector is null ? null : Page.Locator(selector).First;
        await Expect((subject ?? target)!.First).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await SettledAsync();

        var path = Path.Combine(Folder(), $"{++_shot:00}-{name}.png");
        if (target is null)
        {
            await Page.ScreenshotAsync(new() { Path = path });
        }
        else
        {
            await target.ScrollIntoViewIfNeededAsync();
            var box = await target.BoundingBoxAsync() ?? throw new InvalidOperationException($"{selector} has no box to photograph.");
            var viewport = Page.ViewportSize!;
            var x = Math.Max(0, box.X - pad);
            var y = Math.Max(0, box.Y - pad);
            await Page.ScreenshotAsync(new()
            {
                Path = path,
                Clip = new()
                {
                    X = x, Y = y,
                    Width = Math.Min(viewport.Width - x, box.Width + 2 * pad),
                    Height = Math.Min(viewport.Height - y, box.Height + 2 * pad),
                },
            });
        }

        TestContext.Out.WriteLine($"shot {Path.GetFileName(path)}");
    }

    /// <summary>The network quiet, the circuit's loading markers gone, every image decoded and the fonts in.</summary>
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
    }

    private async Task AtPhoneWidthAsync(Func<Task> atPhoneWidth)
    {
        await Page.SetViewportSizeAsync(390, 844);
        try { await atPhoneWidth(); }
        finally { await Page.SetViewportSizeAsync(Wide, Tall); }
    }

    /// <summary>Opens a page, and closes the "work waiting" banners with their own buttons, as the person would.</summary>
    private async Task GoAsync(string route)
    {
        await Page.GotoAsync($"{BaseUrl}{route}");
        await SettledAsync();

        var banners = Page.Locator(".action-needed-banner .btn-close");
        while (await banners.CountAsync() > 0)
        {
            var before = await banners.CountAsync();
            await banners.First.ClickAsync();
            await Expect(banners).ToHaveCountAsync(before - 1);
        }
    }

    /// <summary>
    /// The link to <paramref name="path"/> in a letter to <paramref name="to"/> written after <paramref name="sentAfter"/>,
    /// from the mail catcher's folder, on the local site. Earlier letters are earlier runs' invitations, which no longer
    /// open anything.
    /// </summary>
    /// <remarks>
    /// The mail sender runs every five minutes, so the letter is waited for. A development API has no public address to
    /// put in front of its links, so a relative link is put on the local site, and an absolute one moved onto it.
    /// </remarks>
    private static async Task<string> MailLinkAsync(string to, string path, DateTime sentAfter, TimeSpan patience)
    {
        var folder = Environment.GetEnvironmentVariable("BEN_MAIL_CATCHER_DIR");
        Assert.That(folder is not null && Directory.Exists(folder), Is.True, "Set BEN_MAIL_CATCHER_DIR to the mail catcher's folder.");

        var link = new Regex("href=\"(?<url>[^\"]*" + Regex.Escape(path) + "[^\"]*)\"");
        var until = DateTime.UtcNow + patience;
        while (DateTime.UtcNow < until)
        {
            foreach (var file in new DirectoryInfo(folder!).GetFiles("*.html").Where(f => f.LastWriteTimeUtc >= sentAfter)
                         .OrderByDescending(f => f.LastWriteTimeUtc))
            {
                var text = await File.ReadAllTextAsync(file.FullName);
                if (!text.Contains(to, StringComparison.OrdinalIgnoreCase) || link.Match(text) is not { Success: true } m) continue;

                var url = System.Net.WebUtility.HtmlDecode(m.Groups["url"].Value);
                return Uri.TryCreate(url, UriKind.Absolute, out var absolute) ? $"{BaseUrl}{absolute.PathAndQuery}" : $"{BaseUrl}{url}";
            }
            await Task.Delay(TimeSpan.FromSeconds(10));
        }

        Assert.Fail($"No letter to {to} with a {path} link reached the mail catcher within {patience.TotalMinutes} minutes.");
        return string.Empty;
    }

    private async Task<IAPIRequestContext> ApiAsync(string email, string password)
    {
        var anonymous = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        var login = await anonymous.PostAsync("/login", new() { DataObject = new { email, password } });
        Assert.That(login.Ok, Is.True, $"{email} could not sign in: {await login.TextAsync()}");
        var token = (await login.JsonAsync())!.Value.GetProperty("accessToken").GetString();
        await anonymous.DisposeAsync();

        return await Playwright.APIRequest.NewContextAsync(new()
        {
            BaseURL = ApiUrl,
            ExtraHTTPHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" },
        });
    }
}
