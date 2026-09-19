using Microsoft.Playwright;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The feed arc's later phases walked end to end (item 186 F10): media + screening, categories,
/// attribution, the counted ad door, the home teaser, and the new-posts pill.
/// </summary>
/// <remarks>
/// <para>Same flag discipline as <see cref="FeedTests"/>: the feed is turned on for the fixture
/// and put back exactly as found, because the site's resting state is dark.</para>
///
/// <para>Where a walk needs a post that only the API can shape (a case-derived render with
/// recorded consent), the post is seeded through the API and the UI is what is walked — the
/// point of those tests is the attribution page and the card, not the composer.</para>
/// </remarks>
[TestFixture]
[Category("Feed")]
[NonParallelizable]
public class FeedArcTests : BenTestBase
{
    private const string FeedFlag = "features.public-feed";
    private static bool _wasAlreadyOn;

    [OneTimeSetUp]
    public async Task TurnTheFeedOn()
    {
        var token = await TokenAsync(SuperAdminEmail, SuperAdminPassword);
        if (token is null) return;

        using var http = Api(token);
        var settings = await http.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/admin/site-settings");
        foreach (var setting in settings.EnumerateArray())
        {
            if (setting.GetProperty("key").GetString() != FeedFlag) continue;
            _wasAlreadyOn = setting.TryGetProperty("value", out var value)
                && string.Equals(value.GetString(), "true", StringComparison.OrdinalIgnoreCase);
        }
        await http.PutAsJsonAsync($"/api/admin/site-settings/{FeedFlag}", new { value = "true" });

        // The website caches site settings for up to 30 seconds, so the first tests of this
        // fixture would otherwise run against a page still rendering the feed as off — the
        // same wait the capture fixture documents. Skipped when the flag was already on.
        if (!_wasAlreadyOn) await Task.Delay(35_000);
    }

    [OneTimeTearDown]
    public async Task PutTheFeedBack()
    {
        var token = await TokenAsync(SuperAdminEmail, SuperAdminPassword);
        if (token is null) return;
        using var http = Api(token);
        await http.PutAsJsonAsync($"/api/admin/site-settings/{FeedFlag}",
            new { value = _wasAlreadyOn ? "true" : "false" });
    }

    // ── Media, screening, and the category chip (F4/F5b/F6) ──────────────────

    [Test]
    [Description("A photo post shows its image (auto-screened) or the honest wait, wears its " +
                 "category chip, and the chip leads to the type's page.")]
    public async Task MediaPostWearsItsCategoryAndTheChipLeadsSomewhere()
    {
        var tag = $"t{Guid.NewGuid():N}"[..12];

        await LoginAsync(MemberEmail, MemberPassword);
        await GoToFeedAsync();

        // Attach first: the category select only exists once media is chosen.
        var fileInput = Page.Locator(".card:has(#feed-composer) input[type=file]");
        var photo = WriteTempPng();
        try
        {
            await fileInput.SetInputFilesAsync(photo);

            // The taxonomy select arrives after an async fetch; picking "Apparition" claims a
            // Visual type a bright test image will not be nudged for.
            var select = Page.Locator("#feed-composer-category select");
            await Expect(select).ToBeVisibleAsync(new() { Timeout = 15_000 });
            await select.SelectOptionAsync(new SelectOptionValue { Label = "Apparition" });

            await ComposeAsync($"an apparition on the stairs #{tag}");

            var post = Page.Locator(".bv-feed-post", new() { HasTextString = tag }).First;
            await Expect(post).ToBeVisibleAsync(new() { Timeout = 20_000 });

            // Screening decides which honest state renders: the image (automatic screener
            // approved it inline) or the author-only "being checked" note (manual screener on a
            // machine without the model). EITHER is correct; a broken image or nothing is not.
            var media = post.Locator(".bv-feed-post__media img");
            var pending = post.Locator("#feed-media-pending");
            await Expect(media.Or(pending).First).ToBeVisibleAsync(new() { Timeout = 20_000 });

            // The chip, and where it goes.
            var chip = post.Locator(".bv-feed-category");
            await Expect(chip).ToHaveTextAsync("Apparition", new() { UseInnerText = true, Timeout = 10_000 });
            await chip.ClickAsync();
            await Expect(Page).ToHaveURLAsync(new Regex(".*/feed/types/.*"), new() { Timeout = 15_000 });
            await Expect(Page.Locator(".bv-feed-post", new() { HasTextString = tag }).First)
                .ToBeVisibleAsync(new() { Timeout = 20_000 });
        }
        finally
        {
            File.Delete(photo);
        }
    }

