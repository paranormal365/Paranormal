using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Item 156 Phase A: the per-tier role-areas checklist saves on toggle and survives a reload.
/// Restores what it changes — shared database.
/// </summary>
[TestFixture]
[Category("TierRoleAreas")]
public class TierRoleAreasTests : BenTestBase
{
    [Test]
    public async Task Unchecking_an_area_persists_and_rechecking_restores_it()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/admin/subscription-tiers");
        await WaitUntilLoadedAsync();

        var freeRow = Main.Locator("tr", new() { HasTextString = "Free" }).First;
        var calendar = freeRow.Locator("input[type=checkbox][id^='area-'][id$='-9']");   // Calendar = 9
        await Expect(calendar).ToBeVisibleAsync(new() { Timeout = 45_000 });

        // ENSURE the starting state rather than asserting it: a previous run that died between
        // uncheck and restore leaves residue in the shared database, and an asserted
        // precondition turns one bad run into a permanently red test. Self-healing beats blame.
        if (!await calendar.IsCheckedAsync())
        {
            await calendar.CheckAsync();
            await Expect(calendar).ToBeCheckedAsync(new() { Timeout = 45_000 });
            await Page.ReloadAsync();
            await WaitUntilLoadedAsync();
            freeRow = Main.Locator("tr", new() { HasTextString = "Free" }).First;
            calendar = freeRow.Locator("input[type=checkbox][id^='area-'][id$='-9']");
        }

        try
        {
            await calendar.UncheckAsync();
            await Page.ReloadAsync();
            await WaitUntilLoadedAsync();

            freeRow = Main.Locator("tr", new() { HasTextString = "Free" }).First;
            calendar = freeRow.Locator("input[type=checkbox][id^='area-'][id$='-9']");
            await Expect(calendar).Not.ToBeCheckedAsync(new() { Timeout = 45_000 });
        }
        finally
        {
            freeRow = Main.Locator("tr", new() { HasTextString = "Free" }).First;
            calendar = freeRow.Locator("input[type=checkbox][id^='area-'][id$='-9']");
            if (!await calendar.IsCheckedAsync())
                await calendar.CheckAsync();
            await Expect(calendar).ToBeCheckedAsync(new() { Timeout = 45_000 });
        }
    }

    /// <summary>
    /// Item 156 Phase E: for a group whose plan excludes an area, the role editor shows the
    /// plan note and disables that area's toggles. Uses a dedicated free-band fixture group
    /// (ENSURE-created — one member keeps it on the Free band) so no real group's editor is
    /// involved, and restores the checklist in finally.
    /// </summary>
    [Test]
    public async Task Excluding_an_area_grays_the_role_editor_for_a_free_band_group()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);

        var orgId = await EnsureProbeGroupAsync();

        await Page.GotoAsync($"{BaseUrl}/admin/subscription-tiers");
        await WaitUntilLoadedAsync();

        var freeRow  = Main.Locator("tr", new() { HasTextString = "Free" }).First;
        var calendar = freeRow.Locator("input[type=checkbox][id^='area-'][id$='-9']");   // Calendar = 9
        await Expect(calendar).ToBeVisibleAsync(new() { Timeout = 45_000 });

        if (!await calendar.IsCheckedAsync())   // self-heal residue from a dead run
        {
            await calendar.CheckAsync();
            await Expect(calendar).ToBeCheckedAsync(new() { Timeout = 45_000 });
        }

        try
        {
            await calendar.UncheckAsync();
            await Expect(calendar).Not.ToBeCheckedAsync(new() { Timeout = 45_000 });

            await Page.GotoAsync($"{BaseUrl}/organizations/{orgId}?tab=roles");
            await WaitUntilLoadedAsync();

            var edit = Main.Locator("table button", new() { HasTextString = "Edit" }).First;
            await Expect(edit).ToBeVisibleAsync(new() { Timeout = 45_000 });
            await ClickUntilAsync(edit, Main.Locator("#role-edit-card"));

            await Expect(Main.Locator("#role-areas-note")).ToBeVisibleAsync(new() { Timeout = 45_000 });
            await Expect(Main.Locator("#role-areas-note")).ToContainTextAsync("Calendar");

            // The Calendar row's toggles are disabled, not merely styled.
            var calendarRow = Main.Locator("#role-edit-card tr.opacity-50", new() { HasTextString = "Calendar" }).First;
            await Expect(calendarRow).ToBeVisibleAsync(new() { Timeout = 45_000 });
            await Expect(calendarRow.Locator("button[disabled], input[disabled]").First)
                .ToBeVisibleAsync(new() { Timeout = 45_000 });
        }
        finally
        {
            await Page.GotoAsync($"{BaseUrl}/admin/subscription-tiers");
            await WaitUntilLoadedAsync();
            freeRow  = Main.Locator("tr", new() { HasTextString = "Free" }).First;
            calendar = freeRow.Locator("input[type=checkbox][id^='area-'][id$='-9']");
            await Expect(calendar).ToBeVisibleAsync(new() { Timeout = 45_000 });
            if (!await calendar.IsCheckedAsync())
                await calendar.CheckAsync();
            await Expect(calendar).ToBeCheckedAsync(new() { Timeout = 45_000 });
        }
    }

    /// <summary>
    /// The free-band fixture group's id, creating the group if a previous database wipe took
    /// it. Registered through the same API the sign-up flow uses; the SuperAdmin owner is its
    /// only member, which keeps it on the Free band whatever the bands' member ranges are.
    /// </summary>
    private async Task<string> EnsureProbeGroupAsync()
    {
        const string probeUrlName = "phase-e-probe";

        var login = await Page.APIRequest.PostAsync("http://localhost:5252/login",
            new() { DataObject = new { email = SuperAdminEmail, password = SuperAdminPassword } });
        Assert.That(login.Ok, "API login for the fixture group failed.");
        var token = (await login.JsonAsync())!.Value.GetProperty("accessToken").GetString();
        var auth  = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" };

        var mine = await Page.APIRequest.GetAsync(
            "http://localhost:5252/api/security/organizations/my-memberships",
            new() { Headers = auth });
        if (mine.Ok)
        {
            foreach (var m in (await mine.JsonAsync())!.Value.EnumerateArray())
            {
                // my-memberships carries name + id only — no urlName — so match on the name.
                if (m.TryGetProperty("name", out var n) && n.GetString() == "Phase E Probe Group")
                    return m.GetProperty("organizationId").GetString()!;
            }
        }

        var created = await Page.APIRequest.PostAsync(
            "http://localhost:5252/api/security/organizations/register",
            new()
            {
                Headers = auth,
                DataObject = new { name = "Phase E Probe Group", urlName = probeUrlName },
            });
        Assert.That(created.Ok, "Could not create the free-band fixture group.");
        return (await created.JsonAsync())!.Value.GetProperty("organizationId").GetString()!;
    }
}
