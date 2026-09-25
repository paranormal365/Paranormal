using System.Text.Json;
using Microsoft.Playwright;

namespace Ben.Canvas.Playwright.Tests;

/// <summary>
/// The standalone host boots looking like the site, at desktop, iPad and iPhone sizes.
/// </summary>
[Category("Shell")]
[TestFixture(DeviceKind.Desktop)]
[TestFixture(DeviceKind.Tablet)]
[TestFixture(DeviceKind.Phone)]
public sealed class HostShellTests(DeviceKind device) : CanvasTestBase(device)
{
    [Test]
    public async Task The_editor_shell_renders_dark_with_a_sign_in_chip()
    {
        await StartCleanAsync();

        await Expect(Page.Locator("html")).ToHaveAttributeAsync("data-bs-theme", "dark");
        await Expect(Page.Locator(".bc-editor")).ToBeVisibleAsync();
        await Expect(Page.Locator(".bc-header__title")).ToHaveTextAsync("Untitled board");
        await Expect(Page.Locator(".bwc-signin")).ToBeVisibleAsync();
    }

    [Test]
    public async Task The_editor_uses_the_site_palette_in_both_themes()
    {
        await StartCleanAsync();

        await AssertEditorGroundMatchesPaletteAsync();

        await Page.ClickAsync(".bwc-theme-toggle");
        await Expect(Page.Locator("html")).ToHaveAttributeAsync("data-bs-theme", "light");

        await AssertEditorGroundMatchesPaletteAsync();
    }

    private async Task AssertEditorGroundMatchesPaletteAsync()
    {
        // The palette's hex and the editor's computed rgb() are compared through a probe element, so
        // both go through the browser's own colour parsing.
        var same = await Page.EvaluateAsync<bool>(
            """
            () => {
              const probe = document.createElement('div');
              probe.style.backgroundColor = getComputedStyle(document.documentElement).getPropertyValue('--bs-body-bg');
              document.body.appendChild(probe);
              const palette = getComputedStyle(probe).backgroundColor;
              probe.remove();
              return palette === getComputedStyle(document.querySelector('.bc-editor')).backgroundColor;
            }
            """);

        Assert.That(same, Is.True, "The editor's ground is not the site's --bs-body-bg.");
    }

    [Test]
    public async Task The_page_is_set_in_public_sans()
    {
        await StartCleanAsync();

        var family = await Page.EvaluateAsync<string>("() => getComputedStyle(document.body).fontFamily");
        var loaded = await Page.EvaluateAsync<bool>("async () => { await document.fonts.ready; return document.fonts.check('16px \"Public Sans\"'); }");

        Assert.That(family, Does.Contain("Public Sans"));
        Assert.That(loaded, Is.True, "Public Sans did not load from fonts/ under the host's base.");
    }

    [Test]
    public async Task The_theme_choice_survives_a_reload_before_blazor_boots()
    {
        await StartCleanAsync();
        await Page.ClickAsync(".bwc-theme-toggle");

        await Page.AddInitScriptAsync("document.addEventListener('DOMContentLoaded', () => { window.__themeAtDomReady = document.documentElement.getAttribute('data-bs-theme'); });");
        await Page.ReloadAsync();
        await Expect(Page.Locator(".bc-editor")).ToBeVisibleAsync(new() { Timeout = 60_000 });

        Assert.That(await Page.EvaluateAsync<string>("() => window.__themeAtDomReady"), Is.EqualTo("light"));
    }

    [Test]
    public async Task A_bad_handoff_fragment_is_erased_and_the_editor_still_opens()
    {
        await Page.GotoAsync($"{CanvasUrl}/#handoff=nope&doc={Guid.NewGuid()}");

        await Expect(Page.Locator(".bc-editor")).ToBeVisibleAsync(new() { Timeout = 60_000 });
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("^[^#]*$"), new() { Timeout = 15_000 });
    }

    [Test]
    public async Task The_board_does_not_scroll_the_page()
    {
        await StartCleanAsync();

        var scrolls = await Page.EvaluateAsync<bool>(
            "() => document.scrollingElement.scrollHeight > innerHeight + 1 || document.scrollingElement.scrollWidth > innerWidth + 1");

        Assert.That(scrolls, Is.False);
    }

    [Test]
    public async Task Sign_in_posts_under_the_api_base_path()
    {
        var requested = new List<string>();
        const string api = "http://localhost:5252/webapi";

        await Page.RouteAsync("**/appsettings*.json", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(new { Canvas = new { WebApiBaseUrl = api, SiteBaseUrl = "", MapTokenUrl = "" } }),
        }));

        await Page.RouteAsync("http://localhost:5252/**", async route =>
        {
            var request = route.Request;
            requested.Add(request.Url);
            var cors = new Dictionary<string, string>
            {
                ["Access-Control-Allow-Origin"] = CanvasUrl,
                ["Access-Control-Allow-Headers"] = "authorization, content-type",
                ["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS",
            };

            if (request.Method == "OPTIONS")
                await route.FulfillAsync(new() { Status = 204, Headers = cors });
            else if (request.Url == $"{api}/login")
                await route.FulfillAsync(new() { Status = 200, Headers = cors, ContentType = "application/json", Body = """{"tokenType":"Bearer","accessToken":"t","expiresIn":3600,"refreshToken":"r"}""" });
            else if (request.Url == $"{api}/api/me")
                await route.FulfillAsync(new() { Status = 200, Headers = cors, ContentType = "application/json", Body = """{"userId":"00000000-0000-0000-0000-000000000001","email":"sarah.mitchell@benco.dev","isSuperAdmin":false,"isAdmin":false}""" });
            else
                await route.FulfillAsync(new() { Status = 404, Headers = cors });
        });

        await GoAsync("/login");
        await Page.FillAsync("#email", "sarah.mitchell@benco.dev");
        await Page.FillAsync("#password", "not-a-real-password");
        await Page.ClickAsync("button[type='submit']");

        await Expect(Page.Locator(".bwc-signin__action")).ToHaveTextAsync("Sign out", new() { Timeout = 30_000 });

        Assert.That(requested, Does.Contain($"{api}/login"));
        Assert.That(requested, Has.None.EqualTo("http://localhost:5252/login"));
    }

    [Test]
    public async Task An_empty_api_url_still_opens_a_local_editor()
    {
        var errors = new List<string>();
        Page.Console += (_, m) => { if (m.Type == "error") errors.Add(m.Text); };

        await Page.RouteAsync("**/appsettings*.json", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = """{"Canvas":{"WebApiBaseUrl":"","SiteBaseUrl":"","MapTokenUrl":""}}""",
        }));

        await GoAsync();

        await Expect(Page.Locator(".bc-editor")).ToBeVisibleAsync();
        Assert.That(errors, Is.Empty, string.Join("\n", errors));
    }
}
