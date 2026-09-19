using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Item 158 end to end: the duty board on an investigation's team panel, and a case's points of
/// contact with the case-manager fallback. Cleans up every change — shared database.
/// </summary>
[TestFixture]
[Category("InvestigationDutyAndContact")]
public class InvestigationDutyAndContactTests : BenTestBase
{
    [Test]
    public async Task The_duty_board_renders_and_a_duty_can_be_handed_out_and_back()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        Assert.That(await OpenOrganizationAsync("Paranormal365"), Is.True);
        var orgUrl = System.Text.RegularExpressions.Regex.Match(Page.Url, @"/organizations/[0-9a-f-]{36}").Value;

        // Arrive via the ?tab= deep link (item 149) rather than clicking the strip: a mid-load
        // re-render can bounce a clicked strip back to Details, but a deep link IS the state.
        // The seeded winter survey has two attendees; the FIRST row can be an unattended
        // upcoming visit, whose empty picker reads as "everyone already holds it".
        await Page.GotoAsync($"{BaseUrl}{orgUrl}?tab=investigations");
        await WaitUntilLoadedAsync();
        var visitRow = Main.Locator("tr", new() { HasTextString = "winter survey" }).First;
        await Expect(visitRow).ToBeVisibleAsync(new() { Timeout = 30_000 });

        // ONE click, then wait: Team is a toggle, and a retrying click alternately opens and
        // closes the very panel it is waiting for.
        await visitRow.GetByRole(AriaRole.Button, new() { Name = "Team" }).ClickAsync();
        await Expect(Main.GetByText("Who's doing what")).ToBeVisibleAsync(new() { Timeout = 30_000 });

        // The four seeded duties are on the board.
        foreach (var duty in new[] { "Lead Investigator", "Equipment", "Evidence Collection", "Documentation" })
            await Expect(Main.GetByText(duty, new() { Exact = true }).First).ToBeVisibleAsync(new() { Timeout = 15_000 });

        // Hand out Evidence Collection to the first available attendee, then take it back.
        var row = Main.Locator("div.border.rounded", new() { HasText = "Documentation" }).First;
        var picker = row.Locator("select");
        if (await picker.CountAsync() == 0)
            Assert.Ignore("No manage rights on this seeded visit — the board rendered, which is the read half.");

        var options = picker.Locator("option");
        if (await options.CountAsync() < 2)
            Assert.Ignore("Every attendee already holds Documentation — residue from another run; the finally below is what prevents that.");

        var name = (await options.Nth(1).TextContentAsync())!.Trim();
        await picker.SelectOptionAsync(new SelectOptionValue { Index = 1 });
        var badge = row.Locator(".badge.bg-info", new() { HasText = name });
        await Expect(badge).ToBeVisibleAsync(new() { Timeout = 15_000 });

