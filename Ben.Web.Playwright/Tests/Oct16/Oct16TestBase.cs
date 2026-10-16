using System.Text.Json;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests.Oct16;

/// <summary>
/// What the 10/16/2026 fixtures share: signed-in API contexts, the seeded ids they lean on, the
/// theme switch, and the look-at-it checks (fits the screen, overlaps nothing, a picture kept).
/// </summary>
/// <remarks>
/// <para><b>Running them.</b> Every fixture is in this namespace, has "Oct16" in its class name
/// and carries <c>[Category("Oct16")]</c>, so <c>scripts/run-e2e.sh --filter Oct16</c> (dotnet
/// test reads a bare value as <c>FullyQualifiedName~Oct16</c>) and
/// <c>--filter TestCategory=Oct16</c> both run all of them and nothing else.</para>
///
/// <para><b>Pictures</b> go to <c>Ben.Web.Playwright/TestResults/oct16/</c>, which the repo's
/// <c>TestResults/</c> rule keeps out of git. Each is named
/// <c>{what}-{width}-{theme}.png</c>, so a 375px dark one and a 1280px light one sit side by
/// side.</para>
/// </remarks>
public abstract class Oct16TestBase : BenTestBase
{
    protected const string GroupSlug = "paranormal365";

    /// <summary>The seeded public landmark (DevelopmentDataSeeder.SeedSharedPlaceAsync), fixed id.</summary>
    protected const string BellWitchCaveId = "40000001-0000-0000-0000-000000000001";

    /// <summary>The seeded séance weekend: an Ask event, places asked for (HostedEventDemoSeeder).</summary>
    protected const string RoomsEventId = "40000002-0000-0000-0000-000000000002";

    /// <summary>The seeded 260-seat evening: a Pick event, places chosen off a plan.</summary>
    protected const string SeatsEventId = "40000002-0000-0000-0000-000000000003";

    private readonly List<IAPIRequestContext> _apis = [];

    [TearDown]
    public async Task DisposeTheApiContexts()
    {
        foreach (var api in _apis)
        {
            try { await api.DisposeAsync(); } catch { /* already gone */ }
        }
        _apis.Clear();
    }

    // ── The API, as somebody ────────────────────────────────────────────────

    /// <summary>An API context signed in as this person, disposed after the test.</summary>
    protected async Task<IAPIRequestContext> ApiAsAsync(string email, string password)
    {
        var anon = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        try
        {
            var login = await anon.PostAsync("/login", new() { DataObject = new { email, password } });
            Assert.That(login.Ok, Is.True, $"{email} could not sign in to the API: {await login.TextAsync()}");
            var token = (await login.JsonAsync())!.Value.GetProperty("accessToken").GetString();

            var api = await Playwright.APIRequest.NewContextAsync(new()
            {
                BaseURL = ApiUrl,
                ExtraHTTPHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" },
            });
            _apis.Add(api);
            return api;
        }
        finally
        {
            await anon.DisposeAsync();
        }
    }

    /// <summary>The body of a request that has to work, failing with the server's words when it did not.</summary>
    protected static async Task<JsonElement> OkJsonAsync(IAPIResponse response, string what)
    {
        Assert.That(response.Ok, Is.True, $"{what} failed ({response.Status}): {await response.TextAsync()}");
        return (await response.JsonAsync())!.Value;
    }

    /// <summary>The signed-in person's own account id, from their profile.</summary>
    protected static async Task<string> MyUserIdAsync(IAPIRequestContext api)
        => (await OkJsonAsync(await api.GetAsync("/api/me/profile"), "reading the profile"))
            .GetProperty("appUserId").GetString()!;

    /// <summary>A stored instant, as UTC whichever way the serializer wrote it.</summary>
    protected static DateTime AsUtc(JsonElement value)
    {
        var at = value.GetDateTime();
        return at.Kind switch
        {
            DateTimeKind.Utc => at,
            DateTimeKind.Local => at.ToUniversalTime(),
            _ => DateTime.SpecifyKind(at, DateTimeKind.Utc),
        };
    }

