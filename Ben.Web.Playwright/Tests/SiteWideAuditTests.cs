using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Ben's request (2026-08-24): walk the site as every kind of user and record what is broken or
/// missing. This is an AUDIT, not a gate — each persona visits the surfaces that persona actually
/// uses, and every page that fails to render, refuses without saying so, or shows an empty state
/// where content should be is collected and reported in one list rather than failing on the first.
/// </summary>
/// <remarks>
/// It stays in the suite because "every screen this role sees still renders" is the cheapest
/// regression net there is, and because a persona-shaped walk catches what per-feature tests
/// cannot: a page that works for its author and refuses everybody else.
/// </remarks>
[TestFixture]
[Category("Audit")]
public class SiteWideAuditTests : BenTestBase
{
    // Resolved from the slug at run time — a hardcoded GUID dies with every database rebuild.
    private string TghId = null!;

    [SetUp]
    public async Task ResolveTghId() => TghId = await OrgIdBySlugAsync("paranormal365");

    private static string OwnerEmail => Environment.GetEnvironmentVariable("BEN_OWNER_EMAIL") ?? "emma.rodriguez@benco.dev";
    private static string? OwnerPassword => Environment.GetEnvironmentVariable("BEN_OWNER_PASSWORD");

    /// <summary>What a page must not be.</summary>
    private async Task<string?> ProblemWithAsync(string path, string? expectedText = null)
    {
        await Page.GotoAsync($"{BaseUrl}{path}", new() { Timeout = 30_000 });
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await WaitUntilLoadedAsync();

        var body = await Main.InnerTextAsync();

        if (body.Contains("An unhandled error has occurred")) return $"{path} — unhandled error";
        if (body.Contains("Page not found")) return $"{path} — not routed / gated off";
        if (body.Contains("Sorry, there's nothing at this address")) return $"{path} — not routed";
        if (body.Trim().Length < 40) return $"{path} — rendered only {body.Trim().Length} chars";
        if (expectedText is not null && !body.Contains(expectedText, StringComparison.OrdinalIgnoreCase))
            return $"{path} — rendered without \"{expectedText}\"";
        return null;
    }

    private async Task WalkAsync(string persona, IEnumerable<(string Path, string? Expect)> pages, List<string> findings)
    {
        foreach (var (path, expect) in pages)
        {
            var problem = await ProblemWithAsync(path, expect);
            if (problem is not null) findings.Add($"[{persona}] {problem}");
        }
    }

    // ── 1. The stranger ───────────────────────────────────────────────────────

    [Test]
    public async Task Anonymous_visitor_sees_every_public_surface()
    {
        var findings = new List<string>();
        await WalkAsync("anonymous",
        [
            ("/", "Haunted"),
            ("/pricing", "Pricing"),
            ("/find", null),
            ("/events", null),
            ("/equipment-catalog", null),
            ("/publications", null),
            ("/help", "Help"),
            ("/o/paranormal365", null),
            ("/login", null),
            ("/signup", null),
            ("/contact", null),
            ("/forgot-password", null),
        ], findings);

        Assert.That(findings, Is.Empty, "Anonymous surfaces:\n  " + string.Join("\n  ", findings));
    }

    // ── 2. The client ─────────────────────────────────────────────────────────

