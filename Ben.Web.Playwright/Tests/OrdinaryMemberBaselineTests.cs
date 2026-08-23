using System.Text.Json;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Item 156 Phase F: what an ordinary member is, measured from the ordinary member's seat
/// (the test-as-an-ordinary-member rule — every fixture that only looked from the owner's
/// chair has missed a total failure at least once).
/// </summary>
/// <remarks>
/// Role changes are made through the API as SuperAdmin — the machinery under the buttons —
/// and every VERIFICATION is James's own browser, because the claim under test is what HE
/// sees. The fixture ENSUREs James's normal state (the bridged Investigator Role) on the way
/// out of every test, including failed ones, so a dead run never strands the shared seed
/// account without the access every other fixture assumes.
/// </remarks>
[TestFixture]
[NonParallelizable]
[Category("OrdinaryMemberBaseline")]
public class OrdinaryMemberBaselineTests : BenTestBase
{
    private const string TghId   = "881ea0f6-8c0d-475e-9065-c6ed15e3302f"; // Tennessee Ghost Hunters
    private const string BenCoId = "8e739323-9e09-480f-a10b-32e91eb618cd"; // BenCo
    private const string MemberDisplayName = "James Thornton";

    private string _token = "";

    [SetUp]
    public async Task ApiLogin()
    {
        var login = await Page.APIRequest.PostAsync("http://localhost:5252/login",
            new() { DataObject = new { email = SuperAdminEmail, password = SuperAdminPassword } });
        Assert.That(login.Ok, "API login failed; the fixture cannot arrange roles.");
        _token = (await login.JsonAsync())!.Value.GetProperty("accessToken").GetString()!;
    }

    [Test]
    public async Task A_roleless_member_sees_the_baseline_and_a_role_grant_opens_cases()
    {
        var investigatorId = await RoleIdAsync(TghId, "Investigator Role");
        var caseManagerId  = await RoleIdAsync(TghId, "Case Manager Role");
        var membershipId   = await OrgMembershipIdAsync(TghId, MemberDisplayName);

        try
        {
            // Strip James to a role-less member (ENSURE, not assert: a previous dead run may
            // already have removed it).
            await RemoveRoleMemberAsync(TghId, investigatorId, membershipId);
            await RemoveRoleMemberAsync(TghId, caseManagerId, membershipId);

            // D3 baseline, from his seat: the group's profile, people, calendar, messages,
            // and files are all there — and Cases/Investigations are not.
            await LoginAsync(MemberEmail, MemberPassword);
            await GotoOrgAsync(TghId);
            foreach (var tab in new[] { "Details", "Members", "Calendar", "Messages", "Files" })
                await Expect(Tab(tab)).ToBeVisibleAsync(new() { Timeout = 45_000 });
            await Expect(Tab("Cases")).ToHaveCountAsync(0);
            await Expect(Tab("Investigations")).ToHaveCountAsync(0);

            // One role grant, and the doors it names open — both of them, because the Case
            // Manager Role carries Case and Investigation.
            await AddRoleMemberAsync(TghId, caseManagerId, membershipId);
            await GotoOrgAsync(TghId);
            await Expect(Tab("Cases")).ToBeVisibleAsync(new() { Timeout = 45_000 });
            await Expect(Tab("Investigations")).ToBeVisibleAsync(new() { Timeout = 45_000 });

            // Taken away again: gone again. Grants stop at runtime; nothing lingers.
            await RemoveRoleMemberAsync(TghId, caseManagerId, membershipId);
            await GotoOrgAsync(TghId);
            await Expect(Tab("Cases")).ToHaveCountAsync(0);
            await Expect(Tab("Investigations")).ToHaveCountAsync(0);
        }
        finally
        {
            await RemoveRoleMemberAsync(TghId, caseManagerId, membershipId);
            await EnsureRoleMemberAsync(TghId, investigatorId, membershipId);
        }
    }

