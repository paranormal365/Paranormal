using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework;
using System.Text.Json;

namespace Ben.Web.Playwright;

/// <summary>
/// The header's bell and profile menus are positioned by app.css rather than by Bootstrap, because
/// neither of Bootstrap's positioning hooks ([data-bs-popper], set by Popper) nor the template's
/// ([data-bs-toggle=dropdown]) is present — both are JS-owned, and these menus are Blazor-owned.
/// <para>
/// The failure that produced these tests: with no offsets at all the menu laid out from the
/// button's left edge and ran past the right of the viewport, widening the document.
/// </para>
/// </summary>
[TestFixture]
public class HeaderDropdownTests : BenTestBase
{
    [SetUp]
    public async Task SignIn() => await LoginAsync(SuperAdminEmail, SuperAdminPassword);

    /// <summary>
    /// Opening a header menu must not make the document wider than its viewport. This is the
    /// user-visible symptom — a horizontal scrollbar and the page "pulling to the right".
    /// </summary>
    [TestCase("Open Notifications")]
    [TestCase("Open Profile Dropdown")]
    public async Task OpeningHeaderMenu_DoesNotWidenThePage(string toggleLabel)
    {
        var toggle = Page.Locator($"[aria-label='{toggleLabel}']").First;
        await Expect(toggle).ToBeVisibleAsync(new() { Timeout = 10_000 });

        var before = await Page.EvaluateAsync<int>("document.documentElement.scrollWidth");

        // Retry: a click before the circuit is live is silently dropped.
        var menu = Page.Locator(".app-header .dropdown-menu.show").First;
        await ClickUntilAsync(toggle, menu);

        var metrics = await Page.EvaluateAsync<JsonElement>(@"() => {
            const menu   = document.querySelector('.app-header .dropdown-menu.show');
            const toggle = menu.parentElement.querySelector('button');
            const m = menu.getBoundingClientRect(), t = toggle.getBoundingClientRect();
            return {
                menuLeft: m.left, menuRight: m.right, menuWidth: m.width, menuTop: m.top,
                toggleRight: t.right, toggleBottom: t.bottom,
                viewport: document.documentElement.clientWidth,
                scrollWidth: document.documentElement.scrollWidth
            };
        }");

        float N(string key) => metrics.GetProperty(key).GetSingle();

        Assert.Multiple(() =>
        {
            Assert.That(N("menuWidth"), Is.GreaterThan(100),
                "menu did not actually render at a usable size");

            // The whole point: it opens leftward, into the page.
            Assert.That(N("menuRight"), Is.LessThanOrEqualTo(N("viewport") + 1),
                "menu extends past the right edge of the viewport");
            Assert.That(N("menuLeft"), Is.LessThan(N("toggleRight")),
                "menu should extend to the LEFT of its toggle, not to the right");

            // Right edges anchored together is what makes that true at any width.
            Assert.That(N("menuRight"), Is.EqualTo(N("toggleRight")).Within(2),
                "menu's right edge should be anchored to the toggle's right edge");

            // Hangs below the button rather than at header height.
            Assert.That(N("menuTop"), Is.GreaterThanOrEqualTo(N("toggleBottom") - 1),
                "menu should hang below its toggle");

            Assert.That(N("scrollWidth"), Is.LessThanOrEqualTo(N("viewport") + 1),
                $"document widened on open (was {before})");
        });
    }

    /// <summary>
    /// A nav badge names what it counts. "Organizations 17" was read as seventeen organizations
    /// when it meant seventeen unread messages and the page lists three — the number needs units.
    /// </summary>
    [Test]
    public async Task NavBadge_SaysWhatItIsCounting()
    {
        await Page.GotoAsync($"{BaseUrl}/");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var badges = Page.Locator(".app-sidebar .nav-menu .badge");
        var count = await badges.CountAsync();
        Assert.That(count, Is.GreaterThan(0), "expected at least one badged nav item for this user");

        for (var i = 0; i < count; i++)
        {
            var label = await badges.Nth(i).GetAttributeAsync("aria-label") ?? "";
            var title = await badges.Nth(i).GetAttributeAsync("title") ?? "";
            Assert.That(label, Is.Not.Empty, "badge has no accessible name");
            // A bare count, or the old generic "waiting", does not say what it counts.
            Assert.That(label, Does.Not.Match(@"^\d+$").And.Not.EqualTo(title.Trim()).Or.Not.Match(@"^\d+ waiting$"),
                $"badge label '{label}' does not name its units");
            Assert.That(label, Does.Match(@"^\d+ \D+"), $"badge label '{label}' should read like '17 unread group messages'");
        }
    }

    /// <summary>
    /// The drop-in: the template animates on a cubic-bezier that overshoots 1, which is the bounce.
    /// Asserting the declarations rather than sampled frames — a CSS transition does not advance in
    /// a backgrounded tab, so sampling mid-flight is not reliable evidence.
    /// </summary>
    [Test]
    public async Task HeaderMenu_AnimatesInWithTheTemplatesBounce()
    {
        var toggle = Page.Locator("[aria-label='Open Notifications']").First;
        await Expect(toggle).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await ClickUntilAsync(toggle, Page.Locator(".app-header .dropdown-menu.show").First);

        var style = await Page.EvaluateAsync<JsonElement>(@"() => {
            const menu = document.querySelector('.app-header .dropdown-menu.show');
            const cs = getComputedStyle(menu);
            return {
                timing: cs.transitionTimingFunction,
                property: cs.transitionProperty,
                duration: cs.transitionDuration,
                origin: cs.transformOrigin
            };
        }");

        string S(string key) => style.GetProperty(key).GetString() ?? "";

        Assert.Multiple(() =>
        {
            Assert.That(S("property"), Does.Contain("transform"));
            // The overshoot: y2 = 1.25 > 1 is what makes it bounce rather than ease.
            Assert.That(S("timing"), Does.Contain("1.25"),
                "expected the template's overshooting curve");
            Assert.That(S("duration"), Does.Contain("0.27s"));
        });
    }
}
