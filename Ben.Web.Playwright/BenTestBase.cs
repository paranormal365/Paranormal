using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

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
        await Page.FillAsync("input[type='email'], input[placeholder*='email' i], input[id*='email' i], input[placeholder='you@example.com']", email);
        await Page.FillAsync("input[type='password']", password);
        // Scoped to the login <form> — the nav bar/app bar also has unrelated
        // button[type='submit'] elements (icon toggle, and Sign Out when already
        // authenticated), and an unscoped selector matches the first of those instead.
        await Page.ClickAsync("form button[type='submit']");

        // Wait for the redirect away from /login (successful auth)
        await Page.WaitForURLAsync(url => !url.Contains("/login"), new() { Timeout = 10_000 });
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
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
        if (await signOut.IsVisibleAsync())
            await signOut.ClickAsync();
    }
}
