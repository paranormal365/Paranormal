using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// A picture from an event's gallery offered to the venue, kept, and shown on the venue's page (item 235 phase 12).
/// </summary>
/// <remarks>
/// The seeded host is itself the venue at the Thomas House, so its own offer is kept at once; an offer from another
/// group waiting for the venue is proved in <c>VenuePhotoTests</c>.
/// </remarks>
[TestFixture]
[Category("HostedEvents")]
public class VenuePhotoLibraryTests : BenTestBase
{
    private const string RoomsEventId = "40000002-0000-0000-0000-000000000002";
    private const string VenuePlaceId = "40000002-0000-0000-0000-000000000001";
    private string _orgId = "";
    private readonly string _caption = $"pw-venue-{Guid.NewGuid():N}"[..17];

    [SetUp]
    public async Task Start() => _orgId = await OrgIdBySlugAsync("paranormal365");

    [TearDown]
    public async Task RemoveWhatThisRunAdded()
    {
        await using var api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        var login = await api.PostAsync("/login", new() { DataObject = new { email = SuperAdminEmail, password = SuperAdminPassword } });
        if (!login.Ok) return;
        var headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {(await login.JsonAsync())!.Value.GetProperty("accessToken").GetString()}" };

        // The library first: while it keeps the picture, the gallery's file cannot go.
        var profiles = await api.GetAsync($"/api/organizations/{_orgId}/venue-profiles", new() { Headers = headers });
        if (profiles.Ok)
            foreach (var profile in (await profiles.JsonAsync())!.Value.EnumerateArray())
            {
                var profileId = profile.GetProperty("id").GetString();
                var photos = await api.GetAsync($"/api/organizations/{_orgId}/venue-profiles/{profileId}/photos", new() { Headers = headers });
                if (!photos.Ok) continue;
                foreach (var photo in (await photos.JsonAsync())!.Value.EnumerateArray())
                    if (photo.TryGetProperty("caption", out var c) && c.GetString() == _caption)
                        await api.DeleteAsync($"/api/organizations/{_orgId}/venue-profiles/{profileId}/photos/{photo.GetProperty("id").GetString()}", new() { Headers = headers });
            }

        var list = await api.GetAsync($"/api/organizations/{_orgId}/events/{RoomsEventId}/gallery", new() { Headers = headers });
        if (!list.Ok) return;
        foreach (var image in (await list.JsonAsync())!.Value.EnumerateArray())
            if (image.TryGetProperty("caption", out var c) && c.GetString() == _caption)
                await api.DeleteAsync($"/api/organizations/{_orgId}/events/{RoomsEventId}/gallery/{image.GetProperty("id").GetString()}", new() { Headers = headers });
    }

    [Test]
    public async Task A_gallery_picture_offered_to_the_venue_appears_on_the_venue_page()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/organizations/{_orgId}/events/{RoomsEventId}/gallery");
        await WaitUntilLoadedAsync();

        await Expect(Page.Locator("#gallery-input")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Page.Locator("#gallery-input").SetInputFilesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "room-photo-1.jpg"));
        await Page.Locator("#gallery-caption").FillAsync(_caption);
        await ClickUntilAsync(Page.Locator("#gallery-add"), Page.Locator("#gallery-note"));

        // The newest picture is last; a caption box's value is not in the DOM for a selector to find.
        var card = Page.Locator(".gallery-image").Last;
        await Expect(card).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await ClickUntilAsync(card.GetByRole(AriaRole.Button, new() { Name = "Offer to venue" }),
            card.GetByText("Offered to"));

        await LogoutAsync();
        await Page.GotoAsync($"{BaseUrl}/o/paranormal365/venues/{VenuePlaceId}");
        await WaitUntilLoadedAsync();
        var picture = Page.Locator("#venue-photos figure", new() { HasTextString = _caption });
        await Expect(picture).ToBeVisibleAsync(new() { Timeout = 30_000 });

        var loaded = await picture.Locator("img").EvaluateAsync<bool>("img => img.complete && img.naturalWidth > 0");
        Assert.That(loaded, Is.True, "the venue's picture did not load for a visitor");
    }
}
