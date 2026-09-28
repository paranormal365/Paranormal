using System.Text.Json;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright;

/// <summary>
/// Item 252 on the website, as the people in the role-play use it — the planner approving a seat
/// and confirming a booking before the night, the guest seeing the launch in the web feed, and the
/// group finding what was sent up afterwards and playing it back.
/// </summary>
/// <remarks>
/// Run by the role-play orchestrator between the phone steps, one test at a time, with
/// <c>BEN_RP_FILE</c> naming the JSON the setup script wrote (ids of tonight's tour date and
/// event). Skipped without it. Screenshots go to <c>BEN_RP_SHOTS</c> when set.
/// </remarks>
[TestFixture]
[Category("RolePlay")]
public class LeadLaunchRolePlayTests : BenTestBase
{
    private static JsonElement World()
    {
        var file = Environment.GetEnvironmentVariable("BEN_RP_FILE");
        if (string.IsNullOrEmpty(file) || !File.Exists(file)) Assert.Ignore("run by the role-play orchestrator (BEN_RP_FILE)");
        return JsonDocument.Parse(File.ReadAllText(file!)).RootElement;
    }

    private static string Id(JsonElement world, string name) => world.GetProperty(name).GetString()!;

    private async Task ShotAsync(string name)
    {
        if (Environment.GetEnvironmentVariable("BEN_RP_SHOTS") is { Length: > 0 } folder)
        {
            Directory.CreateDirectory(folder);
            await Page.ScreenshotAsync(new() { Path = Path.Combine(folder, $"{name}.png"), FullPage = true });
        }
    }

    [Test]
    public async Task Planner_approves_the_guests_seat_on_the_tour_date()
    {
        var world = World();
        await LoginAsync(UserEmail, UserPassword);
        await Page.GotoAsync($"{BaseUrl}/organizations/{Id(world, "orgId")}/tours/{Id(world, "tourId")}/dates/{Id(world, "tourDateId")}");

        var approve = Page.GetByRole(AriaRole.Button, new() { Name = "Approve" }).First;
        await Expect(approve).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await ShotAsync("web-01-seat-waiting");
        await approve.ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Approve" })).ToHaveCountAsync(0, new() { Timeout = 20_000 });
        await ShotAsync("web-02-seat-approved");
    }

    [Test]
    public async Task Planner_confirms_the_guests_booking_at_the_event()
    {
        var world = World();
        await LoginAsync(UserEmail, UserPassword);
        await Page.GotoAsync($"{BaseUrl}/organizations/{Id(world, "orgId")}/events/{Id(world, "hostedEventId")}/bookings");

        var confirm = Page.Locator($"#confirm-{Id(world, "danielBookingId")}");
        await Expect(confirm).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await ShotAsync("web-03-booking-waiting");
        await confirm.ClickAsync();
        await Page.Locator("#sheet-confirm").ClickAsync();
        await Expect(confirm).ToHaveCountAsync(0, new() { Timeout = 20_000 });
        await ShotAsync("web-04-booking-confirmed");
    }

    [Test]
    public async Task The_guest_sees_the_launch_in_the_web_feed()
    {
        var title = Environment.GetEnvironmentVariable("BEN_RP_EXPECT") ?? "";
        World();
        await LoginAsync(ClientEmail, ClientPassword);
        await Page.GotoAsync($"{BaseUrl}/feed");

        var card = Page.Locator("[data-testid=feed-post-launch]").Filter(new() { HasText = title }).First;
        await Expect(card).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(card.Locator("[data-testid=feed-post-launch-open]")).ToHaveAttributeAsync("href", new System.Text.RegularExpressions.Regex("^ishaunted://field-kit/launch/"));
        await ShotAsync("web-05-feed-launch-card");
    }

    [Test]
    public async Task The_guide_finds_what_was_sent_up_at_the_tour_date_and_plays_it()
    {
        var world = World();
        var expected = int.Parse(Environment.GetEnvironmentVariable("BEN_RP_TOUR_SESSIONS") ?? "1");
        await LoginAsync(MemberEmail, MemberPassword);
        await Page.GotoAsync($"{BaseUrl}/organizations/{Id(world, "orgId")}/tours/{Id(world, "tourId")}/dates/{Id(world, "tourDateId")}");

        var rows = Page.Locator("[data-testid=group-session-row]");
        await Expect(rows).ToHaveCountAsync(expected, new() { Timeout = 30_000 });
        await rows.First.ScrollIntoViewIfNeededAsync();
        await ShotAsync("web-06-tour-sessions");

        await rows.First.Locator("[data-testid=group-session-play]").ClickAsync();
        await Expect(Page.Locator("[data-testid=elapsed]")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await ShotAsync("web-07-tour-session-playback");
    }

    [Test]
    public async Task The_planner_finds_what_was_sent_up_at_the_event()
    {
        var world = World();
        await LoginAsync(UserEmail, UserPassword);
        await Page.GotoAsync($"{BaseUrl}/organizations/{Id(world, "orgId")}/events/{Id(world, "hostedEventId")}#group-field-sessions");

        var rows = Page.Locator("[data-testid=group-session-row]");
        await Expect(rows.First).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await rows.First.ScrollIntoViewIfNeededAsync();
        await ShotAsync("web-08-event-sessions");
    }

    [Test]
    public async Task Another_guest_cannot_see_what_was_sent_up()
    {
        var world = World();
        // Daniel is a guest: the group's list of what everybody sent is not his to read.
        await LoginAsync(ClientEmail, ClientPassword);
        await Page.GotoAsync($"{BaseUrl}/my-field-sessions");
        await Expect(Page.Locator("[data-testid=session-row]").First).ToBeVisibleAsync(new() { Timeout = 30_000 });
        // His own sessions are his; the tour's are marked the group's and have no Delete.
        await Expect(Page.Locator("[data-testid=belongs-to-group]").First).ToBeVisibleAsync();
        await ShotAsync("web-09-guest-own-sessions");
    }
}
