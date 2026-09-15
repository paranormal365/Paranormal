using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// What an ordinary member is offered is what the server lets them do (member test pass, 2026-09-14).
/// </summary>
/// <remarks>
/// James is a member of Paranormal365 with no role grants beyond reading cases and investigations: no Files permission,
/// not an admin, not the Belmont case's manager. Each of these screens offered him a door the server then refused — the
/// case tour about editing, the group's Upload and delete log, Edit and Delete on the client's own timeline entry.
/// </remarks>
[TestFixture]
[Category("MemberSeat")]
public class MemberSeatTests : BenTestBase
{
    [Test]
    public async Task A_member_is_not_offered_the_publishing_tour_or_changes_to_other_peoples_timeline_entries()
    {
        await LoginAsync(MemberEmail, MemberPassword);
        Assert.That(await OpenOrgCaseAsync("Paranormal365", "Belmont"), Is.True, "The seeded Belmont case should be reachable.");

        await Expect(Page.Locator("#case-contacts-who-chooses")).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Expect(Page.Locator("#case-tour-launch")).ToHaveCountAsync(0);

        await Page.Locator("#tab-timeline").ClickAsync();
        var clientEntry = Main.Locator(".case-timeline > .card", new() { HasText = "Daniel Park" }).First;
        await Expect(clientEntry).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Expect(clientEntry.Locator("button[title='Edit'], button[title='Delete']")).ToHaveCountAsync(0);
    }

    [Test]
    public async Task A_member_without_the_files_permission_sees_the_groups_files_but_not_its_tools()
    {
        await LoginAsync(MemberEmail, MemberPassword);
        var orgId = await OrgIdBySlugAsync("paranormal365");
        await Page.GotoAsync($"{BaseUrl}/organizations/{orgId}?tab=files");
        await WaitForTheCircuitAsync();

        await Expect(Page.Locator("#org-files-who-adds")).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Expect(Main.GetByRole(AriaRole.Button, new() { NameString = "Upload" })).ToHaveCountAsync(0);
        await Expect(Main.GetByRole(AriaRole.Button, new() { NameString = "Delete Log" })).ToHaveCountAsync(0);
        await Expect(Main.GetByRole(AriaRole.Link, new() { NameString = "← Organizations" })).ToHaveCountAsync(0);
    }
}