    /// <summary>An enum the API may write as a number or as a name.</summary>
    protected static bool IsEnum(JsonElement value, int number, string name)
        => value.ValueKind == JsonValueKind.Number
            ? value.GetInt32() == number
            : string.Equals(value.GetString(), name, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A public date on the group's calendar, on Central time, and the slug its public page lives
    /// at. The caller deletes it.
    /// </summary>
    protected static async Task<(string Id, string Slug)> PublicCalendarEventAsync(
        IAPIRequestContext owner, string orgId, string title, DateTime startUtc, DateTime endUtc)
    {
        var made = await owner.PostAsync($"/api/organizations/{orgId}/calendar", new()
        {
            DataObject = new
            {
                title, startDateTime = startUtc, endDateTime = endUtc,
                isAllDay = false, isPublic = true, timeZoneId = "America/Chicago",
            },
        });
        var json = await OkJsonAsync(made, $"making the public date \"{title}\"");
        return (json.GetProperty("id").GetString()!, json.GetProperty("urlName").GetString()!);
    }

    /// <summary>A wall-clock time in a zone, as UTC.</summary>
    protected static DateTime UtcFrom(DateTime wallClock, string ianaZone)
        => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified),
                                         TimeZoneInfo.FindSystemTimeZoneById(ianaZone));

    // ── The theme ───────────────────────────────────────────────────────────

    /// <summary>
    /// Draws the site in <paramref name="theme"/> from the next page load on: the stored setting the
    /// boot script reads first, and the colour scheme it falls back to.
    /// </summary>
    /// <remarks>
    /// Registered after BenTestBase's own (BEN_THEME) script, so this one wins whatever the run
    /// asked for — the visual tests say which theme they mean in their names.
    /// </remarks>
    protected async Task UseThemeAsync(string theme)
    {
        await Context.AddInitScriptAsync($$"""
            try {
                localStorage.setItem('layoutSettings', JSON.stringify({ theme: '{{theme}}' }));
                localStorage.setItem('ben-theme', '{{theme}}');
            } catch (e) { }
            """);
        await Page.EmulateMediaAsync(new() { ColorScheme = theme == "dark" ? ColorScheme.Dark : ColorScheme.Light });
    }

    // ── Looking at it ───────────────────────────────────────────────────────

    /// <summary>Ben.Web.Playwright/TestResults/oct16, found from wherever the DLL runs.</summary>
    protected static string ShotDir
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.Web.Playwright.csproj")))
                dir = dir.Parent;
            var root = dir?.FullName ?? AppContext.BaseDirectory;
            var shots = Path.Combine(root, "TestResults", "oct16");
            Directory.CreateDirectory(shots);
            return shots;
        }
    }

    /// <summary>
    /// Looks at one new piece of the page: visible, inside the screen's width, the page not sliding
    /// sideways, overlapping none of <paramref name="neighbours"/>, drawn in the theme asked for.
    /// Keeps a picture either way, and answers what was wrong (empty when nothing was).
    /// </summary>
    /// <remarks>
    /// The page-level rule is the one asked for: <c>document.documentElement.scrollWidth &lt;=
    /// innerWidth</c>. The piece's own box is checked against the same width, because a control
    /// inside a sideways-scrolling table (the investigations grid) can be off the screen while the
    /// page itself does not slide — the page passes and the person still cannot see the control.
    /// </remarks>
    protected async Task<List<string>> LookAtAsync(ILocator subject, string name, string theme, params ILocator[] neighbours)
    {
        var problems = new List<string>();
        var width = Page.ViewportSize?.Width ?? 0;
        var tag = $"{name} at {width}px ({theme})";

        try
        {
            await Expect(subject).ToBeVisibleAsync(new() { Timeout = 15_000 });
        }
        catch (PlaywrightException)
        {
            problems.Add($"{tag}: not visible.");
            await KeepAPictureAsync(name, theme);
            return problems;
        }

        await subject.ScrollIntoViewIfNeededAsync();
        await Page.WaitForTimeoutAsync(250);   // let a smooth scroll and any reflow finish

        var drawn = await Page.EvaluateAsync<string?>("() => document.documentElement.getAttribute('data-bs-theme')");
        if (drawn != theme) problems.Add($"{tag}: the page drew in '{drawn ?? "no theme"}', not '{theme}'.");

        var (scrollWidth, innerWidth) = await PageWidthsAsync();
        if (scrollWidth > innerWidth)
            problems.Add($"{tag}: the page slides sideways — scrollWidth {scrollWidth} > innerWidth {innerWidth}.");

        if (await subject.BoundingBoxAsync() is { } box)
        {
            if (box.X < -0.5 || box.X + box.Width > innerWidth + 0.5)
                problems.Add($"{tag}: runs past the screen — x {box.X:0} to {box.X + box.Width:0} on a {innerWidth}px screen.");
            if (box.Width < 1 || box.Height < 1)
                problems.Add($"{tag}: drawn with no size ({box.Width:0}×{box.Height:0}).");

            foreach (var neighbour in neighbours)
            {
                if (await neighbour.CountAsync() == 0 || !await neighbour.First.IsVisibleAsync()) continue;
                if (await neighbour.First.BoundingBoxAsync() is not { } other) continue;

                var overlapW = Math.Min(box.X + box.Width, other.X + other.Width) - Math.Max(box.X, other.X);
                var overlapH = Math.Min(box.Y + box.Height, other.Y + other.Height) - Math.Max(box.Y, other.Y);
                if (overlapW > 1 && overlapH > 1)
                    problems.Add($"{tag}: overlaps {neighbour} by {overlapW:0}×{overlapH:0}px.");
            }
        }
        else
        {
            problems.Add($"{tag}: has no box on the page.");
        }

        await KeepAPictureAsync(name, theme);
        return problems;
    }

    /// <summary>The page's scroll width and the window's inner width.</summary>
    protected async Task<(int ScrollWidth, int InnerWidth)> PageWidthsAsync()
    {
        var widths = await Page.EvaluateAsync<int[]>(
            "() => [document.documentElement.scrollWidth, window.innerWidth]");
        return (widths[0], widths[1]);
    }

    /// <summary>A picture of what is on the screen now, kept under TestResults/oct16.</summary>
    protected async Task KeepAPictureAsync(string name, string theme)
    {
        var width = Page.ViewportSize?.Width ?? 0;
        try
        {
            await Page.ScreenshotAsync(new()
            {
                Path = Path.Combine(ShotDir, $"{name}-{width}-{theme}.png"),
                Timeout = 15_000,
            });
        }
        catch (PlaywrightException)
        {
            // A picture is a courtesy; the assertions are the test.
        }
    }

    /// <summary>The two widths every new piece is looked at in: a phone and a laptop.</summary>
    protected static readonly (int Width, int Height)[] Widths = [(375, 812), (1280, 800)];

    /// <summary>Fails with every problem found, one per line — never only the first.</summary>
    protected static void NothingWrong(List<string> problems)
        => Assert.That(problems, Is.Empty, "What looked wrong:\n" + string.Join("\n", problems));
}
