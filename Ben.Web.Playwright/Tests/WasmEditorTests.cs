using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The standalone WebAssembly editor host (<c>Ben.Wasm.Video</c>, :5180).
/// </summary>
/// <remarks>
/// <para>Everything else in this suite drives the site, where the editor is one page inside a
/// signed-in Blazor Server circuit. The WASM host is a different program in the ways that matter:
/// it runs the editor's HttpClients <i>in the browser</i>, so it needs its own sign-in and the API
/// must allow its origin. Those two facts are exactly what nothing covered.</para>
///
/// <para><b>The sign-in point is the product point.</b> Signed out, the host can still edit and
/// export locally — but the Server tab is empty, because listing someone's media is an
/// authenticated call. Anything that reaches the server (listing media, saving a project, publishing
/// a render) waits until the person has proved who they are. These tests hold that line from both
/// sides.</para>
///
/// <para>Runs against <c>BEN_WASM_URL</c> (default <c>http://localhost:5180</c>), with the API and
/// the seeded demo media in place — see <c>DevelopmentDataSeeder.SeedVideoEditorMediaAsync</c>.</para>
/// </remarks>
[TestFixture]
[Category("Wasm")]
[NonParallelizable]
public class WasmEditorTests : BenTestBase
{
    private static string WasmUrl =>
        Environment.GetEnvironmentVariable("BEN_WASM_URL") ?? "http://localhost:5180";

    public override BrowserNewContextOptions ContextOptions() => new()
    {
        ViewportSize = new ViewportSize { Width = 1440, Height = 900 },
    };

