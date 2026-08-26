using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Item 109: the whole org hub, walked from the seat nobody ever sits in — an ordinary member.
/// </summary>
/// <remarks>
/// <para>The suite spent its life signed in as Sarah (administrator, passes every permission
/// check by role) or the SuperAdmin. Three total failures in group messaging were invisible from
/// those seats and caught within minutes of signing in as James. This fixture makes that walk a
/// permanent, repeatable thing rather than a one-off rescue.</para>
///
/// <para>Two mirrored checks. The walk asserts every member-facing tab renders CONTENT rather
/// than a refusal; the gate check asserts the admin-only tabs are absent for James — because a
/// tab appearing for a member is the same class of bug as one refusing them, pointed the other
/// way.</para>
/// </remarks>
[TestFixture]
[Category("MemberWalk")]
public class MemberSurfaceWalkTests : BenTestBase
{
    private const string OrgName = "Paranormal365";

    /// <summary>Text that means a surface refused or broke, in every voice this app uses.</summary>
    private static readonly string[] RefusalMarkers =
    [
        "do not have access",
        "Couldn't load",
        "the server refused",
        "You've been signed out",
        "Something went wrong",
    ];

    private static readonly string[] MemberTabs =
        ["Details", "Members", "Cases", "Investigations", "Calendar", "Messages", "Files", "Equipment"];

    private static readonly string[] AdminOnlyTabs =
        ["Requests", "CMS", "Roles", "Addresses", "Settings"];

    [Test]
    public async Task Every_member_tab_renders_content_not_a_refusal()
    {
        await LoginAsync(MemberEmail, MemberPassword);
        if (!await OpenOrganizationAsync(OrgName)) Assert.Ignore($"No organisation named {OrgName} in this database.");

        foreach (var tab in MemberTabs)
        {
            var handle = Main.GetByRole(AriaRole.Tab, new() { Name = tab, Exact = true });

            // A tab a member is meant to reach must exist at all — its absence is the same
            // failure as a refusal, delivered more quietly.
            await Expect(handle).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await handle.ClickAsync();

            // Let the tab's own fetch land. NetworkIdle proves nothing on Blazor Server, so this
            // waits on the absence of the failure text after the content has had a beat to render.
            await Page.WaitForTimeoutAsync(1_500);

            foreach (var marker in RefusalMarkers)
            {
                var hit = Main.GetByText(marker, new() { Exact = false });
                var hits = await hit.CountAsync();

                // The failure message carries the surrounding card's text, because "a refusal is
                // rendered somewhere on this tab" is only half a bug report — which fetch failed
                // is the other half, and the card usually says.
                var context = hits == 0 ? "" : await hit.First.Locator("xpath=ancestor::div[contains(@class,'card')][1]")
                    .InnerTextAsync(new() { Timeout = 2_000 });

                Assert.That(hits, Is.EqualTo(0),
                    $"The {tab} tab shows \"{marker}\" to an ordinary member of their own group. Card: {context}");
            }
        }
    }

    [Test]
    public async Task Admin_only_tabs_stay_hidden_from_an_ordinary_member()
    {
        await LoginAsync(MemberEmail, MemberPassword);
        if (!await OpenOrganizationAsync(OrgName)) Assert.Ignore($"No organisation named {OrgName} in this database.");

        // Details always renders, so the hub is provably up before asserting absences.
        await Expect(Main.GetByRole(AriaRole.Tab, new() { Name = "Details", Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });

        foreach (var tab in AdminOnlyTabs)
        {
            var count = await Main.GetByRole(AriaRole.Tab, new() { Name = tab, Exact = true }).CountAsync();
            Assert.That(count, Is.EqualTo(0),
                $"The {tab} tab is visible to an ordinary member — it is admin-only.");
        }
    }
}
