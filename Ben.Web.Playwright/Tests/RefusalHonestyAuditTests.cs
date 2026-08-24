using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The second half of Ben's 2026-08-24 audit: not "does the page render" but "does it tell the
/// truth". A surface a role is REFUSED must say so; the failure this catches is the one this
/// codebase keeps re-learning — a refused fetch rendered as "nothing here", which tells somebody
/// their group is empty when the server actually said no.
/// </summary>
[TestFixture]
[Category("Audit")]
public class RefusalHonestyAuditTests : BenTestBase
{
    private const string TghId = "881ea0f6-8c0d-475e-9065-c6ed15e3302f";

    /// <summary>Words that mean "there is nothing", as opposed to "you may not see it".</summary>
    private static readonly string[] EmptyStateWords =
    [
        "No cases", "no cases", "Nothing here", "nothing yet", "No records", "None yet",
    ];

    /// <summary>Words that mean the reader was refused or the load failed — an honest answer.</summary>
    private static readonly string[] HonestWords =
    [
        "couldn't", "could not", "permission", "not allowed", "Sign in", "signed in",
        "cannot", "no access", "Try again", "failed",
    ];

    [Test]
    public async Task A_viewer_refused_the_case_list_is_told_so_not_shown_an_empty_one()
    {
        await LoginAsync(ViewerEmail, ViewerPassword);   // Victor — Viewer, no roles

        await Page.GotoAsync($"{BaseUrl}/organizations/{TghId}?tab=cases");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await WaitUntilLoadedAsync();

        var body = await Main.InnerTextAsync();

        // Either the tab is not offered at all (also honest), or what it shows must not claim
        // emptiness. The API answers this seat 403 — so "No cases" here would be a lie.
        var claimsEmpty = EmptyStateWords.Any(w => body.Contains(w, StringComparison.Ordinal));
        var explains = HonestWords.Any(w => body.Contains(w, StringComparison.OrdinalIgnoreCase));

        Assert.That(!claimsEmpty || explains, Is.True,
            "A Viewer is refused the case list by the API, but the page claims the group has no "
          + "cases instead of saying they cannot see them. Page text:\n" + Truncate(body));
    }

    [Test]
    public async Task A_member_without_settings_permission_is_not_shown_an_empty_billing_history()
    {
        await LoginAsync(MemberEmail, MemberPassword);   // James — Member; billing is 403 for him

        await Page.GotoAsync($"{BaseUrl}/pricing");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await WaitUntilLoadedAsync();

        var body = await Main.InnerTextAsync();

        // The pricing page skips the group card entirely when billing is refused — that IS the
        // honest choice (documented in PricingPage). What must never appear is a card claiming
        // the group is on the free plan when the truth is "you may not see this".
        Assert.That(body, Does.Not.Contain("On the free plan"),
            "A member who may not read billing was shown a plan claim rather than nothing:\n" + Truncate(body));
    }

    [Test]
    public async Task Every_persona_sees_a_help_document_they_are_allowed_to_read()
    {
        // The help ceiling is computed server-side; a reader must always land on something.
        foreach (var (persona, email, password) in new[]
                 {
                     ("client", ClientEmail, ClientPassword),
                     ("member", MemberEmail, MemberPassword),
                     ("admin",  UserEmail,   UserPassword),
                 })
        {
            await LoginAsync(email, password);
            await Page.GotoAsync($"{BaseUrl}/help");
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await WaitUntilLoadedAsync();

            var body = await Main.InnerTextAsync();
            Assert.That(body.Length, Is.GreaterThan(200), $"[{persona}] the help index rendered almost nothing");
            Assert.That(body, Does.Not.Contain("An unhandled error"), $"[{persona}] help index errored");
        }
    }

    private static string Truncate(string s) => s.Length <= 1200 ? s : s[..1200] + "…";
}
