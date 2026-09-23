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

    /// <summary>
    /// The widths each seat's pages are read at: the desktop the walk began with, and a phone.
    /// </summary>
    /// <remarks>
    /// W5 (crawl 2026-09-21): nothing checked layout at phone width. The walk read contrast,
    /// clipping and card edges at desktop only, and the sweep's 375 pass watches the console and
    /// the network, not the page — so a screen that scrolled sideways on every phone passed both.
    /// Item 210 and the request wizard had fits-at-375 tests of their own; nothing else did. The
    /// same auditor runs at both widths, and at 375 its "page scrolls sideways" finding, which a
    /// desktop almost never trips, names the outermost elements that stick out.
    /// </remarks>
    private static readonly (string Name, int Width, int Height)[] Passes =
    [
        ("desktop", 0, 0),        // whatever the browser was given; not changed
        ("phone", 375, 812),
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

        // Before anything is measured: a host started before the last build reports faults that
        // are real on the screen and false about the product. See RefuseAStaleHostAsync.
        await RefuseAStaleHostAsync();

        var report = new StringBuilder($"# What the site looks like — {ThemeName} theme\n\n");
        var body = new StringBuilder("## Page by page\n");
        var all = new List<VisualRollUp.Row>();
        var hard = new List<string>();
        var seen = 0;

        var desktop = Page.ViewportSize;
        foreach (var (pass, width, height) in Passes)
        {
            if (width > 0) await Page.SetViewportSizeAsync(width, height);
            body.Append($"\n## At {(width > 0 ? $"{width}px ({pass})" : pass)} width\n");

            foreach (var (seat, routes) in Walks)
            {
                // Rows are keyed by pass as well as seat, so a phone-only fault reads as one.
                var who = width > 0 ? $"{seat}@{width}" : seat;

                if (seat == "visitor")
                {
                    // The desktop pass ends signed in as the last seat; a visitor is nobody.
                    if (width > 0) await LogoutAsync();
                }
                else
                {
                    var (email, password) = seat == "superadmin"
                        ? (SuperAdminEmail, SuperAdminPassword)
                        : (UserEmail, UserPassword);

                    if (string.IsNullOrWhiteSpace(password))
                    {
                        body.Append($"\n### {who}\n\nSkipped: no password in the environment.\n");
                        continue;
                    }

                    await LoginAsync(email, password);
                }

                body.Append($"\n### {who}\n\n");

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
                        body.Append($"- `{route}` — could not be read: {ex.Message[..Math.Min(80, ex.Message.Length)]}\n");
                        continue;
                    }

                    seen++;
                    foreach (var f in found) all.Add(new(who + route, f.Kind, f.El, f.Detail));
                    if (found.Count == 0) { body.Append($"- `{route}` — nothing\n"); continue; }

                    body.Append($"- `{route}`\n");
                    foreach (var group in found.GroupBy(f => f.Kind).OrderBy(g => g.Key))
                    {
                        body.Append($"    - **{group.Key}** ({group.Count()})\n");
                        foreach (var f in group.Take(4)) body.Append($"        - `{f.El}` — {f.Detail}\n");
                    }

                    // The two that are never a design choice.
                    foreach (var f in found.Where(f => f.Kind is "text on the card edge" or "content clipped"))
                        hard.Add($"{who} {route}: {f.Kind} — {f.El} — {f.Detail}");
                }
            }
        }

        if (desktop is not null) await Page.SetViewportSizeAsync(desktop.Width, desktop.Height);

        report.Append(VisualRollUp.Render(all)).Append(body);

        var folder = Environment.GetEnvironmentVariable("BEN_VISUAL_AUDIT_OUT")
                     ?? TestContext.CurrentContext.WorkDirectory;
        Directory.CreateDirectory(folder);
        // Named by theme so a dark run does not overwrite the light one it is compared against.
        var file = Path.Combine(folder, $"visual-audit-{ThemeName}.md");
        File.WriteAllText(file, report.ToString());
        TestContext.Out.WriteLine($"{seen} pages read; report at {file}");
        TestContext.Out.WriteLine(report.ToString());

        Assert.That(hard, Is.Empty,
            "these are not design choices — a card's text on its own border, or a box hiding its own "
          + "content:\n  " + string.Join("\n  ", hard));
    }
}
