using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework;
using System.Text.RegularExpressions;

namespace Ben.Web.Playwright;

/// <summary>
/// Base class for all IsHaunted front-end Playwright tests.
/// </summary>
/// <remarks>
/// <para>
/// Inherits from <see cref="PageTest"/> which provides a fresh browser <see cref="Page"/>
/// per test and handles browser lifecycle automatically via NUnit's SetUp/TearDown.
/// </para>
/// <para>
/// Configuration is driven by environment variables (or a local <c>.env</c> / test settings):
/// <list type="table">
///   <listheader><term>Variable</term><description>Purpose</description></listheader>
///   <item><term>BEN_BASE_URL</term><description>Root URL of the running WebApp. Defaults to <c>http://localhost:5078</c>.</description></item>
///   <item><term>BEN_SUPERADMIN_EMAIL</term><description>Email for the SuperAdmin login flow tests.</description></item>
///   <item><term>BEN_SUPERADMIN_PASSWORD</term><description>Password for the SuperAdmin login flow tests.</description></item>
///   <item><term>BEN_USER_EMAIL</term><description>Email for a regular authenticated user (used in voting tests).</description></item>
///   <item><term>BEN_USER_PASSWORD</term><description>Password for the regular user.</description></item>
/// </list>
/// </para>
/// <para>
/// <strong>Prerequisites before running tests:</strong>
/// <list type="number">
///   <item><description>Install Playwright browsers: <c>pwsh Ben.Web.Playwright/bin/Debug/net10.0/playwright.ps1 install</c>
///   or on macOS: <c>~/.nuget/packages/microsoft.playwright/1.52.0/runtimes/unix/native/playwright.sh install chromium</c></description></item>
///   <item><description>Start the full stack: run VS Code task <c>start-full-stack</c> or <c>start-web-app</c>.</description></item>
///   <item><description>Ensure dev seed data is present: set <c>SeedData:DevData:Enabled = true</c> in <c>appsettings.Development.json</c>.</description></item>
/// </list>
/// </para>
/// </remarks>
public abstract class BenTestBase : PageTest
{
    /// <summary>
    /// Root URL of the front end under test. Override with the BEN_BASE_URL env var.
    /// <para>
    /// Defaults to :5078 — <c>Ben.Web.Website</c>, now that every page has been ported to it.
    /// The original <c>Ben.Web.WebApp</c> still runs on :5079; point <c>BEN_BASE_URL</c> there to
    /// run the same suite against it.
    /// </para>
    /// </summary>
    protected static string BaseUrl => Environment.GetEnvironmentVariable("BEN_BASE_URL") ?? "http://localhost:5078";

    /// <summary>Root URL of the WebApi. Override with the BEN_API_URL env var.</summary>
    protected static string ApiUrl => Environment.GetEnvironmentVariable("BEN_API_URL") ?? "http://localhost:5252";

    protected static string SuperAdminEmail    => Environment.GetEnvironmentVariable("BEN_SUPERADMIN_EMAIL")    ?? "haveben@msn.com";
    protected static string SuperAdminPassword => Environment.GetEnvironmentVariable("BEN_SUPERADMIN_PASSWORD") ?? "Y@ung615";
    protected static string UserEmail          => Environment.GetEnvironmentVariable("BEN_USER_EMAIL")          ?? "sarah.mitchell@benco.dev";
    protected static string UserPassword       => Environment.GetEnvironmentVariable("BEN_USER_PASSWORD")       ?? "S@rah!Mitchell26";

    /// <summary>
    /// Logs in as the specified user via the /login page and waits for redirect.
    /// </summary>
    /// <param name="email">Email address to enter into the login form.</param>
    /// <param name="password">Password to enter.</param>
    protected async Task LoginAsync(string email, string password)
    {
        await Page.GotoAsync($"{BaseUrl}/login");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Both sites are covered: the original's Telerik TextBox renders a plain <input> with no
        // type or id and the placeholder "you@example.com", while the new one gives it
        // id="login-email". The alternation matches either.
        await FillCredentialsAsync(email, password);

        // Submitting is retried for the same reason every other click here is: Blazor Server
        // attaches its handlers when the circuit connects, which happens *after* NetworkIdle, and
        // a click that lands in that window is silently dropped — the form just sits there. That
        // surfaced as "waiting for navigation" timeouts in tests that had nothing to do with
        // login, because the sign-in they depended on had quietly not happened.
        //
        // Scoped to the login <form>: the app bar also carries button[type='submit'] elements
        // (the icon toggle, and Sign Out when already authenticated), and an unscoped selector
        // matches one of those instead.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                await Page.ClickAsync("form button[type='submit']", new() { Timeout = 5_000 });
            }
            catch (TimeoutException)
            {
                continue;   // form still rendering
            }

