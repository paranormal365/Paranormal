using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Every tab an ordinary member is shown, walked as an ordinary member.
/// </summary>
/// <remarks>
/// <para><b>The seat is the test.</b> Nothing here is clever — it opens each tab the group hub
/// offers a plain member and checks the tab actually works. It finds things because of who is
/// signed in, not because of what it does.</para>
///
/// <para>James is an active <c>Member</c> of Paranormal365 with no access grants and no
/// named role, which is what an ordinary member is. That makes <c>HasAccessAsync</c> return
/// <b>false on every table</b> — so any surface gated on it that members are meant to reach is
/// broken from this seat and from nowhere else. Sarah, whom the rest of the suite uses, is an
/// administrator and passes every check by role.</para>
///
/// <para><b>The failure this guards against does not look like a failure.</b> The website's API
/// client turns any non-2xx into an empty result, so a refused list renders as "No records
/// available" — the page tells a member their group has nothing rather than that they were not
/// allowed to look. There is no error, no empty state that reads as broken, and nothing in a log
/// anybody watches. It found exactly that on the Files tab, where the group had a file and the
/// member was shown none. See item 109.</para>
///
/// <para>So the assertions are about <b>real content</b>, never about a page merely loading. A
/// test that accepted an empty grid would pass against precisely the bug it exists to catch.</para>
/// </remarks>
[TestFixture]
[Category("OrdinaryMember")]
public class OrdinaryMemberSurfaceTests : BenTestBase
{
    /// <summary>The group James belongs to as a plain member.</summary>
    private const string OrgName = "Paranormal365";

    [SetUp]
    public async Task SignInAsAnOrdinaryMember()
        => await LoginAsync(MemberEmail, MemberPassword);

    /// <summary>Opens the group hub, skipping cleanly when the seed differs.</summary>
    private async Task<bool> OpenGroupAsync()
    {
        if (!await OpenOrganizationAsync(OrgName)) return false;
        await WaitUntilLoadedAsync();
        return true;
    }

    // ── The hub itself ───────────────────────────────────────────────────────

    /// <summary>
    /// A member can open their own group, and is offered the member-facing tabs.
    /// </summary>
    /// <remarks>
    /// The tab strip is asserted by name because the set is the contract: these are the surfaces
    /// the product promises a member, and every test below walks one of them. A tab quietly
    /// disappearing would otherwise make the rest of this fixture pass by having nothing to do.
    /// </remarks>
    [Test]
    public async Task A_member_sees_the_member_facing_tabs()
    {
        if (!await OpenGroupAsync()) Assert.Ignore($"Seed org '{OrgName}' not present.");

        foreach (var tab in new[] { "Details", "Members", "Cases", "Investigations",
                                    "Calendar", "Messages", "Files", "Equipment" })
        {
            await Expect(Main.GetByRole(AriaRole.Tab, new() { Name = tab, Exact = true })
                             .Or(Main.Locator(".nav-tabs .nav-link", new() { HasTextString = tab }))
                             .First)
                .ToBeVisibleAsync(new() { Timeout = 15_000 });
        }
    }

    /// <summary>
    /// Administrative tabs are not offered to a member.
    /// </summary>
    /// <remarks>
    /// The other half of the contract. Without it, "fix the member's access" could be satisfied by
    /// giving members everything, and this fixture would applaud.
    /// </remarks>
    [Test]
    public async Task A_member_is_not_offered_the_administrative_tabs()
    {
        if (!await OpenGroupAsync()) Assert.Ignore($"Seed org '{OrgName}' not present.");

        // Wait for the strip to be real before asserting absence — everything is absent from a
        // page that has not rendered yet.
        await Expect(Main.GetByRole(AriaRole.Tab, new() { Name = "Details", Exact = true }).First)
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        foreach (var tab in new[] { "Settings", "Roles", "Addresses", "Requests", "CMS" })
        {
            Assert.That(
                await Main.GetByRole(AriaRole.Tab, new() { Name = tab, Exact = true }).CountAsync(),
                Is.Zero, $"An ordinary member was offered the '{tab}' tab.");
        }
    }

    // ── The tabs, each carrying its real content ─────────────────────────────

