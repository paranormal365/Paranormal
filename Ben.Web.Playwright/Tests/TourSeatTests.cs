using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Asking for a place on a walk, and the business deciding (item 234, Ben 2026-09-10).
/// </summary>
/// <remarks>
/// <para>Ben: <i>"They would not be confirmed until the tour guide or manager approves them meaning
/// they have settled how money will be or has been exchanged."</i> <b>This site never takes the
/// money</b> — this walks the two of them agreeing, which is all the state there is.</para>
///
/// <para>Driven through the PAGES rather than the API, because what is being checked is that a
/// guest is told which of the three things they are looking at. The API-level rules have their own
/// tests; a request that reaches the server and then reads as "you're coming" on screen would pass
/// those and still send somebody to a meeting point with no place held.</para>
///
/// <para>The business is registered through the same endpoint the Start a Group wizard uses and
/// deleted afterwards, so the run leaves nothing behind.</para>
/// </remarks>
[TestFixture]
[Category("TourSeats")]
[NonParallelizable]
public class TourSeatTests : BenTestBase
{
    private IAPIRequestContext _api = null!;
    private string _ownerToken = null!;
    private string? _orgId;
    private string _eventId = null!;
    private string _eventSlug = null!;
    private string _orgSlug = null!;
    private string _tourId = null!;

