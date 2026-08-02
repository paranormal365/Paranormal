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
    /// <summary>Root URL of the running Blazor WebApp. Override with BEN_BASE_URL env var.</summary>
    protected static string BaseUrl => Environment.GetEnvironmentVariable("BEN_BASE_URL") ?? "http://localhost:5078";

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

        // Fill email and password fields — using placeholder selectors consistent
        // with the Login.razor form structure.
        await Page.FillAsync("input[type='email'], input[placeholder*='email' i], input[id*='email' i]", email);
        await Page.FillAsync("input[type='password']", password);
        await Page.ClickAsync("button[type='submit']");

        // Wait for the redirect away from /login (successful auth)
        await Page.WaitForURLAsync(url => !url.Contains("/login"), new() { Timeout = 10_000 });
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    /// <summary>Logs out by navigating to / and clicking the Sign Out button.</summary>
    protected async Task LogoutAsync()
    {
        await Page.GotoAsync(BaseUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var signOut = Page.GetByText("Sign Out");
        if (await signOut.IsVisibleAsync())
            await signOut.ClickAsync();
    }
}
