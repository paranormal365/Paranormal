using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// A file added to a public place, waiting for review, and approved (10/09/2026).
/// </summary>
/// <remarks>
/// Ben added a video to Cragfont, was told somebody would look at it, and could find nowhere to look. This walks
/// the whole of it: the notice that something is waiting, the card on the Place Archive page, Approve, and the
/// photo in the place page's slideshow for a signed-out visitor.
/// </remarks>
[TestFixture]
[Category("Places")]
[NonParallelizable]
public class PlaceFileReviewBrowserTests : BenTestBase
{
    private const string SeededPlace = "40000001-0000-0000-0000-000000000001";

    [Test]
    public async Task A_held_file_is_found_reviewed_and_shown_on_the_place_page()
    {
        var token = await SuperAdminTokenAsync();
        Assert.That(token, Is.Not.Null);
        using var api = new HttpClient { BaseAddress = new Uri(ApiUrl) };
        api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var caption = $"The upstairs window {Guid.NewGuid():N}"[..30];
        using var form = new MultipartFormDataContent();
        var photo = new ByteArrayContent(await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "room-photo-1.jpg")));
        photo.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(photo, "file", "room-photo-1.jpg");
        form.Add(new StringContent(caption), "caption");
        form.Add(new StringContent("1"), "kind");   // AboutThePlace: a picture of the building
        var added = await api.PostAsync($"/api/places/{SeededPlace}/evidence", form);
        Assert.That(added.IsSuccessStatusCode, Is.True, await added.Content.ReadAsStringAsync());
        var addedJson = await added.Content.ReadFromJsonAsync<JsonElement>();
        var id = addedJson.GetProperty("id").GetString()!;

        try
        {
            // Wherever a screener runs automatically it may have cleared the photo; hold it so there is
            // something to review either way.
            if (addedJson.GetProperty("showing").GetBoolean())
                (await api.PostAsJsonAsync($"/api/place-files/review/{id}", new { approve = false })).EnsureSuccessStatusCode();

            await LoginAsync(SuperAdminEmail, SuperAdminPassword);
            await Page.GotoAsync($"{BaseUrl}/moderation/archive");
            await WaitUntilLoadedAsync();

            var card = Page.Locator("[data-testid=place-file-row]", new() { HasTextString = caption });
            await Expect(card).ToBeVisibleAsync(new() { Timeout = 30_000 });
            await Expect(card.Locator("img")).ToBeVisibleAsync();
            Assert.That(await card.Locator("img").EvaluateAsync<bool>("i => i.complete && i.naturalWidth > 0"), Is.True,
                "the photo under review should load for the person deciding about it");

            await card.GetByTestId("approve-place-file").ClickAsync();
            await Expect(card).ToHaveCountAsync(0, new() { Timeout = 10_000 });

            var visitor = await Page.Context.Browser!.NewContextAsync();
            try
            {
                var page = await visitor.NewPageAsync();
                await page.GotoAsync($"{BaseUrl}/places/{SeededPlace}");
                var photos = page.GetByTestId("place-photos");
                await Expect(photos).ToBeVisibleAsync(new() { Timeout = 30_000 });
                await Expect(photos.Locator($"[title='{caption}'], img[alt='{caption}']").First).ToBeAttachedAsync();
            }
            finally { await visitor.CloseAsync(); }
        }
        finally
        {
            await api.DeleteAsync($"/api/places/{SeededPlace}/evidence/{id}");
        }
    }
}
