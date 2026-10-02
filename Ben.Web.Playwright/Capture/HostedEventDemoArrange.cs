using System.Text.Json;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Capture;

/// <summary>
/// Gives a test database the photographs and published events the hosted-events walk photographs.
/// </summary>
/// <remarks>
/// <para>Ben, 10/02/2026: run the hosted-events walk again for the documents. It had only ever run
/// on the shared testing copy, where phase 17c put the stock photographs on the two demo events, the
/// venue and a tour by hand (<c>ProjectNotes/FeatureHistory/README-hosted-events-235-media.md</c>).
/// On any other database the walk's first picture waited for a gallery that was not there, and the
/// whole story stopped before a single booking.</para>
///
/// <para>This does what that note says was done, through the same API and as the same SuperAdmin,
/// so every photograph goes through the site's own fitting and metadata stripping. Each step is
/// skipped when it is already done, so it can be run again. The tour is whichever the site lists
/// first, as <c>FlyerPageShots</c> finds it, rather than the one an old run happened to create.</para>
///
/// <para>Explicit: it publishes and uploads. Test databases only:
/// <c>scripts/run-e2e.sh --keep --filter "TestCategory=HostedEventDemoArrange"</c>.</para>
/// </remarks>
[TestFixture]
[Category("HostedEventDemoArrange")]
[Explicit("Uploads photographs and publishes events on the database it runs against; test databases only.")]
[NonParallelizable]
public sealed class HostedEventDemoArrange : BenTestBase
{
    private const string VenuePlaceId = "40000002-0000-0000-0000-000000000001";
    private const string RoomsEventId = "40000002-0000-0000-0000-000000000002";
    private const string SeatsEventId = "40000002-0000-0000-0000-000000000003";

    // The phase 17c choices, in the same order, so the lead picture is the same one.
    private static readonly (string File, string Caption)[] RoomsGallery =
    [
        ("v1-venue-exterior.jpg",      "The Thomas House at dusk"),
        ("v6-ballroom.jpg",            "The ballroom, where the séance is held"),
        ("v4-hallway-chandelier.jpg",  "The second-floor hall"),
        ("v5-hotel-bedroom.jpg",       "One of the six guest rooms"),
        ("d1-candlelit-dinner.jpg",    "Saturday's candlelit dinner"),
        ("s3-candle-chandelier.jpg",   "Candlelight in the parlor"),
        ("d3-plated-steak.jpg",        "The main course"),
    ];
    private static readonly (string File, string Caption)[] SeatsGallery =
    [
        ("t1-theatre-seats.jpg",           "The theater, before the doors open"),
        ("s1-candles.jpg",                 "Candles on the stage"),
        ("s2-ouija.jpg",                   "The evidence table"),
        ("t2-audience-presentation.jpg",   "The presentation"),
    ];
    private static readonly (string File, string Caption)[] VenuePhotos =
    [
        ("v1-venue-exterior.jpg",     "The Thomas House"),
        ("v3-hotel-lobby.jpg",        "The lobby"),
        ("v6-ballroom.jpg",           "The ballroom"),
        ("v4-hallway-chandelier.jpg", "The second-floor hall"),
        ("v5-hotel-bedroom.jpg",      "A guest room"),
        ("v7-hotel-bar.jpg",          "The bar"),
    ];
    private static readonly (string File, string Caption)[] TourGallery =
    [
        ("w1-cobblestone-streetlights.jpg", "The walk starts under the streetlights"),
        ("w2-cobblestone-night.jpg",        "Down the old cobblestones"),
        ("w3-foggy-graveyard.jpg",          "The last stop"),
    ];