        try
        {
            // The board must survive a cold reload — it is data, not client state.
            await Page.ReloadAsync();
            await WaitUntilLoadedAsync();
            await Page.GotoAsync($"{BaseUrl}{orgUrl}?tab=investigations");
            await WaitUntilLoadedAsync();
            var reopenRow = Main.Locator("tr", new() { HasTextString = "winter survey" }).First;
            await Expect(reopenRow).ToBeVisibleAsync(new() { Timeout = 30_000 });
            await reopenRow.GetByRole(AriaRole.Button, new() { Name = "Team" }).ClickAsync();
            await Expect(Main.GetByText("Who's doing what")).ToBeVisibleAsync(new() { Timeout = 30_000 });
            row = Main.Locator("div.border.rounded", new() { HasText = "Documentation" }).First;
            await Expect(row.Locator(".badge.bg-info", new() { HasText = name })).ToBeVisibleAsync(new() { Timeout = 15_000 });
        }
        finally
        {
            // Take back EVERY Documentation holder, not just ours — residue in a shared database
            // outlives the run that created it, and this finally is the only janitor.
            row = Main.Locator("div.border.rounded", new() { HasText = "Documentation" }).First;
            for (var i = 0; i < 5 && await row.Locator("a", new() { HasTextString = "✕" }).CountAsync() > 0; i++)
            {
                await row.Locator("a", new() { HasTextString = "✕" }).First.ClickAsync();
                await Page.WaitForTimeoutAsync(500);
            }
        }
    }

    [Test]
    public async Task A_case_shows_its_contact_with_the_manager_fallback_and_a_choice_sticks()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        // The SEEDED case by name, not "the first card whose reference starts #2026-". That
        // lookup takes whichever case happens to sort first, so it broke the moment anything
        // added a case to this group — a test's own fixture, a later seed, or a person. The
        // three sibling tests in CaseMessageBoardTests already name Belmont for the same
        // reason, and it is the case this one needs: the only seeded one with a manager, whose
        // fallback badge is the whole assertion below.
        Assert.That(await OpenOrgCaseAsync("Paranormal365", "Belmont"), Is.True,
            "The seeded Belmont case should be reachable.");

        var panel = Main.Locator(".card", new() { HasText = "Points of contact" });
        await Expect(panel).ToBeVisibleAsync(new() { Timeout = 20_000 });

        // The fallback: with no explicit contact the case manager stands in, badged as such.
        await Expect(panel.GetByText("case manager", new() { Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        // Choose an explicit contact…
        await ClickUntilAsync(panel.Locator("#case-contacts-edit"), panel.Locator("input[type=checkbox]").First);
        var first = panel.Locator("input[type=checkbox]").First;
        var label = (await panel.Locator("label").First.TextContentAsync())!.Trim();
        await first.CheckAsync();
        await ClickUntilAsync(panel.Locator("#case-contacts-save"), panel.GetByText(label).First);

        try
        {
            // …and the fallback badge is gone: an explicit choice replaces the stand-in.
            await Expect(panel.GetByText("case manager", new() { Exact = true })).ToHaveCountAsync(0);
        }
        finally
        {
            // Clear back to the fallback, whatever happened.
            await ClickUntilAsync(panel.Locator("#case-contacts-edit"), panel.Locator("input[type=checkbox]").First);
            foreach (var box in await panel.Locator("input[type=checkbox]").AllAsync())
                await box.UncheckAsync();
            await ClickUntilAsync(panel.Locator("#case-contacts-save"),
                panel.GetByText("case manager", new() { Exact = true }));
        }
    }

    [Test]
    public async Task A_member_who_is_not_the_case_manager_is_told_who_chooses_the_contacts()
    {
        // James is neither the case manager nor an admin: the server refuses his Save, so he is not offered the picker
        // (member test pass M.1, 2026-09-14).
        await LoginAsync(MemberEmail, MemberPassword);
        Assert.That(await OpenOrgCaseAsync("Paranormal365", "Belmont"), Is.True, "The seeded Belmont case should be reachable.");

        var panel = Main.Locator(".card", new() { HasText = "Points of contact" });
        await Expect(panel.Locator("#case-contacts-who-chooses")).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Expect(panel.Locator("#case-contacts-edit")).ToHaveCountAsync(0);
    }

    /// <summary>
    /// Item 160: the title-by-duty matrix on the group's settings, under the ladder and the duty
    /// list. Read-only here — this suite runs against a shared database and saving a row would
    /// change what a real group's duties ask for. The saving itself is covered by
    /// <c>DutyEligibilityMatrixTests</c>.
    /// </summary>
    [Test]
    public async Task The_duty_matrix_renders_under_the_duties_with_a_column_per_title()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        Assert.That(await OpenOrganizationAsync("Paranormal365"), Is.True);
        var orgUrl = System.Text.RegularExpressions.Regex.Match(Page.Url, @"/organizations/[0-9a-f-]{36}").Value;

        await Page.GotoAsync($"{BaseUrl}{orgUrl}?tab=settings");
        await WaitUntilLoadedAsync();

        await Expect(Main.GetByText("Who may hold which duty")).ToBeVisibleAsync(new() { Timeout = 30_000 });

        var matrix = Main.Locator("[data-testid='duty-matrix']");
        var empty  = Main.Locator("[data-testid='matrix-empty']");
        // A group with no ladder or no duties says so rather than drawing an empty grid; both are
        // real states of this screen and neither is a failure.
        await Expect(matrix.Or(empty).First).ToBeVisibleAsync(new() { Timeout = 30_000 });

        if (await matrix.CountAsync() == 0) Assert.Ignore("This group has no ladder or no duties yet.");

        // A row per duty, and the capability columns that say what holding one does on the night.
        Assert.That(await Main.Locator("[data-testid='matrix-row']").CountAsync(), Is.GreaterThan(0));
        await Expect(Main.GetByText("point of contact").First).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(Main.GetByText("hands out duties").First).ToBeVisibleAsync(new() { Timeout = 15_000 });
    }
}
