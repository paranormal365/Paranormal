using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// A tour business is billed the flat business price, not a member band (item 231), and it can
/// be started the way the tour-business mailing says.
/// </summary>
/// <remarks>
/// The group is registered through the same endpoint the Start a Group wizard uses, as the seeded
/// ordinary member, with the ghost-walking-tour kind; its billing page is then read as that
/// member. Deleted afterwards through the admin seat whatever happened. Needs a flat tier on
/// offer — the test says so and stops if there is none, because that is a configuration fact,
/// not a defect.
/// </remarks>
[TestFixture]
[Category("TourBilling")]
public class TourBusinessBillingTests : BenTestBase
{
    [Test]
    public async Task A_tour_business_is_quoted_the_flat_price_whatever_its_size()
    {
        var api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        var login = await api.PostAsync("/login", new() { DataObject = new { email = UserEmail, password = UserPassword } });
        Assert.That(login.Ok, Is.True, "the seeded member should be able to sign in");
        var token = (await login.JsonAsync())!.Value.GetProperty("accessToken").GetString();
        var authed = new APIRequestContextOptions { Headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" } };

        var tiers = await api.GetAsync("/api/public/pricing");
        var flat = (await tiers.JsonAsync())!.Value.EnumerateArray()
            .FirstOrDefault(t => t.TryGetProperty("isBandedByMembers", out var b) && !b.GetBoolean());
        if (flat.ValueKind == System.Text.Json.JsonValueKind.Undefined)
            Assert.Ignore("No flat business tier is on offer on this deployment.");
        var flatName = flat.GetProperty("name").GetString()!;

        var slug = $"pw-tour-{Guid.NewGuid():N}"[..20];
        var register = await api.PostAsync("/api/security/organizations/register", new()
        {
            Headers = authed.Headers,
            DataObject = new { name = "Playwright Ghost Walk", urlName = slug, kind = 1 },   // GhostWalkingTour
        });
        Assert.That(register.Ok, Is.True, await register.TextAsync());
        var orgId = (await register.JsonAsync())!.Value.GetProperty("organizationId").GetString();

        try
        {
            await LoginAsync(UserEmail, UserPassword);
            await Page.GotoAsync($"{BaseUrl}/organizations/{orgId}/billing");
            await WaitUntilLoadedAsync();

            // The quote names the flat tier and its price; a one-person tour would otherwise sit in
            // the lowest member band.
            var quote = Page.GetByText(flatName, new() { Exact = false }).First;
            await Expect(quote).ToBeVisibleAsync(new() { Timeout = 20_000 });
            var monthly = flat.GetProperty("prices").EnumerateArray().First(p => p.GetProperty("interval").GetInt32() == 1)
                .GetProperty("price").GetDecimal();
            await Expect(Page.GetByText($"${monthly:0.00}", new() { Exact = false }).First).ToBeVisibleAsync(new() { Timeout = 10_000 });
            Assert.That(await Page.InnerTextAsync("body"), Does.Not.Contain("An unhandled error has occurred"));
        }
        finally
        {
            var admin = await api.PostAsync("/login", new() { DataObject = new { email = SuperAdminEmail, password = SuperAdminPassword } });
            if (admin.Ok)
            {
                var adminToken = (await admin.JsonAsync())!.Value.GetProperty("accessToken").GetString();
                await api.DeleteAsync($"/api/organizations/{orgId}",
                    new() { Headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {adminToken}" } });
            }
            await api.DisposeAsync();
        }
    }
}
