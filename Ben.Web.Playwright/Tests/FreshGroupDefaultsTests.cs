using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Item 156 Phase F: a group born today starts with the seven default roles on its Roles tab,
/// and — item 155's hard-won lesson — can still be DELETED afterwards, because every row the
/// group is born with (roles included, since Phase C) is on the delete's birth-children list.
/// A fresh group that cannot be deleted is how that class of bug ships: the rows arrive at
/// creation, and no test ever tries the delete.
/// </summary>
[TestFixture]
[NonParallelizable]
[Category("FreshGroupDefaults")]
public class FreshGroupDefaultsTests : BenTestBase
{
    private static readonly string[] DefaultRoles =
    [
        "Case Manager Role", "Equipment Manager Role", "CMS Manager Role", "Client Manager Role",
        "Content Manager Role", "Historian Role", "Secretary Role",
    ];

    [Test]
    public async Task A_fresh_group_lists_the_seven_default_roles_and_can_be_deleted()
    {
        var login = await Page.APIRequest.PostAsync("http://localhost:5252/login",
            new() { DataObject = new { email = SuperAdminEmail, password = SuperAdminPassword } });
        Assert.That(login.Ok, "API login failed.");
        var token = (await login.JsonAsync())!.Value.GetProperty("accessToken").GetString();
        var auth  = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" };

        // A random slug per run: the group is deleted below, but a run that dies mid-test must
        // not block the next one on a urlName collision.
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var created = await Page.APIRequest.PostAsync(
            "http://localhost:5252/api/security/organizations/register",
            new()
            {
                Headers = auth,
                DataObject = new { name = $"E2E Fresh Group {suffix}", urlName = $"e2e-fresh-{suffix}" },
            });
        Assert.That(created.Ok, "Could not register the fresh group.");
        var orgId = (await created.JsonAsync())!.Value.GetProperty("organizationId").GetString()!;

        try
        {
            await LoginAsync(SuperAdminEmail, SuperAdminPassword);
            await Page.GotoAsync($"{BaseUrl}/organizations/{orgId}?tab=roles");
            await WaitUntilLoadedAsync();

            foreach (var role in DefaultRoles)
                await Expect(Main.GetByText(role, new() { Exact = true }).First)
                    .ToBeVisibleAsync(new() { Timeout = 45_000 });
        }
        finally
        {
            var deleted = await Page.APIRequest.DeleteAsync(
                $"http://localhost:5252/api/organizations/{orgId}", new() { Headers = auth });
            Assert.That(deleted.Ok,
                "The fresh group could not be deleted — a birth-child row is missing from the delete list.");

            // The roles endpoint answers an empty 200 for any org id a SuperAdmin asks
            // about, so the proof the birth-children went with the group is the EMPTY list.
            var after = await Page.APIRequest.GetAsync(
                $"http://localhost:5252/api/organizations/{orgId}/roles", new() { Headers = auth });
            Assert.That(after.Ok, "Could not re-read the roles after deletion.");
            Assert.That((await after.JsonAsync())!.Value.GetArrayLength(), Is.EqualTo(0),
                "The group's roles survived its deletion.");
        }
    }
}
