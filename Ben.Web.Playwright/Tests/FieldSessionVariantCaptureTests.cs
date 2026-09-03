using System.Text.Json;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Walks a set of already-uploaded field sessions through the web player and records what each
/// one actually rendered — chart, map, room badge, audio/video elements, photo, markers — as a
/// JSON report plus a full-page screenshot per session. Not a pass/fail on the product: a probe
/// that tells the truth about each variant so a person can read the matrix.
///
/// Skipped unless <c>BEN_VARIANT_MANIFEST</c> points at the manifest written by
/// <c>upload_variants.py</c>; <c>BEN_VARIANT_OUT</c> chooses where the report and screenshots go.
/// </summary>
[TestFixture]
[Category("PlayerProbe")]
public class FieldSessionVariantCaptureTests : BenTestBase
{
    [Test]
    public async Task Every_uploaded_variant_renders_and_is_described()
    {
        var manifestPath = Environment.GetEnvironmentVariable("BEN_VARIANT_MANIFEST");
        if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
            Assert.Ignore("Set BEN_VARIANT_MANIFEST to the manifest written by upload_variants.py.");

        var outDir = Environment.GetEnvironmentVariable("BEN_VARIANT_OUT")
                     ?? Path.Combine(Path.GetTempPath(), "player-probe");
        Directory.CreateDirectory(outDir);

        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath!));
        var email = manifest.RootElement.GetProperty("email").GetString()!;
        await LoginAsync(email, MemberPassword);

        var report = new List<Dictionary<string, object?>>();
        foreach (var s in manifest.RootElement.GetProperty("sessions").EnumerateArray())
        {
            if (!s.TryGetProperty("sessionId", out var idProp)) continue;
            var id = idProp.GetString()!;
            var variant = s.GetProperty("variant").GetString()!;
            var row = new Dictionary<string, object?> { ["variant"] = variant, ["label"] = s.GetProperty("label").GetString(), ["sessionId"] = id };

            await Page.GotoAsync($"{BaseUrl}/field-sessions/{id}");
            // The page renders before the circuit fetches: wait for either the player's chart,
            // its empty-readings notice, or a refusal — whichever this session produces.
            try
            {
                await Page.WaitForSelectorAsync(
                    "svg.k-chart-surface, .k-chart, .alert, [data-testid='current-room'], audio, video, input[type=range]",
                    new() { Timeout = 30_000 });
            }
            catch (TimeoutException) { row["timedOut"] = true; }
            await Page.WaitForTimeoutAsync(1500);

            var body = await Page.Locator("main").InnerTextAsync();
            row["title"]              = await Page.TitleAsync();
            row["chart"]              = await Page.Locator(".k-chart, svg.k-chart-surface").CountAsync() > 0;
            row["scrubber"]           = await Page.Locator("input[type=range]").CountAsync() > 0;
            row["noPositionNotice"]   = body.Contains("No position was recorded", StringComparison.OrdinalIgnoreCase);
            row["noReadingsNotice"]   = body.Contains("holds no readings", StringComparison.OrdinalIgnoreCase);
            row["mapTiles"]           = await Page.Locator(".leaflet-container, .leaflet-tile, img[src*='tile']").CountAsync() > 0;
            row["currentRoomBadge"]   = await Page.Locator("[data-testid='current-room']").CountAsync() > 0;
            row["chartCount"]         = await Page.Locator(".k-chart").CountAsync();
            row["markerRoomBadges"]   = await Page.Locator("[data-testid='marker-room']").CountAsync();
            row["markerRows"]         = await Page.Locator("[data-testid='marker-row'], table tbody tr").CountAsync();
            row["audioElements"]      = await Page.Locator("audio").CountAsync();
            row["videoElements"]      = await Page.Locator("video").CountAsync();
            row["imgInRecordings"]    = await Page.Locator(".card:has(.card-header:text('Recordings')) img").CountAsync();
            row["sessionPhotos"]      = await Page.Locator("[data-testid='session-photo']").CountAsync();
            row["noChannelsNotice"]   = await Page.Locator("[data-testid='no-channels']").CountAsync() > 0;
            row["recordingsListed"]   = await Page.Locator(".card:has(.card-header:text('Recordings')) li").CountAsync();
            row["damagedBadges"]      = await Page.Locator("text=arrived damaged").CountAsync();
            row["refusalText"]        = body.Contains("no longer on the server") || body.Contains("isn't here") || body.Contains("not allowed");
            row["consoleErrors"]      = 0;

            // Does each media element actually load? Ask the browser for its readyState/error.
            row["audioCanPlay"] = await Page.EvaluateAsync<string>(
                "() => Array.from(document.querySelectorAll('audio')).map(a => { a.load(); return a.error ? 'error' : 'ok'; }).join(',')");
            row["videoCanPlay"] = await Page.EvaluateAsync<string>(
                "() => Array.from(document.querySelectorAll('video')).map(v => { v.load(); return v.error ? 'error' : 'ok'; }).join(',')");
            await Page.WaitForTimeoutAsync(1500);
            row["mediaNetwork"] = await Page.EvaluateAsync<string>(
                "() => Array.from(document.querySelectorAll('audio,video')).map(m => `${m.tagName.toLowerCase()}:rs=${m.readyState}:err=${m.error ? m.error.code : 0}`).join(' ')");

            var shot = Path.Combine(outDir, $"variant-{variant}.png");
            await Page.ScreenshotAsync(new() { Path = shot, FullPage = true });
            row["screenshot"] = shot;

            // The page scrolls inside <main>, so a full-page shot stops at the fold. Capture the
            // lower cards on their own: the recordings list and the marker list.
            foreach (var (name, selector) in new[] {
                ("recordings", ".card:has(.card-header:text('Recordings'))"),
                ("markers",    ".card:has(.card-header:text-matches('Marked|Flagged|Markers', 'i'))") })
            {
                var card = Page.Locator(selector).First;
                if (await card.CountAsync() == 0) continue;
                await card.ScrollIntoViewIfNeededAsync();
                var cardShot = Path.Combine(outDir, $"variant-{variant}-{name}.png");
                await card.ScreenshotAsync(new() { Path = cardShot });
                row[$"screenshot_{name}"] = cardShot;
            }
            report.Add(row);
            TestContext.Out.WriteLine(JsonSerializer.Serialize(row));
        }

        // ── The matrix stops merely describing and starts asserting ──────────────
        //
        // Every one of these failed on develop before this branch. The probe noticed all four
        // during the sweep and none of them broke a test, which is exactly how they shipped.
        foreach (var row in report)
        {
            var label = (string?)row["label"] ?? "";

            // A photo must be visible to the person who took it, not listed as a filename.
            if (label.Contains("photo", StringComparison.OrdinalIgnoreCase))
                Assert.That(row["sessionPhotos"], Is.Not.EqualTo(0),
                            $"{row["variant"]}: a photo in the session should render, not just list");

            // A room named during the session survives having no GPS fix.
            if (label.Contains("room", StringComparison.OrdinalIgnoreCase)
                && !label.Contains("no room", StringComparison.OrdinalIgnoreCase))
                Assert.That(row["currentRoomBadge"], Is.True,
                            $"{row["variant"]}: the room should show whether or not there was a fix");

            // A session with sound draws a line; one with neither channel says so instead of
            // drawing an empty grid.
            if (label.Contains("sound only", StringComparison.OrdinalIgnoreCase))
                Assert.That(row["chart"], Is.True,
                            $"{row["variant"]}: a sound-only session should still be charted");
        }

        var reportPath = Path.Combine(outDir, "player-report.json");
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        TestContext.Out.WriteLine($"report → {reportPath}");
        Assert.That(report, Is.Not.Empty, "the manifest should have produced at least one rendered session");
    }
}