    /// <summary>Loads a route on the WASM host and waits for the app to boot.</summary>
    private async Task GoAsync(string route = "/")
    {
        await Page.GotoAsync($"{WasmUrl}{route}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // A WebAssembly host downloads and starts a runtime before it renders anything, which is
        // far slower than a server-rendered page and has its own failure mode: a stale
        // fingerprinted framework file 404s and the app sits on "Loading" forever.
        await Expect(Page.Locator("#app")).ToBeVisibleAsync(new() { Timeout = 60_000 });
    }

    private async Task SignInAsync()
    {
        await GoAsync("/login");

        await Page.FillAsync("#email", UserEmail);
        await Page.FillAsync("#password", UserPassword);
        await Page.ClickAsync("button[type='submit']");

        await Expect(Page.Locator(".bv-editor")).ToBeVisibleAsync(new() { Timeout = 60_000 });
    }

    /// <summary>Opens one of the media panel's tabs.</summary>
    private async Task OpenMediaTabAsync(string name)
    {
        var tab = Page.GetByRole(AriaRole.Tab, new() { Name = name, Exact = true })
                      .Or(Page.Locator(".k-tabstrip-item", new() { HasTextString = name }))
                      .First;
        await Expect(tab).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await tab.ClickAsync();
        await Page.WaitForTimeoutAsync(800);
    }

    [Test]
    [Description("The host boots and renders the editor, with no framework files missing.")]
    public async Task TheEditorLoads()
    {
        var failed = new List<string>();
        Page.Response += (_, r) =>
        {
            if (r.Status >= 400 && r.Url.Contains("_framework")) failed.Add($"{r.Status} {r.Url}");
        };

        await GoAsync();

        await Expect(Page.Locator(".bv-editor")).ToBeVisibleAsync(new() { Timeout = 60_000 });
        await Expect(Page.Locator(".bv-timeline")).ToBeVisibleAsync();

        Assert.That(failed, Is.Empty,
            "Framework files 404'd, which leaves the host stuck on its loading spinner. This is what "
            + "a rebuild without restarting the dev server looks like:\n  " + string.Join("\n  ", failed));
    }

    [Test]
    [Description("The editor wears the site's palette here too, not the stock Kendo theme.")]
    public async Task TheEditorUsesTheSitePalette()
    {
        await GoAsync();
        await Expect(Page.Locator(".bv-editor")).ToBeVisibleAsync(new() { Timeout = 60_000 });

        var surface = await Page.EvaluateAsync<string>(
            "getComputedStyle(document.documentElement).getPropertyValue('--bv-surface-1').trim()");
        var bodyBg = await Page.EvaluateAsync<string>(
            "getComputedStyle(document.documentElement).getPropertyValue('--bs-body-bg').trim()");

        Assert.That(surface, Is.Not.Empty, "The editor's own surface token is not defined at all.");
        Assert.That(surface, Is.EqualTo(bodyBg),
            "The editor's ground no longer follows the template's body colour, so the host and the "
            + "editor are painting two different backgrounds.");
    }

    [Test]
    [Description("The theme toggle flips the theme and remembers it under the template's key.")]
    public async Task TheThemeTogglePersists()
    {
        await GoAsync();
        await Expect(Page.Locator(".bv-editor")).ToBeVisibleAsync(new() { Timeout = 60_000 });

        var before = await Page.EvaluateAsync<string?>(
            "document.documentElement.getAttribute('data-bs-theme')");

        await Page.ClickAsync(".bwv-theme-toggle");

        var after = await Page.EvaluateAsync<string?>(
            "document.documentElement.getAttribute('data-bs-theme')");
        var stored = await Page.EvaluateAsync<string?>("localStorage.getItem('ben-theme')");

        Assert.That(after, Is.Not.EqualTo(before), "The toggle did not change the theme.");
        Assert.That(stored, Is.EqualTo(after),
            "The choice was not written back to the key the site reads, so it will not carry over.");
    }

    [Test]
    [Description("Signed out, the Server tab lists nothing — media is an authenticated call.")]
    public async Task SignedOut_TheServerTabHasNoMedia()
    {
        await GoAsync();
        await Expect(Page.Locator(".bv-editor")).ToBeVisibleAsync(new() { Timeout = 60_000 });

        await OpenMediaTabAsync("Server");

        var cards = await Page.Locator(".bv-clip-card").CountAsync();
        Assert.That(cards, Is.Zero,
            "Someone who has not signed in was shown server media. Listing a person's files is an "
            + "authenticated call, and this host has no session until they sign in.");
    }

    /// <summary>
    /// The Server tab's scope selector narrows the list without leaving the tab.
    /// </summary>
    /// <remarks>
    /// <para>This asserts the wiring — that the selector exists, that choosing a scope reaches the
    /// server, and that the cascade appears — rather than which files come back. What each scope
    /// <i>means</i>, and the property that a scope can never widen what somebody may see, is held
    /// by MediaLibraryControllerTests, where it can be stated against known data.</para>
    ///
    /// <para>The half-made selection is worth its own assertion: "By case" with no case chosen
    /// must empty the list and say so, rather than leaving the previous scope's files sitting
    /// under a selector that no longer describes them.</para>
    /// </remarks>
    [Test]
    [Description("The Server tab offers All / My files / By case, and the case choice cascades.")]
    public async Task TheServerTabCanBeScoped()
    {
        await SignInAsync();
        await OpenMediaTabAsync("Server");

        var scopeSelect = Page.Locator(".bv-browser__scope-select").First;
        await Expect(scopeSelect).ToBeVisibleAsync(new() { Timeout = 20_000 });

        var options = await scopeSelect.Locator("option").AllInnerTextsAsync();
        Assert.That(options.Select(o => o.Trim()),
            Is.EquivalentTo(new[] { "All media", "My files", "By case" }),
            "The scope selector did not offer the three scopes. 'By case' is only offered when the "
            + "host supplies the groups, so its absence means the scopes endpoint returned nothing.");

        // Personal is a real round trip, not a client-side filter — it must not error.
        await scopeSelect.SelectOptionAsync("personal");
        await Expect(Page.Locator(".bv-browser__error")).ToHaveCountAsync(0, new() { Timeout = 15_000 });

        // By case, with nothing chosen yet: a second dropdown, and a list that says what to do.
        await scopeSelect.SelectOptionAsync("case");
        await Expect(Page.Locator(".bv-browser__scope-select")).ToHaveCountAsync(2, new() { Timeout = 15_000 });
        await Expect(Page.Locator(".bv-browser__empty"))
            .ToContainTextAsync("Choose a case", new() { Timeout = 15_000 });

        // And choosing one reloads against that case.
        var caseSelect = Page.Locator(".bv-browser__scope-select").Nth(1);
        var values = await caseSelect.Locator("option").EvaluateAllAsync<string[]>("os => os.map(o => o.value)");
        var firstCase = values.FirstOrDefault(v => !string.IsNullOrEmpty(v));
        Assert.That(firstCase, Is.Not.Null, "No cases were offered to scope by.");

        await caseSelect.SelectOptionAsync(firstCase!);
        await Expect(Page.Locator(".bv-browser__error")).ToHaveCountAsync(0, new() { Timeout = 15_000 });
    }

    /// <summary>
    /// The diagnostics panel is an operator tool, and this host now knows who is looking.
    /// </summary>
    /// <remarks>
    /// <para>Sign-in here goes through <c>MapIdentityApi</c>, which returns tokens and no claims —
    /// there is no principal on the client to read a role from. So the page asks
    /// <c>GET /api/me</c>. Before this, <c>ShowDiagnostics</c> was simply left unset: safe, but
    /// administrators lost the panel on this host entirely.</para>
    ///
    /// <para>Both directions are asserted. A test that only checked the panel was hidden would
    /// pass against the previous behaviour, which hid it from everybody — including the people it
    /// exists for.</para>
    /// </remarks>
    [Test]
    [Description("An ordinary account does not get the ffmpeg diagnostics chip.")]
    public async Task SignedInAsAnOrdinaryUser_TheDiagnosticsChipIsHidden()
    {
        await SignInAsync();

        await Expect(Page.Locator(".bv-diagnostics-chip"))
            .ToHaveCountAsync(0, new() { Timeout = 20_000 });
    }

    [Test]
    [Description("A platform administrator does get it.")]
    public async Task SignedInAsAnAdministrator_TheDiagnosticsChipIsShown()
    {
        await GoAsync("/login");
        await Page.FillAsync("#email", SuperAdminEmail);
        await Page.FillAsync("#password", SuperAdminPassword);
        await Page.ClickAsync("button[type='submit']");

        await Expect(Page.Locator(".bv-editor")).ToBeVisibleAsync(new() { Timeout = 60_000 });
        await Expect(Page.Locator(".bv-diagnostics-chip"))
            .ToBeVisibleAsync(new() { Timeout = 30_000 });
    }

    [Test]
    [Description("Signed in, the Server tab lists the person's own media.")]
    public async Task SignedIn_TheServerTabListsMedia()
    {
        await SignInAsync();
        await OpenMediaTabAsync("Server");

        await Expect(Page.GetByText("porch-camera.mp4").First)
            .ToBeVisibleAsync(new() { Timeout = 20_000 });
    }

    [Test]
    [Description("A file from the server reaches the timeline: download, cache, then place it.")]
    public async Task AnImportedClipReachesTheTimeline()
    {
        await SignInAsync();

        // Loading the ffmpeg core is what makes placing a clip possible; downloading does not
        // need it, which is why the two steps are separate.
        var status = Page.Locator(".bv-toolbar__status");
        if (!(await status.InnerTextAsync()).Contains("Ready", StringComparison.OrdinalIgnoreCase))
        {
            await Page.GetByRole(AriaRole.Button, new() { Name = "Initialize" }).First.ClickAsync();
            await Expect(status).ToContainTextAsync("Ready", new() { Timeout = 180_000 });
        }

        await OpenMediaTabAsync("Server");

        var card = Page.Locator(".bv-clip-card").Filter(new() { HasTextString = "porch-camera" }).First;
        await Expect(card).ToBeVisibleAsync(new() { Timeout = 20_000 });

        // The card's middle is its thumbnail, which carries a preview button that stops
        // propagation; the name block bubbles to the card's own handler.
        var target = card.Locator(".bv-clip-card__info").First;

        await target.ClickAsync();                                   // download + cache
        await Expect(card.Locator(".bv-clip-card__cached-badge"))
            .ToBeVisibleAsync(new() { Timeout = 120_000 });

        await target.ClickAsync();                                   // place on the timeline

        var chip = Page.Locator(".bv-clip-chip").First;
        await Expect(chip).ToBeVisibleAsync(new() { Timeout = 180_000 });

        // Name the clip, not just "a chip exists": a timeline that starts with placeholder chips
        // would satisfy the weaker check without anything having been imported.
        await Expect(chip).ToContainTextAsync("porch-camera", new() { Timeout = 30_000 });

        TestContext.Out.WriteLine($"timeline chips: {await Page.Locator(".bv-clip-chip").CountAsync()}");
    }
}