    [Test]
    public async Task Client_sees_their_own_case_and_nothing_of_the_groups()
    {
        await LoginAsync(ClientEmail, ClientPassword);
        var findings = new List<string>();

        await WalkAsync("client",
        [
            ("/my-cases", null),
            ("/my-investigations", null),
            ("/my-requests", null),
            ("/my-requests/new", null),
            ("/notifications", null),
            ("/profile", null),
            ("/pricing", "Pricing"),
            ("/help", "Help"),
        ], findings);

        // A client is in no group: the group hub must refuse rather than render an empty shell.
        await Page.GotoAsync($"{BaseUrl}/organizations/{TghId}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await WaitUntilLoadedAsync();
        var body = await Main.InnerTextAsync();
        if (body.Contains("An unhandled error has occurred"))
            findings.Add("[client] a group hub they do not belong to threw instead of refusing");

        Assert.That(findings, Is.Empty, "Client surfaces:\n  " + string.Join("\n  ", findings));
    }

    // ── 3. The ordinary member ────────────────────────────────────────────────

    [Test]
    public async Task Member_sees_their_group_and_their_own_billing()
    {
        await LoginAsync(MemberEmail, MemberPassword);   // James — Member, Investigator role
        var findings = new List<string>();

        await WalkAsync("member",
        [
            ("/", null),
            ($"/organizations/{TghId}", null),
            ($"/organizations/{TghId}?tab=cases", null),
            ($"/organizations/{TghId}?tab=calendar", null),
            ("/my-equipment", null),
            ("/my-checkouts", null),
            ("/media-library", null),
            ("/my-videos", null),
            ($"/organizations/{TghId}/messages", null),
            ("/notifications", null),
            ("/equipment-catalog", null),
            ("/pricing", "Pricing"),
            ("/help", "Help"),
        ], findings);

        Assert.That(findings, Is.Empty, "Member surfaces:\n  " + string.Join("\n  ", findings));
    }

    // ── 4. The viewer ─────────────────────────────────────────────────────────

    [Test]
    public async Task Viewer_can_read_and_is_told_when_they_cannot_act()
    {
        await LoginAsync(ViewerEmail, ViewerPassword);   // Victor — Viewer, no roles
        var findings = new List<string>();

        await WalkAsync("viewer",
        [
            ($"/organizations/{TghId}", null),
            ($"/organizations/{TghId}?tab=cases", null),
            ($"/organizations/{TghId}/messages", null),
            ("/notifications", null),
            ("/pricing", "Pricing"),
        ], findings);

        Assert.That(findings, Is.Empty, "Viewer surfaces:\n  " + string.Join("\n  ", findings));
    }

    // ── 5. The group administrator ────────────────────────────────────────────

    [Test]
    public async Task Administrator_reaches_every_management_surface()
    {
        await LoginAsync(UserEmail, UserPassword);   // Sarah — Administrator of TGH
        var findings = new List<string>();

        await WalkAsync("admin",
        [
            ($"/organizations/{TghId}", null),
            ($"/organizations/{TghId}?tab=members", null),
            ($"/organizations/{TghId}?tab=requests", null),
            ($"/organizations/{TghId}?tab=cms", null),
            ($"/organizations/{TghId}?tab=files", null),
            ($"/organizations/{TghId}/edit", null),
            ($"/organizations/{TghId}/members", null),
            ($"/organizations/{TghId}/pending-requests", null),
            ($"/organizations/{TghId}/membership-questions", null),
            ($"/organizations/{TghId}/client-settings", null),
            ($"/organizations/{TghId}/files", null),
            ($"/organizations/{TghId}/calendar", null),
            ($"/organizations/{TghId}/cms", null),
            ($"/organizations/{TghId}/messages", null),
            ($"/organizations/{TghId}/promote", null),
            ($"/organizations/{TghId}/cases/new", null),
            ("/organization-security", null),
            ("/pricing", "Pricing"),
        ], findings);

        Assert.That(findings, Is.Empty, "Administrator surfaces:\n  " + string.Join("\n  ", findings));
    }

    // ── 6. The owner ──────────────────────────────────────────────────────────

    [Test]
    public async Task Owner_reaches_the_owner_only_surfaces()
    {
        if (string.IsNullOrEmpty(OwnerPassword))
            Assert.Ignore("BEN_OWNER_PASSWORD not set — the owner seat's password lives only in gitignored config.");

        await LoginAsync(OwnerEmail, OwnerPassword!);
        var findings = new List<string>();

        await WalkAsync("owner",
        [
            ($"/organizations/{TghId}", null),
            ($"/organizations/{TghId}/edit", null),
            ($"/organizations/{TghId}/members", null),
            ("/organization-security", null),
            ("/pricing", "Pricing"),
        ], findings);

        Assert.That(findings, Is.Empty, "Owner surfaces:\n  " + string.Join("\n  ", findings));
    }

    // ── 7. The platform administrator ─────────────────────────────────────────

    [Test]
    public async Task SuperAdmin_reaches_every_administration_surface()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        var findings = new List<string>();

        await WalkAsync("superadmin",
        [
            ("/admin/dashboard", null),
            ("/organizations", null),
            ("/admin/users", null),
            ("/admin/roles", null),
            ("/admin/file-types", null),
            ("/admin/lookup-types", null),
            ("/admin/experience-taxonomy", null),
            ("/admin/feed-reports", null),
            ("/admin/sidecar-telemetry", null),
            ("/admin/rate-limits", null),
            ("/upload-files", null),
            ("/admin/cases", null),
            ("/admin/investigations", null),
            ("/admin/site-settings", null),
            ("/admin/support-tickets", null),
            ("/admin/equipment-taxonomy", null),
            ("/admin/video-assets", null),
            ("/admin/org-ads", null),
            ("/admin/merge-groups", "Merge Groups"),
            ("/admin/subscription-tiers", null),
            ("/admin/coupons", null),
            ("/admin/org-subscriptions", null),
            ("/admin/billing-ledger", "Ledger"),
            ("/admin/tax-rates", "Tax Rates"),
            ("/admin/referrals", "Referrals"),
            ("/admin/member-seats", "Member Seats"),
            ("/admin/audit-log", null),
        ], findings);

        Assert.That(findings, Is.Empty, "SuperAdmin surfaces:\n  " + string.Join("\n  ", findings));
    }
}
