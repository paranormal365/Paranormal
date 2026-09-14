using System.Text.Json;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The event's room, the phone's add-photos page and the photo wall (item 235 phase 11).
/// </summary>
/// <remarks>
/// Against the seeded rooms weekend, with the seeded guest given a confirmed place for the run and
/// everything this run posted taken down afterwards — posts through the guest's own "Take down", the
/// place through the host's cancel.
/// </remarks>
[TestFixture]
[Category("HostedEvents")]
public class EventRoomTests : BenTestBase
{
    private const string RoomsEventId = "40000002-0000-0000-0000-000000000002";

    private IAPIRequestContext _api = null!;
    private string _orgId = "";
    private string? _madeBookingId;
    private readonly string _marker = $"pw-room-{Guid.NewGuid():N}"[..16];

    [SetUp]
    public async Task AConfirmedGuest()
    {
        _api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        _orgId = await OrgIdBySlugAsync("paranormal365");

        var guest = await HeadersAsync(ClientEmail, ClientPassword);
        var admin = await HeadersAsync(SuperAdminEmail, SuperAdminPassword);
        if (guest is null || admin is null) Assert.Ignore("The seeded guest or admin cannot sign in on this deployment.");

        var mine = await _api.GetAsync($"/api/public/hosted-events/{RoomsEventId}/my-booking", new() { Headers = guest });
        if (mine.Ok && (await mine.TextAsync()).Length > 2)
        {
            var status = (await mine.JsonAsync())!.Value.GetProperty("status").GetInt32();
            if (status == 1) return;
            await _api.DeleteAsync($"/api/public/hosted-events/{RoomsEventId}/my-booking", new() { Headers = guest });
        }

        var me = (await (await _api.GetAsync("/api/me", new() { Headers = guest })).JsonAsync())!.Value;
        var guestId = me.TryGetProperty("userId", out var id) ? id.GetString() : me.GetProperty("id").GetString();
        var made = await _api.PostAsync($"/api/organizations/{_orgId}/events/{RoomsEventId}/bookings/on-behalf", new()
        {
            Headers = admin,
            DataObject = new { leadAppUserId = guestId, kind = 1, partySize = 1, confirmImmediately = true },
        });
        Assert.That(made.Ok, Is.True, await made.TextAsync());
        _madeBookingId = (await made.JsonAsync())!.Value.GetProperty("id").GetString();
    }

    [TearDown]
    public async Task TakeItAllDown()
    {
        if (await HeadersAsync(ClientEmail, ClientPassword) is { } guest)
        {
            var room = await _api.GetAsync($"/api/public/hosted-events/{RoomsEventId}/room", new() { Headers = guest });
            if (room.Ok)
                foreach (var m in (await room.JsonAsync())!.Value.GetProperty("messages").EnumerateArray())
                    if (m.GetProperty("isMine").GetBoolean())
                        await _api.DeleteAsync($"/api/public/hosted-events/{RoomsEventId}/room/messages/{m.GetProperty("id").GetString()}",
                            new() { Headers = guest });
        }

        if (_madeBookingId is not null && await HeadersAsync(SuperAdminEmail, SuperAdminPassword) is { } admin)
            await _api.PostAsync($"/api/organizations/{_orgId}/events/{RoomsEventId}/bookings/{_madeBookingId}/cancel",
                new() { Headers = admin, DataObject = new { decisionNote = "Room test finished." } });

        await _api.DisposeAsync();
    }

