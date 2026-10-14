using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// A Viewer reads a group's calendar and messages and changes neither (Ben, 2026-09-14: "make viewers read-only").
/// </summary>
/// <remarks>
/// Victor is the seeded Viewer of Paranormal365. The UI test pass found him offered New event, the drag and the ✕ on the
/// group's public events, and Compose on its messages, and the server accepted all of it.
/// </remarks>
[TestFixture]
[Category("ViewerSeat")]
public class ViewerSeatTests : BenTestBase
{
    [Test]
    public async Task A_viewer_reads_the_calendar_without_being_offered_changes()
    {
        await SkipIfFeatureOffAsync("features.events");
        await LoginAsync(ViewerEmail, ViewerPassword);
        var orgId = await OrgIdBySlugAsync("paranormal365");
        await Page.GotoAsync($"{BaseUrl}/organizations/{orgId}/calendar");
        await WaitForTheCircuitAsync();

        await Expect(Page.Locator("#calendar-viewer-note")).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Expect(Page.Locator("#calendar-new-event")).ToHaveCountAsync(0);
        // Telerik 15's toolbar has a New Event of its own by default; the calendar declares its tools
        // so nobody gets one, and a viewer least of all (10/14/2026). Its ✕ on appointments is gone
        // for everybody, so the place a delete could be is our editor's — never open for a viewer.
        await Expect(Page.Locator(".k-scheduler-toolbar").GetByRole(AriaRole.Button, new() { NameString = "New Event" }))
            .ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("calendar-editor-delete")).ToHaveCountAsync(0);
    }

    [Test]
    public async Task A_viewer_reads_the_groups_messages_without_a_compose_button()
    {
        await LoginAsync(ViewerEmail, ViewerPassword);
        var orgId = await OrgIdBySlugAsync("paranormal365");
        await Page.GotoAsync($"{BaseUrl}/organizations/{orgId}?tab=messages");
        await WaitForTheCircuitAsync();

        await Expect(Page.Locator("#messages-viewer-note")).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Expect(Main.GetByRole(AriaRole.Button, new() { NameString = "Compose" })).ToHaveCountAsync(0);
    }
}