    /// <summary>Sarah owns the walk; James asks for a place on it.</summary>
    [SetUp]
    public async Task AWalkOnTheCalendar()
    {
        _api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });

        var login = await _api.PostAsync("/login", new()
        { DataObject = new { email = UserEmail, password = UserPassword } });
        if (!login.Ok) Assert.Ignore("The seeded owner seat cannot sign in on this deployment.");
        _ownerToken = (await login.JsonAsync())!.Value.GetProperty("accessToken").GetString()!;
        var authed = new Dictionary<string, string> { ["Authorization"] = $"Bearer {_ownerToken}" };

        var slug = $"pw-seats-{Guid.NewGuid():N}"[..20];
        var register = await _api.PostAsync("/api/security/organizations/register", new()
        {
            Headers = authed,
            DataObject = new { name = "Playwright Seat Walks", urlName = slug, kind = 1 },  // GhostWalkingTour
        });
        Assert.That(register.Ok, Is.True, await register.TextAsync());
        _orgId = (await register.JsonAsync())!.Value.GetProperty("organizationId").GetString();

        // A meeting point, because a tour must start somewhere. The address type is asked for
        // rather than assumed: it is a per-site lookup row, and the FK refuses an invented id.
        var types = await _api.GetAsync("/api/organization-address-types", new() { Headers = authed });
        Assert.That(types.Ok, Is.True, await types.TextAsync());
        var typeId = (await types.JsonAsync())!.Value.EnumerateArray().First().GetProperty("id").GetString();

        var address = await _api.PostAsync($"/api/organizations/{_orgId}/addresses", new()
        {
            Headers = authed,
            DataObject = new
            {
                organizationAddressTypeId = typeId,
                streetAddress1 = "1 Printers Alley", city = "Nashville", state = "TN",
                zipCode = "37201", country = "US", isPrimary = true,
            },
        });
        Assert.That(address.Ok, Is.True, await address.TextAsync());
        var addressId = (await address.JsonAsync())!.Value.GetProperty("id").GetString();

        var tour = await _api.PostAsync($"/api/organizations/{_orgId}/tours", new()
        {
            Headers = authed,
            DataObject = new
            {
                name = "Seat Test Walk", description = "<p>A walk.</p>",
                startOrganizationAddressId = addressId, durationMinutes = 90,
                defaultCapacity = 4, timeZoneId = "America/Chicago",
            },
        });
        Assert.That(tour.Ok, Is.True, await tour.TextAsync());
        _tourId = (await tour.JsonAsync())!.Value.GetProperty("id").GetString()!;

        var start = DateTime.UtcNow.AddDays(9);
        var date = await _api.PostAsync($"/api/organizations/{_orgId}/calendar", new()
        {
            Headers = authed,
            DataObject = new
            {
                title = "Seat test night", startDateTime = start, endDateTime = start.AddMinutes(90),
                isAllDay = false, isPublic = true, tourId = _tourId, attendeeCapacity = 4,
            },
        });
        Assert.That(date.Ok, Is.True, await date.TextAsync());
        var created = (await date.JsonAsync())!.Value;
        _eventId = created.GetProperty("id").GetString()!;

        // The public address of the night, taken from the record that was just written rather
        // than from the public list — a brand-new business is not in that list, and asking it for
        // one returns an empty body that reads as a JSON fault three calls later.
        _eventSlug = created.GetProperty("urlName").GetString()!;
        _orgSlug = slug;
    }

    [TearDown]
    public async Task TakeTheBusinessAwayAgain()
    {
        if (_orgId is not null)
        {
            var admin = await _api.PostAsync("/login", new()
            { DataObject = new { email = SuperAdminEmail, password = SuperAdminPassword } });
            if (admin.Ok)
            {
                var token = (await admin.JsonAsync())!.Value.GetProperty("accessToken").GetString();
                var headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" };

                // PURGE, not the ordinary delete. This business has a tour, a date and sign-ups
                // hanging off it, and the ordinary delete refuses in words for exactly that reason
                // — which would leave one of these behind on every run.
                // The purge asks for the group's name typed back, which is the guard against
                // purging the wrong one.
                var purged = await _api.DeleteAsync(
                    $"/api/admin/organizations/{_orgId}/purge",
                    new() { Headers = headers, DataObject = new { confirmName = "Playwright Seat Walks" } });
                if (!purged.Ok)
                    await _api.DeleteAsync($"/api/organizations/{_orgId}", new() { Headers = headers });
            }
        }
        await _api.DisposeAsync();
    }

    /// <summary>Opens the event's public page, whatever slug the server gave it.</summary>
    private async Task GoToTheWalkAsync()
    {
        await Page.GotoAsync($"{BaseUrl}/o/{_orgSlug}/events/{_eventSlug}");
        await WaitUntilLoadedAsync();

        // Named, so a wrong address fails here rather than three assertions later as "the button
        // is missing" — which is what it looked like the first time this ran.
        await Expect(Page.GetByText("Seat test night").First).ToBeVisibleAsync(new() { Timeout = 20_000 });
    }

    [Test]
    [Description("A place on a walk is asked for, approved, and acknowledged — all on the pages.")]
    public async Task A_seat_is_asked_for_approved_and_acknowledged()
    {
        // ── The guest asks ───────────────────────────────────────────────────
        await LoginAsync(MemberEmail, MemberPassword);
        await GoToTheWalkAsync();

        var howMany = Page.Locator("#event-seats");
        await Expect(howMany).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await howMany.SelectOptionAsync("2");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Ask for a place" }).ClickAsync();

        // Asked for, NOT coming. The distinction is the whole item.
        await Expect(Page.GetByText("Asked for").First).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Expect(Page.GetByText("They'll confirm it", new() { Exact = false }).First)
            .ToBeVisibleAsync(new() { Timeout = 10_000 });

        // And the walk is still empty: a request holds nothing.
        Assert.That(await Page.InnerTextAsync("body"), Does.Contain("0 of 4"));

        // ── The business decides ─────────────────────────────────────────────
        await LogoutAsync();
        await LoginAsync(UserEmail, UserPassword);
        await Page.GotoAsync($"{BaseUrl}/organizations/{_orgId}/tours/{_tourId}/dates/{_eventId}");
        await WaitUntilLoadedAsync();

        await Expect(Page.GetByText("Waiting on you (1)").First).ToBeVisibleAsync(new() { Timeout = 20_000 });

        // Opt-in proof for a reviewer, the same shape HomeMapTests uses: BEN_SEAT_SHOT=/some/dir
        // writes the sign-ups page as a PNG.
        if (Environment.GetEnvironmentVariable("BEN_SEAT_SHOT") is { Length: > 0 } shotDir)
        {
            Directory.CreateDirectory(shotDir);
            await Page.ScreenshotAsync(new() { Path = Path.Combine(shotDir, "tour-seats.png") });
        }

        await Page.GetByRole(AriaRole.Button, new() { Name = "Approve" }).First.ClickAsync();
        await Expect(Page.GetByText("Reserved").First).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Expect(Page.GetByText("Waiting on you (0)").First).ToBeVisibleAsync(new() { Timeout = 10_000 });

        // ── The guest sees it, and says so ───────────────────────────────────
        await LogoutAsync();
        await LoginAsync(MemberEmail, MemberPassword);
        await GoToTheWalkAsync();

        await Expect(Page.GetByText("2 places reserved").First).ToBeVisibleAsync(new() { Timeout = 20_000 });

        if (Environment.GetEnvironmentVariable("BEN_SEAT_SHOT") is { Length: > 0 } guestDir)
        {
            Directory.CreateDirectory(guestDir);
            await Page.ScreenshotAsync(new() { Path = Path.Combine(guestDir, "guest-seat.png") });
        }

        // Two places gone from one sign-up, which is the other half of item 234.
        Assert.That(await Page.InnerTextAsync("body"), Does.Contain("2 of 4"));

        await Page.GetByRole(AriaRole.Button, new() { Name = "Got it" }).ClickAsync();
        await Expect(Page.GetByText("You've confirmed you've seen it").First)
            .ToBeVisibleAsync(new() { Timeout = 20_000 });
    }

    [Test]
    [Description("An approval that would overfill the walk is refused in words that name the places left.")]
    public async Task An_approval_that_does_not_fit_is_refused_in_words()
    {
        await LoginAsync(MemberEmail, MemberPassword);
        await GoToTheWalkAsync();

        var howMany = Page.Locator("#event-seats");
        await Expect(howMany).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await howMany.SelectOptionAsync("5");            // a walk that seats four
        await Page.GetByRole(AriaRole.Button, new() { Name = "Ask for a place" }).ClickAsync();
        await Expect(Page.GetByText("Asked for").First).ToBeVisibleAsync(new() { Timeout = 20_000 });

        await LogoutAsync();
        await LoginAsync(UserEmail, UserPassword);
        await Page.GotoAsync($"{BaseUrl}/organizations/{_orgId}/tours/{_tourId}/dates/{_eventId}");
        await WaitUntilLoadedAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Approve" }).First.ClickAsync();

        // The number is the point: "full" alone would not tell them to go back and ask for four.
        var refusal = Page.Locator("#seats-error");
        await Expect(refusal).ToBeVisibleAsync(new() { Timeout = 20_000 });
        Assert.That(await refusal.InnerTextAsync(), Does.Contain("4 places left"));
        Assert.That(await refusal.InnerTextAsync(), Does.Contain("request for 5"));
    }
}