    // ── Attribution: the group's name is the group's call (F7) ───────────────

    [Test]
    [Description("A case-derived post shows no group name until the group claims it; claiming " +
                 "puts the name and the Group-verified badge on the card.")]
    public async Task ClaimingAttributionNamesTheGroupOnTheCard()
    {
        var tag = $"t{Guid.NewGuid():N}"[..12];

        // Seed the case-derived post through the API — the editor's own door, minus the editor.
        var memberToken = await TokenAsync(MemberEmail, MemberPassword);
        Assert.That(memberToken, Is.Not.Null, "member sign-in failed");

        using var memberApi = Api(memberToken!);
        var orgs = await memberApi.GetFromJsonAsync<System.Text.Json.JsonElement>(
            "/api/security/organizations/my-memberships");
        var orgId = orgs.EnumerateArray()
            .First(o => o.GetProperty("name").GetString() == "BenCo")
            .GetProperty("organizationId").GetString()!;
        var cases = await memberApi.GetFromJsonAsync<System.Text.Json.JsonElement>(
            $"/api/organizations/{orgId}/cases");
        var caseId = cases.EnumerateArray().First().GetProperty("id").GetString()!;

        var postId = await PostWithMediaAsync(memberApi, $"render from the case #{tag}",
            sourceCaseId: caseId, consent: true);

        // The thread page, before the claim: no group name anywhere on the card.
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/feed/{postId}");
        var card = Page.Locator(".bv-feed-post", new() { HasTextString = tag }).First;
        await Expect(card).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Expect(card.Locator(".bv-feed-attribution")).ToHaveCountAsync(0);

        // The group's queue: claim it.
        await Page.GotoAsync($"{BaseUrl}/organizations/{orgId}/feed-attributions");
        var row = Page.Locator(".card", new() { HasTextString = tag }).First;
        await Expect(row).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await row.GetByRole(AriaRole.Button, new() { Name = "Claim — link it to us" }).ClickAsync();
        await Expect(row.Locator(".badge", new() { HasTextString = "Claimed" }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        // And now the card wears the name and the badge.
        await Page.GotoAsync($"{BaseUrl}/feed/{postId}");
        card = Page.Locator(".bv-feed-post", new() { HasTextString = tag }).First;
        await Expect(card.Locator(".bv-feed-attribution")).ToHaveTextAsync("BenCo",
            new() { UseInnerText = true, Timeout = 20_000 });
        await Expect(card.Locator(".bv-feed-badge", new() { HasTextString = "Group verified" }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    // ── /go: the counted door (F8) ───────────────────────────────────────────

    [Test]
    [Description("/go counts the click and lands on the group's page; a bogus id lands on /find.")]
    public async Task GoRedirectsThroughTheClosedSetAndCounts()
    {
        var token = await TokenAsync(SuperAdminEmail, SuperAdminPassword);
        Assert.That(token, Is.Not.Null);
        using var http = Api(token!);

        var orgs = await http.GetFromJsonAsync<System.Text.Json.JsonElement>(
            "/api/security/organizations/my-memberships");
        var orgId = orgs.EnumerateArray()
            .First(o => o.GetProperty("name").GetString() == "BenCo")
            .GetProperty("organizationId").GetString()!;

        // Through the real review chain, and withdrawn again in finally — an approved test ad
        // must not keep rotating on the live placements after the walk. One card per group is
        // the rule, so REUSE an existing row (editing pulls it back to Draft) before creating.
        var adPayload = new
        {
            headline = "e2e walk ad", body = "temporary", imageUploadFileId = (Guid?)null,
            targetKind = "org",
        };
        var existing = await http.GetFromJsonAsync<System.Text.Json.JsonElement>(
            $"/api/organizations/{orgId}/ads");
        string adId;
        if (existing.EnumerateArray().Any())
        {
            adId = existing.EnumerateArray().First().GetProperty("id").GetString()!;
            using var edited = await http.PutAsJsonAsync(
                $"/api/organizations/{orgId}/ads/{adId}", adPayload);
            Assert.That(edited.IsSuccessStatusCode, Is.True,
                $"reusing the group's ad failed: {await edited.Content.ReadAsStringAsync()}");
        }
        else
        {
            var created = await http.PostAsJsonAsync($"/api/organizations/{orgId}/ads", adPayload);
            Assert.That(created.IsSuccessStatusCode, Is.True,
                $"creating the ad failed: {await created.Content.ReadAsStringAsync()}");
            var ad = await created.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            adId = ad.GetProperty("id").GetString()!;
        }
        try
        {
            await http.PostAsync($"/api/organizations/{orgId}/ads/{adId}/submit", null);
            await http.PostAsync($"/api/admin/organization-ads/{adId}/approve", null);

            await Page.GotoAsync($"{BaseUrl}/go/{adId}");
            await Expect(Page).ToHaveURLAsync(new Regex(".*/o/benco.*"), new() { Timeout = 15_000 });

            var clicks = await http.GetFromJsonAsync<System.Text.Json.JsonElement>(
                $"/api/organizations/{orgId}/ads");
            Assert.That(clicks.EnumerateArray().First(a => a.GetProperty("id").GetString() == adId)
                .GetProperty("clicks").GetInt64(), Is.GreaterThanOrEqualTo(1));

            await Page.GotoAsync($"{BaseUrl}/go/{Guid.NewGuid()}");
            await Expect(Page).ToHaveURLAsync(new Regex(".*/find.*"), new() { Timeout = 15_000 });
        }
        finally
        {
            await http.PostAsync($"/api/organizations/{orgId}/ads/{adId}/withdraw", null);
        }
    }

    // ── The home teaser (F9) ─────────────────────────────────────────────────

    [Test]
    [Description("The landing page shows the feed's top posts to a visitor with no account.")]
    public async Task HomeTeaserShowsTheFeedToAVisitor()
    {
        // Make sure there is something to tease.
        var token = await TokenAsync(MemberEmail, MemberPassword);
        Assert.That(token, Is.Not.Null);
        using var http = Api(token!);
        await PostWithMediaAsync(http, $"teaser fodder #t{Guid.NewGuid():N}"[..40],
            sourceCaseId: null, consent: false, includeMedia: false);

        await Page.GotoAsync(BaseUrl); // no login: the visitor is the audience
        var teaser = Page.Locator("#feed-teaser");
        await Expect(teaser).ToBeVisibleAsync(new() { Timeout = 25_000 });
        await Expect(teaser.Locator(".bv-feed-teaser-row").First).ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    // ── The new-posts pill (F9) ──────────────────────────────────────────────

    [Test]
    [Description("A post made elsewhere surfaces as the pill within a poll cycle, and clicking " +
                 "it brings the post in.")]
    public async Task NewPostsPillSurfacesAPostMadeElsewhere()
    {
        var tag = $"t{Guid.NewGuid():N}"[..12];

        await LoginAsync(MemberEmail, MemberPassword);
        await GoToFeedAsync();
        // Latest: the tab where "new" has a stable meaning and the click prepends.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Latest" }).ClickAsync();
        await Expect(Page.Locator(".bv-feed-post").First).ToBeVisibleAsync(new() { Timeout = 20_000 });

        // The second author posts through the API — the pill's whole job is noticing them.
        var token = await TokenAsync(SuperAdminEmail, SuperAdminPassword);
        Assert.That(token, Is.Not.Null);
        using var http = Api(token!);
        await PostWithMediaAsync(http, $"posted from elsewhere #{tag}",
            sourceCaseId: null, consent: false, includeMedia: false);

        // One 45s poll cycle plus slack.
        await Expect(Page.Locator("#feed-new-posts")).ToBeVisibleAsync(new() { Timeout = 70_000 });
        await Page.Locator("#feed-new-posts button").ClickAsync();
        await Expect(Page.Locator(".bv-feed-post", new() { HasTextString = tag }).First)
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static HttpClient Api(string token)
    {
        var http = new HttpClient { BaseAddress = new Uri(ApiUrl), Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    private static async Task<string?> TokenAsync(string email, string password)
    {
        using var http = new HttpClient { BaseAddress = new Uri(ApiUrl), Timeout = TimeSpan.FromSeconds(30) };
        try
        {
            using var response = await http.PostAsJsonAsync("/login", new { email, password });
            if (!response.IsSuccessStatusCode) return null;
            var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            return json.GetProperty("accessToken").GetString();
        }
        catch (HttpRequestException) { return null; }
    }

    /// <summary>A feed post through the API's own multipart door.</summary>
    private static async Task<string> PostWithMediaAsync(
        HttpClient http, string body, string? sourceCaseId, bool consent, bool includeMedia = true)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(body), "Body");
        if (sourceCaseId is not null) form.Add(new StringContent(sourceCaseId), "SourceCaseId");
        if (consent) form.Add(new StringContent("true"), "ConsentToPublishPrivateEngagement");
        if (includeMedia)
        {
            var png = new ByteArrayContent(TinyPng());
            png.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(png, "media", "walk.png");
        }

        using var response = await http.PostAsync("/api/feed/posts", form);
        Assert.That(response.IsSuccessStatusCode, Is.True,
            $"seeding post failed: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        return json.GetProperty("id").GetString()!;
    }

    private static string WriteTempPng()
    {
        var path = Path.Combine(Path.GetTempPath(), $"feed-walk-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(path, TinyPng());
        return path;
    }

    /// <summary>
    /// A genuinely decodable 8×8 PNG, built by hand — the ingest pipeline DECODES uploads, so a
    /// fake byte blob would be refused, and this project deliberately carries no image library.
    /// </summary>
    private static byte[] TinyPng()
    {
        const int size = 8;
        // Raw image data: each row = filter byte 0 + RGB pixels (mid gray).
        var raw = new byte[size * (1 + size * 3)];
        for (var y = 0; y < size; y++)
            for (var i = 0; i < size * 3; i++)
                raw[(y * (1 + size * 3)) + 1 + i] = 0x80;

        using var compressed = new MemoryStream();
        // zlib wrapper: header 0x78 0x9C, deflate body, Adler-32 trailer.
        compressed.WriteByte(0x78);
        compressed.WriteByte(0x9C);
        using (var deflate = new DeflateStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
            deflate.Write(raw);
        uint a = 1, b = 0;
        foreach (var value in raw) { a = (a + value) % 65521; b = (b + a) % 65521; }
        compressed.Write(BigEndian((b << 16) | a));

        using var png = new MemoryStream();
        png.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        WriteChunk(png, "IHDR", [
            .. BigEndian(size), .. BigEndian(size),
            8, 2, 0, 0, 0, // 8-bit, truecolor RGB
        ]);
        WriteChunk(png, "IDAT", compressed.ToArray());
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static byte[] BigEndian(long value) => BigEndian((uint)value);
    private static byte[] BigEndian(uint value) =>
        [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        stream.Write(BigEndian((uint)data.Length));
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);
        stream.Write(BigEndian(Crc32([.. typeBytes, .. data])));
    }

    private static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ (0xEDB88320 & (uint)-(crc & 1));
        }
        return crc ^ 0xFFFFFFFF;
    }

    // ── Page helpers (mirrors of FeedTests' private ones) ────────────────────

    private async Task GoToFeedAsync()
    {
        await Page.GotoAsync($"{BaseUrl}/feed");
        await Page.Locator("#feed-composer, #feed-join-prompt").First
            .WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = 30_000 });
    }

    private async Task ComposeAsync(string body)
    {
        var box = Page.Locator("#feed-composer");
        var post = Page.GetByRole(AriaRole.Button, new() { Name = "Post", Exact = true });

        for (var attempt = 0; attempt < 10; attempt++)
        {
            await box.FillAsync(body);
            try
            {
                await Expect(post).ToBeEnabledAsync(new() { Timeout = 1_500 });
                await post.ClickAsync();
                return;
            }
            catch (Exception)
            {
                // Circuit not live yet; the value was discarded. Try again.
            }
        }
        Assert.Fail("The composer never accepted the post — the page is not becoming interactive.");
    }
}