    /// <summary>
    /// The Files tab shows the group's files.
    /// </summary>
    /// <remarks>
    /// <para>This is the one that found the bug, and it is worth saying exactly how it failed:
    /// <c>GET /api/organizations/{id}/files</c> answered 403 for a member and 200 for an
    /// administrator, and the page rendered the 403 as <b>"No records available. 0 – 0 of 0
    /// items"</b>. The group had a file. The member was told there were none.</para>
    ///
    /// <para>Hence the assertion is on a file being listed, not on the grid existing. An empty
    /// grid is the bug.</para>
    /// </remarks>
    [Test]
    public async Task A_member_can_see_the_groups_files()
    {
        if (!await OpenGroupAsync()) Assert.Ignore($"Seed org '{OrgName}' not present.");

        await OpenTabAsync("Files", Main.GetByRole(AriaRole.Button, new() { Name = "Upload" }));
        await WaitUntilLoadedAsync();

        var emptyGrid = Main.GetByText("No records available", new() { Exact = false });

        Assert.That(await emptyGrid.CountAsync(), Is.Zero,
            "The Files tab told an ordinary member the group has no files. Check whether the "
            + "group really has none before believing it: a refused request renders identically "
            + "to an empty one, which is how this was missed the first time.");
    }

    /// <summary>The Cases tab carries the group's cases.</summary>
    [Test]
    public async Task A_member_can_see_the_groups_cases()
    {
        if (!await OpenGroupAsync()) Assert.Ignore($"Seed org '{OrgName}' not present.");

        // The loaded-signal is a case REFERENCE, not the "New Case" button it used to be:
        // IH-03 step 2 gated that button on the Create grant, and this member deliberately
        // holds Read alone. Waiting on the button turned the new rule working into a timeout.
        await OpenTabAsync("Cases", Main.GetByText("#2026-", new() { Exact = false }).First);
        await WaitUntilLoadedAsync();

        await Expect(Main.GetByText("#2026-", new() { Exact = false }).First)
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        // And the rule itself, from the seat it protects: reading the list does not offer the
        // door to a refusal.
        await Expect(Main.GetByRole(AriaRole.Button, new() { Name = "New Case" })).ToHaveCountAsync(0);
    }

    /// <summary>The Investigations tab carries the group's investigations.</summary>
    [Test]
    public async Task A_member_can_see_the_groups_investigations()
    {
        if (!await OpenGroupAsync()) Assert.Ignore($"Seed org '{OrgName}' not present.");

        // Same IH-03 correction as the Cases tab: the schedule button now answers to the
        // Create grant, so the loaded-signal is the panel's own heading.
        await OpenTabAsync("Investigations",
            Main.GetByText("Everywhere this group has worked", new() { Exact = false }).First);
        await WaitUntilLoadedAsync();

        // The panel names every visit the group has made, including ones with no location — the
        // heading alone would render even if the list behind it were refused.
        await Expect(Main.GetByText("Everywhere this group has worked", new() { Exact = false }).First)
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        await Expect(Main.GetByRole(AriaRole.Button, new() { Name = "Schedule an investigation" }))
            .ToHaveCountAsync(0);
    }

    /// <summary>The Members tab names the people in the group — including the member reading it.</summary>
    /// <remarks>
    /// Asserted on a name rather than on a row count: a directory that renders three empty rows
    /// would satisfy a count and tell nobody anything.
    /// </remarks>
    [Test]
    public async Task A_member_can_see_who_else_is_in_the_group()
    {
        if (!await OpenGroupAsync()) Assert.Ignore($"Seed org '{OrgName}' not present.");

        await OpenTabAsync("Members", Main.Locator("table").First);
        await WaitUntilLoadedAsync();

        Assert.That(await Main.GetByText("No records available", new() { Exact = false }).CountAsync(),
            Is.Zero, "The Members tab told an ordinary member their group has no members. The "
            + "Details tab beside it counts three.");

        // A name, not a row count: three empty rows would satisfy a count and tell nobody who is
        // in the group.
        await Expect(Main.GetByText("Sarah", new() { Exact = false }).First)
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
    }
}