            try
            {
                await Page.WaitForURLAsync(url => !url.Contains("/login"), new() { Timeout = 8_000 });
                await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                return;
            }
            catch (TimeoutException)
            {
                // The API rate-limits sign-in: a fixed one-minute window per client. A suite this
                // size, retrying, is exactly the traffic that limiter exists to refuse — so back
                // off and let the window roll rather than spending the retries inside it. This is
                // the suite adapting to a real protection, not the protection being wrong.
                if (await Page.GetByText("Too many sign-in attempts", new() { Exact = false })
                              .CountAsync() > 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(20));
                }

                // Dropped click, bad credentials, or a navigation still in flight from a sign-out
                // that had not finished — that last one moves the page out from under the form
                // mid-fill. Go back to a known state rather than assuming the form is still there,
                // then try again; the assert below reports honestly if it never takes.
                if (!Page.Url.Contains("/login"))
                {
                    await Page.GotoAsync($"{BaseUrl}/login");
                    await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                    if (!Page.Url.Contains("/login")) return;   // already signed in as this user
                }

                await FillCredentialsAsync(email, password);
            }
        }

        // Say what the page was showing, not just that it did not move. "Never left the login page"
        // is the same message whether the credentials were rejected, the submit never fired, or a
        // navigation pulled the form away — and those need different fixes.
        var shown = "";
        try
        {
            shown = (await Page.Locator(".alert, .text-danger, .validation-message")
                               .AllInnerTextsAsync() is { Count: > 0 } texts)
                    ? string.Join(" | ", texts) : "(no error shown on the page)";
        }
        catch { shown = "(could not read the page)"; }

        Assert.That(Page.Url, Does.Not.Contain("/login"),
            $"sign-in as {email} never left the login page. Page reported: {shown}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    private const string EmailSelector =
        "input[type='email'], input[placeholder*='email' i], input[id*='email' i], input[placeholder='you@example.com']";

    /// <summary>
    /// Types the credentials and confirms the fields actually hold them before returning.
    /// <para>
    /// Filling two Blazor-bound inputs back to back is not reliable: the first field's value
    /// change re-renders the component, and that render can land while the second field is being
    /// set, wiping it. The form then submits an empty password and the server answers "Invalid
    /// email or password" — with credentials that are perfectly good, which is what made this look
    /// like an account problem rather than a typing one.
    /// </para>
    /// </summary>
    private async Task FillCredentialsAsync(string email, string password)
    {
        var emailBox    = Page.Locator(EmailSelector).First;
        var passwordBox = Page.Locator("input[type='password']").First;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            await emailBox.FillAsync(email);
            await passwordBox.FillAsync(password);

            if (await emailBox.InputValueAsync() == email
             && await passwordBox.InputValueAsync() == password)
                return;
        }
    }

    /// <summary>
    /// Clicks <paramref name="target"/> until <paramref name="expected"/> appears, then returns.
    /// <para>
    /// Blazor Server attaches its event handlers when the circuit connects, which happens *after*
    /// NetworkIdle. A click that lands in that window is silently dropped: nothing throws, the
    /// page simply does not advance, and the next assertion fails somewhere unrelated — which is
    /// what made a visible, uniquely-named case report as "not visible".
    /// </para>
    /// <para>
    /// Retrying is more honest than sleeping: it costs nothing once the circuit is up, and it
    /// states the actual requirement — the click has to have had its effect.
    /// </para>
    /// </summary>
    protected async Task ClickUntilAsync(ILocator target, ILocator expected, int attempts = 4)
    {
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            try
            {
                await target.ClickAsync(new() { Timeout = 5_000 });
            }
            catch (TimeoutException)
            {
                continue;   // covered by an overlay mid-render; try again
            }

            try
            {
                await expected.First.WaitForAsync(
                    new() { State = WaitForSelectorState.Visible, Timeout = 4_000 });
                return;
            }
            catch (TimeoutException)
            {
                // Click was dropped, or the page is still catching up.
            }
        }

        // Out of attempts: assert so the failure names what was actually missing.
        await Expect(expected.First).ToBeVisibleAsync(new() { Timeout = 5_000 });
    }

    /// <summary>
    /// Clicks <paramref name="target"/> until the URL matches <paramref name="urlPattern"/>.
    /// <para>
    /// The counterpart to <see cref="ClickUntilAsync"/> for a control whose only visible effect is
    /// navigation — a Telerik GridCommandButton that calls NavigationManager, for instance. There
    /// is no element to wait for, so waiting on the address is the honest expectation.
    /// </para>
    /// </summary>
    protected async Task ClickUntilUrlAsync(ILocator target, string urlPattern, int attempts = 4)
    {
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            try
            {
                await target.ClickAsync(new() { Timeout = 5_000 });
            }
            catch (TimeoutException)
            {
                continue;
            }

            try
            {
                await Page.WaitForURLAsync(new Regex(urlPattern), new() { Timeout = 4_000 });
                await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                return;
            }
            catch (TimeoutException)
            {
                // Click dropped before the circuit was live; try again.
            }
        }

        Assert.That(Page.Url, Does.Match(urlPattern),
            $"clicking never navigated to something matching {urlPattern}");
    }

    /// <summary>
    /// Opens an organisation from /organizations by name.
    /// <para>
    /// The org name is a plain grid cell on the new site, not a link — you get in through the row's
    /// View control. Clicking the name text did nothing, which is why a whole cluster of case tests
    /// failed several steps later, reporting a case they never navigated to as "not visible".
    /// A link is still tried first, so this works against the original site too.
    /// </para>
    /// </summary>
    protected async Task<bool> OpenOrganizationAsync(string orgName)
    {
        await Page.GotoAsync($"{BaseUrl}/organizations");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var asLink = Page.GetByRole(AriaRole.Link, new() { Name = orgName, Exact = false });
        var row = Page.Locator("tr", new() { HasTextString = orgName }).First;

        ILocator opener;
        if (await asLink.CountAsync() > 0)
        {
            opener = asLink.First;
        }
        else if (await row.CountAsync() > 0)
        {
            opener = row.GetByRole(AriaRole.Button, new() { Name = "View" })
                        .Or(row.GetByRole(AriaRole.Link, new() { Name = "View" })).First;
        }
        else
        {
            return false;   // seed data differs; caller decides whether to skip
        }

        // The org hub is identified by its tab strip, which only the detail page renders.
        await ClickUntilAsync(opener, Main.GetByRole(AriaRole.Tab).Or(Main.Locator(".nav-tabs .nav-link")));
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        return true;
    }

    /// <summary>
    /// Switches to a tab on the organisation hub or a case, waiting for the click to take effect.
    /// Accepts the new site's <c>role="tab"</c> buttons and the original's plain text tabs.
    /// </summary>
    protected async Task OpenTabAsync(string tabName, ILocator expected)
    {
        var tab = Main.GetByRole(AriaRole.Tab, new() { Name = tabName, Exact = true })
                      .Or(Main.Locator(".nav-tabs .nav-link", new() { HasTextString = tabName }))
                      .Or(Main.GetByText(tabName, new() { Exact = true }))
                      .First;

        await Expect(tab).ToBeVisibleAsync(new() { Timeout = 8_000 });
        await ClickUntilAsync(tab, expected);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    /// <summary>
    /// Waits for the page's "Loading…" placeholders to go away.
    /// <para>
    /// Needed because WaitForLoadState(NetworkIdle) proves nothing here: a page's data arrives
    /// over the SignalR circuit, not as an HTTP request, so NetworkIdle is already true while the
    /// component is still showing its placeholder. Tests that read the body straight after
    /// navigating captured "Loading your case…" and reported the content as missing.
    /// </para>
    /// </summary>
    protected async Task WaitUntilLoadedAsync(int timeoutMs = 15_000)
    {
        var placeholder = Main.GetByText("Loading", new() { Exact = false });
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (await placeholder.CountAsync() == 0) return;
            await Task.Delay(150);
        }
        // Deliberately does not throw: some pages keep a permanent element containing the word,
        // and a caller's own assertion is a better failure message than a generic timeout here.
    }

    /// <summary>
    /// Clicks whichever of <paramref name="candidates"/> is actually on top at its own centre.
    /// Returns false when every one of them is covered.
    /// <para>
    /// Map pins overlap: at the default zoom two of them sit close enough that the first in DOM
    /// order is underneath the second, and clicking it is intercepted until the timeout. Forcing
    /// the click would paper over that — a pin a person cannot click is a real problem — so this
    /// picks one that is genuinely reachable and leaves the overlap visible for what it is.
    /// </para>
    /// </summary>
    protected async Task<bool> ClickTopmostAsync(ILocator candidates)
    {
        var count = await candidates.CountAsync();
        for (var i = 0; i < count; i++)
        {
            var candidate = candidates.Nth(i);
            if (!await candidate.IsVisibleAsync()) continue;

            var onTop = await candidate.EvaluateAsync<bool>(@"el => {
                const r = el.getBoundingClientRect();
                const hit = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2);
                return !!hit && (hit === el || el.contains(hit) || hit.contains(el));
            }");
            if (!onTop) continue;

            await candidate.ClickAsync(new() { Timeout = 5_000 });
            return true;
        }
        return false;
    }

    /// <summary>
    /// Opens a case from an organisation: /organizations → the org → its Cases tab → the case.
    /// Returns false when the seed data does not contain them, so a caller can skip rather than
    /// fail.
    /// <para>
    /// Several fixtures grew their own copy of this walk, written against the original site, and
    /// each copy carried the same two faults: it clicked the organisation's *name* (a plain grid
    /// cell here, not a link) and then an unscoped "Cases" link, which resolves to the sidebar's
    /// own entry and navigates away. Those tests ended up on My Cases and reported the case they
    /// never opened as "not visible".
    /// </para>
    /// </summary>
    protected async Task<bool> OpenOrgCaseAsync(string orgName, string caseText)
    {
        if (!await OpenOrganizationAsync(orgName)) return false;

        await OpenTabAsync("Cases", Main.GetByText(caseText, new() { Exact = false })
                                        .Or(Main.GetByText("No cases", new() { Exact = false })));

        // Each case is a card with its own "Open" button, and the card's text is not itself
        // clickable — the same shape as the organisation list. Clicking the case name did nothing
        // at all, so the walk sat on the organisation hub and reported the case as missing.
        var row = Main.Locator(".card").Filter(new() { HasTextString = caseText }).First;
        if (await row.CountAsync() == 0) return false;

        // Not `.Or(row).First`: Or() resolves in DOM order, and the card contains the button, so
        // the card always won and the click landed on dead space. Pick the button when there is
        // one, and only fall back to the card — which the original site made clickable — when
        // there is not.
        var openButton = row.GetByRole(AriaRole.Button, new() { Name = "Open" })
                            .Or(row.GetByRole(AriaRole.Link, new() { Name = "Open" }));
        var opener = await openButton.CountAsync() > 0 ? openButton.First : row;

        // Waiting on the URL, not on a tab strip: the organisation hub has tabs of its own, so
        // "a tab strip is visible" was already true before the click and the helper returned
        // having gone nowhere.
        await ClickUntilUrlAsync(opener, @"/organizations/[0-9a-f\-]+/cases/[0-9a-f\-]+");
        await WaitUntilLoadedAsync();
        return true;
    }

    /// <summary>
    /// The page's main content, excluding the chrome.
    /// <para>
    /// Tab and link lookups need this on the new site: its sidebar lists "My Cases",
    /// "Cases &amp; Investigations", "Equipment", "Messages" and more, and sits *before* the
    /// content in the DOM — so an unscoped <c>GetByText("Cases").First</c> resolves to a nav entry
    /// and either clicks the wrong thing or trips strict mode. Matches the original site's layout
    /// too, where it simply narrows to the same region.
    /// </para>
    /// </summary>
    protected ILocator Main => Page.Locator(".app-content, main, .content-wrapper").First;

    /// <summary>
    /// Opens the header's profile menu, which is where the new site keeps the signed-in email and
    /// the Sign Out button. The original showed both directly in the app bar, so this is a no-op
    /// there — it only clicks when the menu's contents are not already visible.
    /// </summary>
    protected async Task OpenProfileMenuAsync()
    {
        var signOut = Page.GetByText("Sign Out");
        if (await signOut.IsVisibleAsync()) return;

        var profile = Page.Locator("[aria-label='Open Profile Dropdown'], .user-menu > button").First;
        if (!await profile.IsVisibleAsync()) return;

        // The button is Blazor-interactive; clicking before the circuit is live does nothing, so
        // retry rather than assuming the first click lands.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            // Do not click a menu that is already open — that would close it again.
            if (await profile.GetAttributeAsync("aria-expanded") == "true")
            {
                try
                {
                    await signOut.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 3_000 });
                    return;
                }
                catch (TimeoutException) { }
            }

            await profile.ClickAsync(new() { Timeout = 5_000 });
            try
            {
                await signOut.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 3_000 });
                return;
            }
            catch (TimeoutException) { /* circuit not ready yet — try again */ }
        }
    }

    /// <summary>
    /// Logs out through the header. On the new site Sign Out lives inside the profile dropdown,
    /// so the menu has to be opened first; on the original it is directly in the bar. Opening the
    /// menu when it is already open would close it, so this only clicks when Sign Out is hidden.
    /// </summary>
    protected async Task LogoutAsync()
    {
        await Page.GotoAsync(BaseUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await OpenProfileMenuAsync();

        var signOut = Page.GetByText("Sign Out");
        if (!await signOut.IsVisibleAsync()) return;   // already signed out

        await signOut.ClickAsync();

        // Wait for the sign-out to have actually happened before returning. Signing out navigates
        // to /login on its own, and returning early let the next LoginAsync start filling the form
        // while that navigation was still in flight — it then wiped the fields, the submit went
        // nowhere, and sign-in "failed" three times running for no reason visible in the app.
        try
        {
            await Page.GetByText("Sign In").Or(Page.Locator("form button[type='submit']"))
                      .First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        }
        catch (TimeoutException)
        {
            // Fall through: LoginAsync retries and reports honestly if it never takes.
        }
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }
}
