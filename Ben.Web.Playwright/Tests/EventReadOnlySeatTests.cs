using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// A member who may look at a group's events but not change them is shown them to read, and is offered nothing the
/// server would refuse.
/// </summary>
/// <remarks>
/// <para>Found walking the site after hosted events merged (2026-09-13): a Viewer opened the group's Events page and was
/// given the whole "Add an event" form, and every event's page in full edit mode, although adding or changing an event
/// needs the group's settings permission and every press would have been refused. A form offered and then refused is
/// the shape this site does not ship.</para>
///
/// <para>Victor is a Viewer of Paranormal365 with no roles; Sarah administers it. Neither test changes anything.</para>
/// </remarks>
[TestFixture]
[Category("HostedEvents")]
public class EventReadOnlySeatTests : BenTestBase
{
    private const string RoomsEventId = "40000002-0000-0000-0000-000000000002";

    private string _orgId = string.Empty;

    [SetUp]
    public async Task FindTheGroup() => _orgId = await OrgIdBySlugAsync("paranormal365");

    [Test]
    public async Task A_viewer_reads_the_events_and_is_offered_nothing_to_change()
    {
        await LoginAsync(ViewerEmail, ViewerPassword);

        await Page.GotoAsync($"{BaseUrl}/organizations/{_orgId}/events");
        await WaitUntilLoadedAsync();
        await Expect(Page.Locator("#events-read-only")).ToContainTextAsync("manage the group's settings", new() { Timeout = 30_000 });
        await Expect(Page.Locator("#new-event-name")).ToHaveCountAsync(0);

        await Page.GotoAsync($"{BaseUrl}/organizations/{_orgId}/events/{RoomsEventId}");
        await WaitUntilLoadedAsync();
        await Expect(Page.Locator("#event-read-only")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.Locator("#publish-event, #unpublish-event")).ToHaveCountAsync(0);
        await Expect(Page.Locator("#event-name")).ToBeDisabledAsync();
    }

    [Test]
    public async Task The_group_admin_still_gets_the_form_and_the_controls()
    {
        await LoginAsync(UserEmail, UserPassword);

        await Page.GotoAsync($"{BaseUrl}/organizations/{_orgId}/events");
        await WaitUntilLoadedAsync();
        await Expect(Page.Locator("#new-event-name")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.Locator("#events-read-only")).ToHaveCountAsync(0);

        await Page.GotoAsync($"{BaseUrl}/organizations/{_orgId}/events/{RoomsEventId}");
        await WaitUntilLoadedAsync();
        await Expect(Page.Locator("#event-name")).ToBeEnabledAsync(new() { Timeout = 30_000 });
        await Expect(Page.Locator("#event-read-only")).ToHaveCountAsync(0);
    }
}