    [Test]
    public async Task Put_the_demo_photographs_in_place_and_publish()
    {
        var orgId = await OrgIdBySlugAsync("paranormal365");
        var admin = await ApiAsAsync(SuperAdminEmail, SuperAdminPassword);

        await FillAsync(admin, $"/api/organizations/{orgId}/events/{RoomsEventId}/gallery", RoomsGallery);
        await FillAsync(admin, $"/api/organizations/{orgId}/events/{SeatsEventId}/gallery", SeatsGallery);

        foreach (var eventId in new[] { RoomsEventId, SeatsEventId })
        {
            var published = await admin.PostAsync($"/api/organizations/{orgId}/events/{eventId}/publish");
            TestContext.Out.WriteLine($"publish {eventId}: {published.Status} {Short(await published.TextAsync())}");
        }

        var profiles = await admin.GetAsync($"/api/organizations/{orgId}/venue-profiles");
        Assert.That(profiles.Ok, Is.True, $"venue profiles: {profiles.Status}");
        var profile = (await profiles.JsonAsync())!.Value.EnumerateArray()
            .FirstOrDefault(p => p.GetProperty("placeId").GetString() == VenuePlaceId);
        if (profile.ValueKind == JsonValueKind.Object)
            await FillAsync(admin, $"/api/organizations/{orgId}/venue-profiles/{profile.GetProperty("id").GetString()}/photos", VenuePhotos);
        else
            TestContext.Out.WriteLine("no venue profile for the demo venue in this group; venue photos skipped");

        var tours = await admin.GetAsync("/api/public/tours");
        Assert.That(tours.Ok, Is.True, $"tours: {tours.Status}");
        var first = (await tours.JsonAsync())!.Value.EnumerateArray().FirstOrDefault();
        if (first.ValueKind == JsonValueKind.Object)
            await FillAsync(admin,
                $"/api/organizations/{first.GetProperty("organizationId").GetString()}/tours/{first.GetProperty("id").GetString()}/gallery",
                TourGallery);
        else
            TestContext.Out.WriteLine("no public tour on this database; tour photos skipped");
    }

    /// <summary>Uploads the pictures to a gallery that has none yet.</summary>
    private async Task FillAsync(IAPIRequestContext api, string route, (string File, string Caption)[] pictures)
    {
        var existing = await api.GetAsync(route);
        Assert.That(existing.Ok, Is.True, $"{route}: {existing.Status} {Short(await existing.TextAsync())}");
        if ((await existing.JsonAsync())!.Value.GetArrayLength() > 0)
        {
            TestContext.Out.WriteLine($"{route}: already has pictures");
            return;
        }

        foreach (var (file, caption) in pictures)
        {
            var form = Context.APIRequest.CreateFormData();
            form.Append("file", new FilePayload
            {
                Name = file, MimeType = "image/jpeg",
                Buffer = await File.ReadAllBytesAsync(Path.Combine(RepoRoot(), "docs", "media", "stock", file)),
            });
            form.Append("caption", caption);
            var sent = await api.PostAsync(route, new() { Multipart = form });
            Assert.That(sent.Ok, Is.True, $"{route} {file}: {sent.Status} {Short(await sent.TextAsync())}");
        }
        TestContext.Out.WriteLine($"{route}: {pictures.Length} pictures");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx"))) dir = dir.Parent;
        Assert.That(dir, Is.Not.Null, "Could not find the repository root.");
        return dir!.FullName;
    }

    private static string Short(string text) => text.Length > 200 ? text[..200] : text;

    private async Task<IAPIRequestContext> ApiAsAsync(string email, string password)
    {
        await using var anon = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        var login = await anon.PostAsync("/login", new()
        {
            DataObject = new Dictionary<string, object> { ["email"] = email, ["password"] = password },
        });
        Assert.That(login.Ok, Is.True, $"API sign-in failed for {email}: {login.Status}");
        var token = (await login.JsonAsync())!.Value.GetProperty("accessToken").GetString()!;
        return await Playwright.APIRequest.NewContextAsync(new()
        {
            BaseURL = ApiUrl,
            ExtraHTTPHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" },
        });
    }
}
