using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Item 156 Phase F: the full tier journey, seen from both ends. A SuperAdmin unchecks the
/// Cases area on the tier a real group resolves to; a role-holding ordinary member of that
/// group loses the Cases tab at runtime — the honest rendering of the refusal — and gets it
/// back the moment the area returns. Restores everything in finally (shared database), and
/// relies on Phase E's notice netting to turn the uncheck-then-recheck into silence for any
/// group with a subscription row on the tier.
/// </summary>
[TestFixture]
[NonParallelizable]
[Category("RoleTierJourney")]
public class RoleTierJourneyTests : BenTestBase
{
    /// <summary>
    /// Paranormal365's id, resolved from its slug at run time.
    /// </summary>
    /// <remarks>
    /// This was a hardcoded GUID, which survives exactly until the next database rebuild — the
    /// org comes back under a fresh id, the test navigates to an org that no longer exists, and
    /// the failure reads as "the Cases tab never appeared", pointing at permissions when the
    /// address was simply wrong. The slug is the identity the seeder actually maintains.
    /// </remarks>
    private string? _orgId;

    private async Task<string> OrgIdAsync()
    {
        if (_orgId is not null) return _orgId;
        var api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        var login = await api.PostAsync("/login", new()
        {
            DataObject = new { email = MemberEmail, password = MemberPassword },
        });
        Assert.That(login.Ok, Is.True, "the member seat should be able to sign in");
        var token = (await login.JsonAsync())!.Value.GetProperty("accessToken").GetString();
        var orgs = await api.GetAsync("/api/organizations",
            new() { Headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" } });
        Assert.That(orgs.Ok, Is.True, await orgs.TextAsync());
        foreach (var o in (await orgs.JsonAsync())!.Value.EnumerateArray())
            if (o.GetProperty("urlName").GetString() == "paranormal365")
                return _orgId = o.GetProperty("id").GetString()!;
        Assert.Fail("Paranormal365 is not in the seed data.");
        return "";
    }

    [Test]
    public async Task Unchecking_a_tier_area_revokes_a_role_holders_access_until_it_returns()
    {
        // The tier to toggle is whatever the group actually resolves to — asked of the same
        // endpoint the UI uses, so a band edit never silently redirects this test at a
        // different row than the one that governs the group.
        var tierName = await ResolveTierNameAsync();
        Assert.That(tierName, Is.Not.Null.And.Not.Empty,
            "The group resolved no tier — the price list is unusable, which is its own bug.");

        // The member's starting state: James holds the (bridged) Investigator Role, so the
        // Cases tab is there. If it is not, the database has drifted and the test should say
        // so rather than "fix" a group's roles it does not own.
        await LoginAsync(MemberEmail, MemberPassword);
        await GotoOrgAsync();
        await Expect(CasesTab()).ToBeVisibleAsync(new() { Timeout = 45_000 });

        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        var cases = await OpenTierRowCheckboxAsync(tierName!);

        if (!await cases.IsCheckedAsync())   // self-heal residue from a run that died mid-test
        {
            await cases.CheckAsync();
            await Expect(cases).ToBeCheckedAsync(new() { Timeout = 45_000 });
        }

        try
        {
            await cases.UncheckAsync();
            await Expect(cases).Not.ToBeCheckedAsync(new() { Timeout = 45_000 });

            // The member's Cases tab is gone — not erroring, not disabled: gone, because the
            // tab gate asks the same my-permissions endpoint the server enforces with.
            await LoginAsync(MemberEmail, MemberPassword);
            await GotoOrgAsync();
            await Expect(Main.GetByRole(AriaRole.Tab, new() { Name = "Members", Exact = true }))
                .ToBeVisibleAsync(new() { Timeout = 45_000 });   // page is rendered…
            await Expect(CasesTab()).ToHaveCountAsync(0);         // …and Cases is not in it

            // The area returns; so does the access. Same seat, same page, no role changes.
            await LoginAsync(SuperAdminEmail, SuperAdminPassword);
            cases = await OpenTierRowCheckboxAsync(tierName!);
            await cases.CheckAsync();
            await Expect(cases).ToBeCheckedAsync(new() { Timeout = 45_000 });

            await LoginAsync(MemberEmail, MemberPassword);
            await GotoOrgAsync();
            await Expect(CasesTab()).ToBeVisibleAsync(new() { Timeout = 45_000 });
        }
        finally
        {
            await LoginAsync(SuperAdminEmail, SuperAdminPassword);
            var restore = await OpenTierRowCheckboxAsync(tierName!);
            if (!await restore.IsCheckedAsync())
                await restore.CheckAsync();
            await Expect(restore).ToBeCheckedAsync(new() { Timeout = 45_000 });
        }
    }

    private ILocator CasesTab()
        => Main.GetByRole(AriaRole.Tab, new() { Name = "Cases", Exact = true });

    private async Task GotoOrgAsync()
    {
        await Page.GotoAsync($"{BaseUrl}/organizations/{await OrgIdAsync()}?tab=details");
        await WaitUntilLoadedAsync();
    }

    /// <summary>The Cases-area checkbox (area 3) on the named tier's admin row.</summary>
    private async Task<ILocator> OpenTierRowCheckboxAsync(string tierName)
    {
        await Page.GotoAsync($"{BaseUrl}/admin/subscription-tiers");
        await WaitUntilLoadedAsync();
        var row = Main.Locator("tr", new() { HasTextString = tierName }).First;
        var box = row.Locator("input[type=checkbox][id^='area-'][id$='-3']");   // Cases = 3
        await Expect(box).ToBeVisibleAsync(new() { Timeout = 45_000 });
        return box;
    }

    private async Task<string?> ResolveTierNameAsync()
    {
        var login = await Page.APIRequest.PostAsync("http://localhost:5252/login",
            new() { DataObject = new { email = SuperAdminEmail, password = SuperAdminPassword } });
        Assert.That(login.Ok, "API login failed while resolving the group's tier.");
        var token = (await login.JsonAsync())!.Value.GetProperty("accessToken").GetString();

        var areas = await Page.APIRequest.GetAsync(
            $"http://localhost:5252/api/security/organizations/{await OrgIdAsync()}/included-areas",
            new() { Headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" } });
        if (!areas.Ok) return null;
        var json = await areas.JsonAsync();
        return json!.Value.TryGetProperty("tierName", out var n) ? n.GetString() : null;
    }
}
