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
    /// <summary>
    /// The LOWEST band's row — the one a group of one lands on.
    /// </summary>
    /// <remarks>
    /// Both tests here used to find the row by the literal text "Free", and stopped working the
    /// day the ladder was renamed to Small Group / Standard / Large / Enterprise. A band's NAME is
    /// a business decision the tests must not own; what they actually need is "the band the probe
    /// group is on", which is the first row carrying area toggles. Renaming the ladder again will
    /// not break this.
    /// </remarks>
    private ILocator LowestBandRow => Main.Locator("tr")
        .Filter(new() { Has = Page.Locator("input[type=checkbox][id^='area-']") })
        .First;

    [Test]
    public async Task Unchecking_an_area_persists_and_rechecking_restores_it()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/admin/subscription-tiers");
        await WaitUntilLoadedAsync();

        var freeRow = LowestBandRow;
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
            freeRow = LowestBandRow;
            calendar = freeRow.Locator("input[type=checkbox][id^='area-'][id$='-9']");
        }

        try
        {
            await calendar.UncheckAsync();
            await Page.ReloadAsync();
            await WaitUntilLoadedAsync();

            freeRow = LowestBandRow;
            calendar = freeRow.Locator("input[type=checkbox][id^='area-'][id$='-9']");
            await Expect(calendar).Not.ToBeCheckedAsync(new() { Timeout = 45_000 });
        }
        finally
        {
            freeRow = LowestBandRow;
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

        var freeRow  = LowestBandRow;
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

            // If this is what fails, the question is NOT the test. The lowest band now has Calendar
            // excluded (asserted above, and it saved), and the probe group has one member so it
            // resolves to that band — yet its role editor shows no plan note. Either the group is
            // not resolving to the band that was edited, or a group with no SUBSCRIPTION row
            // inherits no area restrictions at all, which is a real answer about what an
            // unsubscribed group's plan means and not something a test should decide.
            //
            // Recorded 2026-08-31: this went red when the ladder lost its "Free" band. Do not
            // "fix" it by relaxing the assertion.
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
            freeRow  = LowestBandRow;
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
