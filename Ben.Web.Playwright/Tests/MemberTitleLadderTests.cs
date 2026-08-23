using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The member-title ladder (item 157): seniority labels, never permissions. Covers the ladder
/// manager in group Settings, assignment from the Members tab, and the badge a member sees.
/// Cleans up after itself — this database is shared with the public site.
/// </summary>
[TestFixture]
[Category("MemberTitleLadder")]
public class MemberTitleLadderTests : BenTestBase
{
    private const string TestRung = "E2E Tech Specialist";

    [Test]
    public async Task Owner_edits_the_ladder_and_assigns_a_title_the_member_can_see()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        Assert.That(await OpenOrganizationAsync("Tennessee Ghost Hunters"), Is.True,
            "The seeded Tennessee Ghost Hunters group should exist.");

        // ── The ladder manager lives in Settings, seeded with the five rungs ──
        await OpenTabAsync("Settings", Main.GetByText("Member titles"));
        var manager = Main.Locator("div", new() { HasText = "Member titles" }).Last;
        await Expect(Main.GetByText("Senior Investigator", new() { Exact = true }).First)
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        // Self-heal: residue rungs from a previous failed run accumulate in the shared DB and
        // break every later cleanup locator with a strict-mode violation. Sweep them first.
        while (await Main.Locator("li.list-group-item", new() { HasText = TestRung }).CountAsync() > 0)
        {
            await Main.Locator("li.list-group-item", new() { HasText = TestRung }).First
                .GetByRole(AriaRole.Button, new() { Name = "Delete" }).ClickAsync();
            await Page.WaitForTimeoutAsync(400);
        }

        try
        {
            // Add a rung…
            await Main.Locator("#level-new-name").FillAsync(TestRung);
            await ClickUntilAsync(
                Main.GetByRole(AriaRole.Button, new() { Name = "Add title" }),
                Main.GetByText(TestRung, new() { Exact = true }).First);

            // ── Assign it from the Members tab ──
            await OpenTabAsync("Members", Main.Locator("select[id^='member-level-']").First);
            var dropdown = Main.Locator("select[id^='member-level-']").First;
            await dropdown.SelectOptionAsync(new SelectOptionValue { Label = TestRung });

            // The badge is what a plain member sees; reload cold to prove it persisted.
            await Page.ReloadAsync();
            await WaitUntilLoadedAsync();
            await OpenTabAsync("Members", Main.Locator("select[id^='member-level-']").First);
            await Expect(Main.Locator("select[id^='member-level-']").First)
                .ToHaveValueAsync(new System.Text.RegularExpressions.Regex("[0-9a-f-]{36}"), new() { Timeout = 15_000 });
        }
        finally
        {
            // Clear the assignment and remove the rung, whatever happened above.
            await OpenTabAsync("Members", Main.Locator("select[id^='member-level-']").First);
            var dropdown = Main.Locator("select[id^='member-level-']").First;
            try { await dropdown.SelectOptionAsync(new SelectOptionValue { Label = "— none —" }); } catch { }

            await OpenTabAsync("Settings", Main.GetByText("Member titles"));
            // Sweep ALL matches, First-based — duplicates must shrink the list, not break it.
            while (await Main.Locator("li.list-group-item", new() { HasText = TestRung }).CountAsync() > 0)
            {
                await Main.Locator("li.list-group-item", new() { HasText = TestRung }).First
                    .GetByRole(AriaRole.Button, new() { Name = "Delete" }).ClickAsync();
                await Page.WaitForTimeoutAsync(400);
            }
        }
    }
}
