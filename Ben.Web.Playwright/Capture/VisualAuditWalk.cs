using System.Text;
using System.Text.Json;
using NUnit.Framework;

namespace Ben.Web.Playwright.Capture;

/// <summary>
/// Walks the site looking for the kinds of fault only looking can find.
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> Ben, 2026-09-18: "the events on the main page need some padding in
/// the card. They do not look right with no padding." The card's body had no padding rule at all —
/// the markup named a class nothing styled, so the text sat on the card's own border. Every test we
/// had passed: the markup was right, the component rendered, nothing threw. Then: "Can you crawl
/// through the web app and make sure there are no other visual anomalies like the card on the main
/// page."</para>
///
/// <para>Looking at every page by hand does not scale and does not repeat. So the shapes that fault
/// can take are written down in <c>visual-audit.js</c> and run against every page here. The first run
/// found two more: the privacy note under the home page's map was clipped away in its entirety, and
/// <c>.text-secondary</c> — how nearly every explanatory line on the site is drawn — was rendering
/// Bootstrap's light-mode grey on the dark theme at 3.29:1.</para>
///
/// <para><b>It reports rather than fails.</b> Contrast in particular is a judgement: a badge at
/// 3.35:1 may be a deliberate choice, and a test that fails on it would be turned off within a week.
/// The walk writes what it saw and leaves the reading to a person — except for the two shapes that
/// are never a choice, which do fail: text sitting on a card's border, and content a box is hiding
/// from the person who asked for it.</para>
///
/// <para>Opt-in with <c>BEN_VISUAL_AUDIT=1</c>, like the other walks, because it needs the site
/// running and takes a minute.</para>
/// </remarks>
[TestFixture]
[Category("Capture")]
[NonParallelizable]
public sealed class VisualAuditWalk : BenTestBase
{
    private sealed record Finding(string Kind, string El, string Detail);

    /// <summary>What a seat can reach. Kept short deliberately: breadth of SHAPES beats depth of pages.</summary>
    private static readonly (string Seat, string[] Routes)[] Walks =
    [
        // /tonight and /places/new added 2026-09-21 with items 248 and 250; a place's own page and
        // the guest-code sheet need an id and are photographed by VisualShots instead.
        ("visitor", ["/", "/pricing", "/events", "/publications", "/find", "/equipment-catalog", "/login", "/signup", "/help", "/changes", "/contact", "/privacy", "/terms", "/tonight", "/places/new"]),
        ("member",  ["/", "/feed", "/notifications", "/profile", "/my-cases", "/my-requests", "/my-events", "/my-equipment", "/my-checkouts", "/my-evidence", "/my-field-sessions", "/organizations", "/media-library", "/tonight", "/places/new"]),
        ("superadmin", ["/admin/dashboard", "/admin/users", "/admin/cases", "/admin/events", "/admin/site-settings", "/admin/subscription-tiers", "/admin/coupons", "/admin/org-subscriptions", "/admin/billing-ledger", "/admin/audit-log", "/admin/error-log", "/admin/file-types", "/admin/roles", "/admin/referrals"]),
    ];

    private string _script = "";

    [OneTimeSetUp]
    public void ReadScript()
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Capture", "visual-audit.js");
        Assert.That(File.Exists(path), Is.True, $"the auditor is missing: {path}");
        _script = File.ReadAllText(path);
    }

    [Test]
    public async Task Walk_the_site_and_report_what_looks_wrong()
    {
        if (Environment.GetEnvironmentVariable("BEN_VISUAL_AUDIT") != "1")
            Assert.Ignore("Set BEN_VISUAL_AUDIT=1 to walk the site for visual faults.");

        var report = new StringBuilder("# What the site looks like\n\n");
        var hard = new List<string>();
        var seen = 0;

        foreach (var (seat, routes) in Walks)
        {
            if (seat != "visitor")
            {
                var (email, password) = seat == "superadmin"
                    ? (SuperAdminEmail, SuperAdminPassword)
                    : (UserEmail, UserPassword);

                if (string.IsNullOrWhiteSpace(password))
                {
                    report.Append($"\n## {seat}\n\nSkipped: no password in the environment.\n");
                    continue;
                }

                await LoginAsync(email, password);
            }

            report.Append($"\n## {seat}\n\n");

            foreach (var route in routes)
            {
                await Page.GotoAsync($"{BaseUrl}{route}", new() { Timeout = 30_000 });
                await WaitUntilLoadedAsync();
                await Page.WaitForTimeoutAsync(1_500);

                List<Finding> found;
                try
                {
                    var json = await Page.EvaluateAsync<string>(
                        _script + "\n JSON.stringify(window.__benVisualAudit())");
                    found = JsonSerializer.Deserialize<List<Finding>>(json,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
                }
                catch (Exception ex)
                {
                    report.Append($"- `{route}` — could not be read: {ex.Message[..Math.Min(80, ex.Message.Length)]}\n");
                    continue;
                }

                seen++;
                if (found.Count == 0) { report.Append($"- `{route}` — nothing\n"); continue; }

                report.Append($"- `{route}`\n");
                foreach (var group in found.GroupBy(f => f.Kind).OrderBy(g => g.Key))
                {
                    report.Append($"    - **{group.Key}** ({group.Count()})\n");
                    foreach (var f in group.Take(4)) report.Append($"        - `{f.El}` — {f.Detail}\n");
                }

                // The two that are never a design choice.
                foreach (var f in found.Where(f => f.Kind is "text on the card edge" or "content clipped"))
                    hard.Add($"{seat} {route}: {f.Kind} — {f.El} — {f.Detail}");
            }
        }

        var folder = Environment.GetEnvironmentVariable("BEN_VISUAL_AUDIT_OUT")
                     ?? TestContext.CurrentContext.WorkDirectory;
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "visual-audit.md");
        File.WriteAllText(file, report.ToString());
        TestContext.Out.WriteLine($"{seen} pages read; report at {file}");
        TestContext.Out.WriteLine(report.ToString());

        Assert.That(hard, Is.Empty,
            "these are not design choices — a card's text on its own border, or a box hiding its own "
          + "content:\n  " + string.Join("\n  ", hard));
    }
}