    [Test]
    public async Task A_guest_posts_a_photo_and_the_organizer_sees_it_on_the_wall()
    {
        await LoginAsync(ClientEmail, ClientPassword);
        await Page.GotoAsync($"{BaseUrl}/events/{RoomsEventId}/photos");
        await WaitUntilLoadedAsync();

        await Expect(Page.Locator("label[for=add-photos-input]")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Page.Locator("#add-photos-input").SetInputFilesAsync(new FilePayload
        {
            Name = "stairs.png", MimeType = "image/png", Buffer = TinyPng(),
        });
        await Page.Locator("#add-photos-caption").FillAsync(_marker);

        // The first photo a guest adds at an event waits for them to agree to it being shown (Ben,
        // 2026-09-13). The seeded guest may already have agreed on an earlier run.
        var agree = Page.Locator("#add-photos-agree");
        if (await agree.CountAsync() > 0)
        {
            await Expect(Page.Locator("#add-photos-send-button")).ToBeDisabledAsync();
            await Expect(Page.Locator("#add-photos-notice")).ToContainTextAsync("photo wall or slideshow");
            await agree.CheckAsync();
        }
        await ClickUntilAsync(Page.Locator("#add-photos-send-button"), Page.Locator("#add-photos-done"));

        // The guest is not shown the wall: it is behind the organizer's and venue's accounts.
        await Page.GotoAsync($"{BaseUrl}/events/{RoomsEventId}/wall");
        await WaitUntilLoadedAsync();
        await Expect(Page.Locator("#photo-wall-refused")).ToBeVisibleAsync(new() { Timeout = 30_000 });

        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/events/{RoomsEventId}/wall");
        await WaitUntilLoadedAsync();
        await Expect(Page.Locator("#photo-wall img")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.Locator("#photo-wall")).ToContainTextAsync(_marker, new() { Timeout = 30_000 });
    }

    [Test]
    public async Task The_room_shows_on_the_event_page_and_a_post_can_be_taken_down()
    {
        await LoginAsync(ClientEmail, ClientPassword);
        await OpenTheWeekendAsync();

        await Page.Locator("#room-body").FillAsync(_marker);
        await ClickUntilAsync(Page.Locator("#room-post"), Page.Locator(".room-message", new() { HasTextString = _marker }));

        var post = Page.Locator(".room-message", new() { HasTextString = _marker });
        await post.GetByRole(AriaRole.Button, new() { Name = "Take down" }).ClickAsync();
        await Expect(Page.Locator(".room-message", new() { HasTextString = _marker })).ToHaveCountAsync(0, new() { Timeout = 15_000 });
    }

    [Test]
    public async Task The_add_photos_page_fits_a_phone()
    {
        await LoginAsync(ClientEmail, ClientPassword);
        await Page.SetViewportSizeAsync(375, 812);
        await Page.GotoAsync($"{BaseUrl}/events/{RoomsEventId}/photos");
        await WaitUntilLoadedAsync();

        var choose = Page.Locator("label[for=add-photos-input]");
        await Expect(choose).ToBeInViewportAsync(new() { Timeout = 30_000 });

        var slides = await Page.EvaluateAsync<bool>(
            "document.documentElement.scrollWidth > document.documentElement.clientWidth + 1");
        Assert.That(slides, Is.False, "the add-photos page slides sideways on a phone");
    }

    // ── the plumbing ─────────────────────────────────────────────────────────

    private async Task OpenTheWeekendAsync()
    {
        var ev = await _api.GetAsync($"/api/public/hosted-events/{RoomsEventId}");
        Assert.That(ev.Ok, Is.True, "the seeded rooms weekend is not on the public site");
        var slug = (await ev.JsonAsync())!.Value.GetProperty("urlName").GetString();
        await Page.GotoAsync($"{BaseUrl}/o/paranormal365/events/{slug}");
        await WaitUntilLoadedAsync();
        await Expect(Page.Locator("#room-body")).ToBeVisibleAsync(new() { Timeout = 30_000 });
    }

    private static byte[] TinyPng() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAAFklEQVR4nGP8z8DAwMDAxMDAwMDAAAANHQEDasKb6QAAAABJRU5ErkJggg==");

    private async Task<Dictionary<string, string>?> HeadersAsync(string email, string password)
    {
        var login = await _api.PostAsync("/login", new() { DataObject = new { email, password } });
        if (!login.Ok) return null;
        var token = (await login.JsonAsync())!.Value.GetProperty("accessToken").GetString();
        return new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" };
    }
}
