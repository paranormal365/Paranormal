using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The SuperAdmin-only pages, none of which had any coverage.
/// </summary>
/// <remarks>
/// <para>
/// These matter out of proportion to how often they are opened: only a SuperAdmin can reach them,
/// so nobody else will ever hit the breakage first and report it. A page that throws here stays
/// broken until the one person who uses it happens to look.
/// </para>
/// <para>
/// Deliberately shallow. Each asserts the page comes up with its own content and that a regular
/// user is kept out — the two things most likely to regress and the two that no other test covers.
/// Driving each admin screen in depth would be a much larger job with far less value per test.
/// </para>
/// </remarks>
[TestFixture]
[Category("Admin")]
public class AdminPageTests : BenTestBase
{
    /// <summary>Route, and a word that only that page renders.</summary>
    private static readonly (string Path, string Marker)[] Pages =
    {
        ("/admin/cases",              "Cases"),
        ("/admin/investigations",     "Investigations"),
        ("/admin/site-settings",      "Settings"),
        ("/admin/support-tickets",    "Support"),
        ("/admin/equipment-taxonomy", "Equipment"),
        ("/admin/video-assets",       "Clipart"),   // the route and the heading differ
        ("/admin/sidecar-telemetry",  "Sidecar"),
        ("/admin/rate-limits",        "Rate Limits"),
        // The Billing trio (items 85/84). Price Bands earns its place the hard way: its first
        // production load killed the circuit, because the healthy price list answers the
        // validation call with an empty 204 the HTTP client then threw on. Only a real browser
        // ever runs OnInitializedAsync — every curl-level check passed.
        ("/admin/subscription-tiers",  "Price Bands"),
        ("/admin/coupons",             "Coupons"),
        ("/admin/org-subscriptions",   "Subscriptions"),
        // The money trail + merge (items 168/110, 2026-08-23).
        ("/admin/billing-ledger",      "Ledger"),
        ("/admin/tax-rates",           "Tax Rates"),
        ("/admin/referrals",           "Referrals"),
        ("/admin/merge-groups",        "Merge Groups"),
        ("/admin/place-duplicates",    "Duplicate places"),
        ("/admin/member-seats",        "Member Seats"),
        ("/admin/org-ads",             "Group ads"),
        // Deleting a person (2026-09-04). It belongs in this list for exactly the reason the list
        // exists: only a SuperAdmin can open it, so nobody else would ever find it broken.
        ("/admin/delete-user",         "Delete a person"),
    };

    [Test]
    public async Task EveryAdminPage_RendersForASuperAdmin()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);

        var broken = new List<string>();

        foreach (var (path, marker) in Pages)
        {
            await Page.GotoAsync($"{BaseUrl}{path}", new() { Timeout = 25_000 });
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await WaitUntilLoadedAsync();

            var body = await Main.InnerTextAsync();

            if (body.Contains("An unhandled error has occurred")) broken.Add($"{path} — unhandled error");
            else if (body.Contains("Page not found")) broken.Add($"{path} — not routed");
            else if (body.Trim().Length < 40) broken.Add($"{path} — rendered {body.Trim().Length} chars");
            else if (!body.Contains(marker, StringComparison.OrdinalIgnoreCase))
                broken.Add($"{path} — rendered, but without its own content (no \"{marker}\")");
        }

        Assert.That(broken, Is.Empty,
            $"{broken.Count} of {Pages.Length} admin pages did not come up:\n  " + string.Join("\n  ", broken));
    }

    [Test]
    public async Task NoAdminPage_LeaksToARegularUser()
    {
        await LoginAsync(UserEmail, UserPassword);

        var leaked = new List<string>();

        foreach (var (path, _) in Pages)
        {
            await Page.GotoAsync($"{BaseUrl}{path}", new() { Timeout = 25_000 });
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await WaitUntilLoadedAsync();

            var body = await Main.InnerTextAsync();

            // A refusal can be rendered several ways — a redirect, an empty state, a message — and
            // pinning one of them would make this brittle. What must never happen is the admin
            // grid itself appearing, so that is what is asserted.
            var showsAdminData = await Page.Locator(".k-grid").CountAsync() > 0
                                 && !body.Contains("not authorized", StringComparison.OrdinalIgnoreCase)
                                 && !body.Contains("signed in", StringComparison.OrdinalIgnoreCase);

            if (showsAdminData) leaked.Add(path);
        }

        Assert.That(leaked, Is.Empty,
            "a regular user was shown administrative data on:\n  " + string.Join("\n  ", leaked));
    }
}