    [Test]
    public async Task Removing_a_role_in_one_group_leaves_the_other_groups_access_alone()
    {
        var tghInvestigatorId = await RoleIdAsync(TghId, "Investigator Role");
        var tghMembershipId   = await OrgMembershipIdAsync(TghId, MemberDisplayName);

        try
        {
            await RemoveRoleMemberAsync(TghId, tghInvestigatorId, tghMembershipId);

            await LoginAsync(MemberEmail, MemberPassword);
            await GotoOrgAsync(TghId);
            await Expect(Tab("Members")).ToBeVisibleAsync(new() { Timeout = 45_000 });
            await Expect(Tab("Cases")).ToHaveCountAsync(0);

            // Same person, same minute, different group: BenCo's Investigator Role is its own
            // grant in its own group, and TGH's removal must not reach it.
            await GotoOrgAsync(BenCoId);
            await Expect(Tab("Cases")).ToBeVisibleAsync(new() { Timeout = 45_000 });
        }
        finally
        {
            await EnsureRoleMemberAsync(TghId, tghInvestigatorId, tghMembershipId);
        }
    }

    // ── The member's view ─────────────────────────────────────────────────────

    private ILocator Tab(string name)
        => Main.GetByRole(AriaRole.Tab, new() { Name = name, Exact = true });

    private async Task GotoOrgAsync(string orgId)
    {
        await Page.GotoAsync($"{BaseUrl}/organizations/{orgId}?tab=details");
        await WaitUntilLoadedAsync();
    }

    // ── The SuperAdmin's arrangement, over the same API the UI uses ───────────

    private Dictionary<string, string> Auth()
        => new() { ["Authorization"] = $"Bearer {_token}" };

    private async Task<string> RoleIdAsync(string orgId, string roleName)
    {
        var res = await Page.APIRequest.GetAsync(
            $"http://localhost:5252/api/organizations/{orgId}/roles", new() { Headers = Auth() });
        Assert.That(res.Ok, $"Could not list roles for {orgId}.");
        foreach (var r in (await res.JsonAsync())!.Value.EnumerateArray())
            if (r.GetProperty("name").GetString() == roleName)
                return r.GetProperty("id").GetString()!;
        Assert.Fail($"Role '{roleName}' not found in {orgId} — the default-role seed is missing.");
        return "";
    }

    private async Task<string> OrgMembershipIdAsync(string orgId, string displayName)
    {
        var res = await Page.APIRequest.GetAsync(
            $"http://localhost:5252/api/organizations/{orgId}/roster", new() { Headers = Auth() });
        Assert.That(res.Ok, $"Could not read the roster for {orgId}.");
        foreach (var m in (await res.JsonAsync())!.Value.EnumerateArray())
            if (m.GetProperty("displayName").GetString() == displayName)
                return m.GetProperty("membershipId").GetString()!;
        Assert.Fail($"'{displayName}' is not on the roster of {orgId}.");
        return "";
    }

    /// <summary>The role-membership ROW id for this member, or null when they don't hold it.</summary>
    private async Task<string?> RoleMembershipRowIdAsync(string orgId, string roleId, string membershipId)
    {
        var res = await Page.APIRequest.GetAsync(
            $"http://localhost:5252/api/organizations/{orgId}/roles/{roleId}/members",
            new() { Headers = Auth() });
        if (!res.Ok) return null;
        foreach (var m in (await res.JsonAsync())!.Value.EnumerateArray())
            if (m.GetProperty("organizationUserMembershipId").GetString() == membershipId)
                return m.GetProperty("id").GetString();
        return null;
    }

    private async Task AddRoleMemberAsync(string orgId, string roleId, string membershipId)
    {
        var res = await Page.APIRequest.PostAsync(
            $"http://localhost:5252/api/organizations/{orgId}/roles/{roleId}/members",
            new()
            {
                Headers = Auth(),
                DataObject = new { organizationUserMembershipId = membershipId },
            });
        Assert.That(res.Ok, $"Could not assign role {roleId} to membership {membershipId}.");
    }

    private async Task RemoveRoleMemberAsync(string orgId, string roleId, string membershipId)
    {
        // Idempotent by design: removing a role someone doesn't hold is a no-op, because the
        // ENSURE-not-assert rule applies to teardown as much as setup.
        var rowId = await RoleMembershipRowIdAsync(orgId, roleId, membershipId);
        if (rowId is null) return;
        await Page.APIRequest.DeleteAsync(
            $"http://localhost:5252/api/organizations/{orgId}/roles/{roleId}/members/{rowId}",
            new() { Headers = Auth() });
    }

    private async Task EnsureRoleMemberAsync(string orgId, string roleId, string membershipId)
    {
        if (await RoleMembershipRowIdAsync(orgId, roleId, membershipId) is null)
            await AddRoleMemberAsync(orgId, roleId, membershipId);
    }
}
