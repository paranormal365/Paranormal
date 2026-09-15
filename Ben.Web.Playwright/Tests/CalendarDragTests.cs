using System.Text.Json;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// A group's calendar, handled with the mouse: an event dragged to another day moves there and the move can be undone,
/// and the ✕ on an event asks before it deletes.
/// </summary>
/// <remarks>
/// UI test pass, 2026-09-14. The calendar let an event be picked up and dropped, and nothing handled the drop: it sprang
/// back and nothing was said (6.3). The ✕ deleted an event — a public one with its sign-ups — on one click, and a server
/// refusal was thrown away (6.4). The event is made through the API as Sarah and removed afterwards.
/// </remarks>
[TestFixture]
[Category("Calendar")]
public class CalendarDragTests : BenTestBase
{
    private IAPIRequestContext _api = null!;
    private Dictionary<string, string> _auth = null!;
    private string _orgId = null!;
    private string? _eventId;
    private string _title = null!;

    [SetUp]
    public async Task AnEventThisWeek()
    {
        await SkipIfFeatureOffAsync("features.events");
        _api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        var login = await _api.PostAsync("/login", new() { DataObject = new { email = UserEmail, password = UserPassword } });
        Assert.That(login.Ok, Is.True, await login.TextAsync());
        _auth = new() { ["Authorization"] = $"Bearer {(await login.JsonAsync())!.Value.GetProperty("accessToken").GetString()}" };
        _orgId = await OrgIdBySlugAsync("paranormal365");

        // Noon today, so it is in the week the calendar opens on and in the middle of its day whatever the zone.
        _title = $"Drag test {Guid.NewGuid():N}"[..20];
        var start = DateTime.UtcNow.Date.AddHours(12);
        var made = await _api.PostAsync($"/api/organizations/{_orgId}/calendar", new()
        {
            Headers = _auth,
            DataObject = new { title = _title, startDateTime = start, endDateTime = start.AddHours(2), isAllDay = false, isPublic = false },
        });
        Assert.That(made.Ok, Is.True, await made.TextAsync());
        _eventId = (await made.JsonAsync())!.Value.GetProperty("id").GetString();
    }

    [TearDown]
    public async Task TakeItAwayAgain()
    {
        if (_eventId is not null) await _api.DeleteAsync($"/api/organizations/{_orgId}/calendar/{_eventId}", new() { Headers = _auth });
        await _api.DisposeAsync();
    }

    private async Task<DateTime?> StartOnServerAsync()
    {
        var got = await _api.GetAsync($"/api/organizations/{_orgId}/calendar/{_eventId}", new() { Headers = _auth });
        if (got.Status == 404) return null;
        Assert.That(got.Ok, Is.True, await got.TextAsync());
        return (await got.JsonAsync())!.Value.GetProperty("startDateTime").GetDateTime().ToUniversalTime();
    }

    private ILocator TheEvent => Page.Locator(".k-event", new() { HasText = _title });

    private async Task OpenTheCalendarAsync()
    {
        await LoginAsync(UserEmail, UserPassword);
        await Page.GotoAsync($"{BaseUrl}/organizations/{_orgId}/calendar");
        await Expect(TheEvent).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await TheEvent.ScrollIntoViewIfNeededAsync();
    }

    [Test]
    public async Task AnEventDraggedToAnotherDay_MovesThere_AndTheMoveCanBeUndone()
    {
        var before = (await StartOnServerAsync())!.Value;
        await OpenTheCalendarAsync();

        // The day beside it: the next one, or the one before on the last day of the week.
        var box = (await TheEvent.BoundingBoxAsync())!;
        var days = await Page.Locator(".k-heading-cell .k-nav-day").EvaluateAllAsync<float[][]>(
            "els => els.map(e => { const r = e.closest('.k-heading-cell').getBoundingClientRect(); return [r.left, r.right]; })");
        var column = Array.FindIndex(days, d => box.X + 4 >= d[0] && box.X + 4 < d[1]);
        Assert.That(column, Is.GreaterThanOrEqualTo(0), "the event is not under any day of the week shown");
        var step = column < days.Length - 1 ? 1 : -1;
        var target = days[column + step];

        await Page.Mouse.MoveAsync(box.X + box.Width / 2, box.Y + Math.Min(10, box.Height / 2));
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync((target[0] + target[1]) / 2, box.Y + Math.Min(10, box.Height / 2), new() { Steps = 12 });
        await Page.Mouse.UpAsync();

        await Expect(Page.Locator("[data-testid=calendar-moved]")).ToContainTextAsync(_title, new() { Timeout = 15_000 });
        Assert.That(await StartOnServerAsync(), Is.EqualTo(before.AddDays(step)), "the drop was not saved one day over, at the same time");

        await Page.Locator("#calendar-move-undo").ClickAsync();
        await Expect(Page.Locator("[data-testid=calendar-moved]")).ToHaveCountAsync(0, new() { Timeout = 15_000 });
        Assert.That(await StartOnServerAsync(), Is.EqualTo(before), "Undo did not put the event back");
    }

    [Test]
    public async Task TheCrossOnAnEvent_AsksFirst_AndCancelKeepsIt()
    {
        await OpenTheCalendarAsync();

        await TheEvent.HoverAsync();
        await TheEvent.Locator(".k-event-delete").ClickAsync();
        await Expect(Page.Locator("[data-testid=calendar-delete-message]")).ToContainTextAsync(_title, new() { Timeout = 10_000 });
        Assert.That(await StartOnServerAsync(), Is.Not.Null, "the event went before the question was answered");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();
        await Expect(Page.Locator("[data-testid=calendar-delete-message]")).ToHaveCountAsync(0);
        Assert.That(await StartOnServerAsync(), Is.Not.Null);

        await TheEvent.HoverAsync();
        await TheEvent.Locator(".k-event-delete").ClickAsync();
        await Page.Locator(".modal.show, [role=dialog]").GetByRole(AriaRole.Button, new() { Name = "Delete", Exact = true }).ClickAsync();
        await Expect(TheEvent).ToHaveCountAsync(0, new() { Timeout = 15_000 });
        Assert.That(await StartOnServerAsync(), Is.Null, "the event is still there after Delete");
        _eventId = null;
    }
}
