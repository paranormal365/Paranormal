using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The harness can see every shape this site uses to say "not finished yet" (W13).
/// </summary>
/// <remarks>
/// <para><b>Why this is a guard and not a comment.</b> Every walk waits for
/// <c>BenTestBase.Spinners</c> to reach zero and then treats the page as settled — it photographs
/// it, audits it and reports it. Whatever that locator cannot see is a page measured while it is
/// still empty, and an empty page has no clipped content and no contrast fault, so it comes back
/// CLEAN. The failure mode is a false pass, which nobody investigates, rather than a flake, which
/// somebody does.</para>
///
/// <para>It was <c>.spinner-border:visible</c> alone until 2026-09-22. That covers
/// <c>BenLoaderOverlay</c>, which renders a spinner — but not the 36 places across 34 files that
/// say it in text, of which 33 files have no spinner anywhere. ProductWalk's admin-dashboard step
/// is how it surfaced: the dashboard's loader is a <c>Loading…</c> paragraph, the walk thought the
/// page was done, and the step reported that a fully working page "never said Sign-ins and
/// registrations".</para>
///
/// <para><b>What this test is really protecting.</b> Not the selector — the assumption. A new
/// screen spelling its placeholder any other way ("Loading...", "Please wait…", a bare div) is
/// invisible again, and nothing else in the suite would notice, because the symptom is silence.
/// The last case here is the one that matters: it asserts the site has not grown a placeholder
/// shape the harness does not know.</para>
/// </remarks>
[TestFixture]
[Category("Guard")]
public sealed class LoadingPlaceholdersAreVisibleToTheHarnessTests : BenTestBase
{
    /// <summary>Every markup shape the site actually uses to say it is loading.</summary>
    private static readonly string[] Shapes =
    [
        "<div class=\"spinner-border\"></div>",
        "<p class=\"text-secondary\">Loading…</p>",
        "<p>Loading…</p>",
        "<p class=\"text-secondary small mb-0\">Loading…</p>",
        "<div class=\"text-secondary small mt-3\">Loading…</div>",
        "<div class=\"text-secondary small\">Loading…</div>",
        "<div class=\"text-secondary\">Loading…</div>",
    ];

    [Test]
    public async Task Every_shape_the_site_uses_is_seen_as_still_loading()
    {
        await Page.GotoAsync($"{BaseUrl}/");
        await WaitUntilLoadedAsync();

        var unseen = new List<string>();
        foreach (var shape in Shapes)
        {
            await Page.EvaluateAsync(
                @"html => {
                    document.querySelectorAll('.w13-probe').forEach(n => n.remove());
                    const host = document.querySelector('.app-content, main, .content-wrapper') || document.body;
                    const box = document.createElement('div');
                    box.className = 'w13-probe';
                    box.innerHTML = html;
                    host.appendChild(box);
                }", shape);

            if (await Spinners.CountAsync() == 0) unseen.Add(shape);
        }

        await Page.EvaluateAsync("() => document.querySelectorAll('.w13-probe').forEach(n => n.remove())");

        Assert.That(unseen, Is.Empty,
            "A walk waits for these to disappear before it photographs and audits a page. These "
          + "shapes are on the site and the harness cannot see them, so every screen using one is "
          + "measured while it is still empty — and reported as clean:\n  "
          + string.Join("\n  ", unseen));
    }

    /// <summary>A placeholder the harness would not see must not reach the site unnoticed.</summary>
    [Test]
    public void The_site_has_not_grown_a_placeholder_shape_the_harness_cannot_see()
    {
        var root = RepoRoot();
        var razor = Directory.EnumerateFiles(root, "*.razor", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}worktrees{Path.DirectorySeparatorChar}"));

        // Anything that looks like a loading placeholder but is not the ellipsis spelling the
        // locator matches. "Loading" followed by three dots, or by nothing at all, reads the same
        // on screen and is invisible to every walk.
        var pattern = new System.Text.RegularExpressions.Regex(
            @"<(p|div|span)[^>]*>\s*(Loading\.\.\.|Loading\s*)</(p|div|span)>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        var offenders = new List<string>();
        foreach (var file in razor)
            foreach (System.Text.RegularExpressions.Match m in pattern.Matches(File.ReadAllText(file)))
                offenders.Add($"{Path.GetRelativePath(root, file)}: {m.Value.Trim()}");

        Assert.That(offenders, Is.Empty,
            "These say \"loading\" in a spelling BenTestBase.Spinners does not match, so every walk "
          + "will treat those screens as finished while they are still empty and report them as "
          + "clean. Use \"Loading…\" (the ellipsis character) or BenLoaderOverlay:\n  "
          + string.Join("\n  ", offenders));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
