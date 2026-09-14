using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// A place's contact details, and a group claiming to run the place (item 235 phase 9).
/// </summary>
/// <remarks>
/// <para><b>The code path cannot be walked here</b> — the harness has no mail — so proving a claim
/// by a code, the week for objections and objecting are unit tests (<c>VenuePlaceClaimTests</c>).
/// What is walked is everything a person does on a screen: recording a public and a private detail
/// and seeing who can see which, claiming a place that has no address to prove it by, and a reviewer
/// confirming it, after which the place names its venue.</para>
///
/// <para>A group and a place are made fresh for each run and the group is purged afterwards. The
/// place stays — places are shared — so each run records a website no earlier run did.</para>
/// </remarks>
[TestFixture]
[Category("HostedEvents")]
public class VenueClaimTests : BenTestBase
{
    private const string OrgName = "Playwright Claimants";

    private IAPIRequestContext _api = null!;
    private string? _orgId;
    private string _placeId = "";
    private readonly string _website = $"https://pw-{Guid.NewGuid():N}"[..20] + ".example.com";

    [SetUp]
    public async Task AGroupAndABuildingNobodyRunsYet()
    {
        _api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        var authed = await HeadersAsync(UserEmail, UserPassword);
        if (authed is null) Assert.Ignore("The seeded owner seat cannot sign in on this deployment.");

        var register = await _api.PostAsync("/api/security/organizations/register", new()
        {
            Headers = authed,
            DataObject = new { name = OrgName, urlName = $"pw-claim-{Guid.NewGuid():N}"[..20], kind = 0 },
        });
        Assert.That(register.Ok, Is.True, await register.TextAsync());
        _orgId = (await register.JsonAsync())!.Value.GetProperty("organizationId").GetString();

        var start = DateTime.UtcNow.Date.AddDays(60);
        var ev = await _api.PostAsync($"/api/organizations/{_orgId}/events", new()
        {
            Headers = authed,
            DataObject = new
            {
                name = "Claim test evening", placeId = Guid.Empty, startsOn = start, endsOn = start,
                timeZoneId = "America/Chicago",
                newVenue = new
                {
                    name = "Playwright Claim Hall", streetAddress1 = "9 Claim Test Row",
                    city = "Franklin", state = "TN", zipCode = "37064", country = "US",
                    latitude = 35.9251m, longitude = -86.8689m,
                },
            },
        });
        Assert.That(ev.Ok, Is.True, await ev.TextAsync());
        _placeId = (await ev.JsonAsync())!.Value.GetProperty("placeId").GetString()!;
    }

    [TearDown]
    public async Task TakeTheGroupAwayAgain()
    {
        if (_orgId is not null && await HeadersAsync(SuperAdminEmail, SuperAdminPassword) is { } admin)
        {
            var purged = await _api.DeleteAsync($"/api/admin/organizations/{_orgId}/purge",
                new() { Headers = admin, DataObject = new { confirmName = OrgName } });
            Assert.That(purged.Ok, Is.True, $"the purge refused a group with a claim, a profile and contacts: {await purged.TextAsync()}");
        }
        await _api.DisposeAsync();
    }

    [Test]
    public async Task A_public_detail_is_everybodys_and_a_private_one_is_the_groups()
    {
        await LoginAsync(UserEmail, UserPassword);
        await OpenThePlaceAsync();

        await AddAsync("Website", _website, "Their site", isPublic: true);
        await AddAsync("Phone", "(615) 555-0142", "Events coordinator, for scheduling", isPublic: false);

        var contacts = Page.Locator("#place-contacts");
        await Expect(contacts).ToContainTextAsync(_website);
        await Expect(contacts).ToContainTextAsync("Private to");

        // Signed out, the public detail is there and the coordinator's number is not.
        await LogoutAsync();
        await OpenThePlaceAsync();
        await Expect(contacts).ToContainTextAsync(_website, new() { Timeout = 30_000 });
        await Expect(contacts).Not.ToContainTextAsync("555-0142");
    }

    [Test]
    public async Task A_claim_with_nothing_to_prove_it_by_is_reviewed_and_then_the_place_names_its_venue()
    {
        await LoginAsync(UserEmail, UserPassword);
        await OpenThePlaceAsync();

        await Page.Locator(".place-claim-link", new() { HasTextString = OrgName }).ClickAsync();
        await Expect(Page.Locator("#claim-submit")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.Locator("#claim-no-proving")).ToBeVisibleAsync();

        await Page.Locator("label[for=claim-role-2]").ClickAsync();
        await Page.Locator("#claim-evidence").FillAsync("I book events for the owners. Our licence is under this name.");
        await ClickUntilAsync(Page.Locator("#claim-submit"), Page.Locator("#claim-under-review"));

        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/admin/venue-claims");
        await WaitUntilLoadedAsync();

        var claim = Page.Locator(".admin-venue-claim", new() { HasTextString = OrgName });
        await Expect(claim).ToContainTextAsync("I book events for the owners", new() { Timeout = 30_000 });
        await ClickUntilAsync(claim.Locator("button", new() { HasTextString = "Confirm" }), Page.Locator("#admin-claims-note"));

        await OpenThePlaceAsync();
        await Expect(Page.Locator("#place-venue-line")).ToContainTextAsync(OrgName, new() { Timeout = 30_000 });
    }

    // ── the plumbing ─────────────────────────────────────────────────────────

    private async Task OpenThePlaceAsync()
    {
        await Page.GotoAsync($"{BaseUrl}/places/{_placeId}");
        await WaitUntilLoadedAsync();
        await Expect(Page.Locator("#place-contacts")).ToBeVisibleAsync(new() { Timeout = 30_000 });
    }

    private async Task AddAsync(string kind, string value, string label, bool isPublic)
    {
        await ClickUntilAsync(Page.Locator("#place-contact-add-open"), Page.Locator("#place-contact-form"));
        await Page.Locator("#place-contact-kind").SelectOptionAsync(new SelectOptionValue { Label = kind });
        await Page.Locator("#place-contact-value").FillAsync(value);
        await Page.Locator("#place-contact-label").FillAsync(label);
        await Page.Locator(isPublic ? "label[for=place-contact-public]" : "label[for=place-contact-private]").ClickAsync();
        await Page.Locator("#place-contact-save").ClickAsync();
        await Expect(Page.Locator("#place-contacts")).ToContainTextAsync(label, new() { Timeout = 15_000 });
    }

    private async Task<Dictionary<string, string>?> HeadersAsync(string email, string password)
    {
        var login = await _api.PostAsync("/login", new() { DataObject = new { email, password } });
        if (!login.Ok) return null;
        var token = (await login.JsonAsync())!.Value.GetProperty("accessToken").GetString();
        return new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" };
    }
}
