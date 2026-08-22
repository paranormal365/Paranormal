using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Ben's "super test": one continuous journey through real screens — a brand-new person signs up,
/// founds a group, the group goes on a paid tier, two more new people apply and are approved, and
/// the group opens a case and schedules an investigation.
/// </summary>
/// <remarks>
/// <para>Every account here is created DURING the test — nothing leans on the seeded roster, so
/// the journey proves the product works for someone arriving cold, which no other fixture does.
/// Email confirmation uses the dev fallback: SMTP is unconfigured, so the confirmation link lands
/// in the API's log, and the test reads it from the file named by <c>BEN_API_LOG</c>. Without
/// that variable the fixture skips rather than pretending.</para>
///
/// <para>Writing it found two write-only features before it ever ran: no screen let anybody APPLY
/// to join a group (the API and the review panel existed; the door didn't), and the manual
/// payment screen had no coupon box (the request field existed; nothing sent it). Both fixed
/// alongside this fixture — which is the "super testing" argument in one sentence.</para>
/// </remarks>
[TestFixture]
[Category("Journey")]
public class NewGroupJourneyTests : BenTestBase
{
    private static string? ApiLogPath => Environment.GetEnvironmentVariable("BEN_API_LOG");
    private static string Unique => Guid.NewGuid().ToString("N")[..8];

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task SignUpAsync(string tag, string email, string password)
    {
        await Page.GotoAsync($"{BaseUrl}/signup");
        await Expect(Page.Locator("#signup-handle")).ToBeVisibleAsync(new() { Timeout = 15_000 });

        await TypeHandleAsync($"journey{tag}");
        await Expect(Page.GetByText("is free.")).ToBeVisibleAsync(new() { Timeout = 15_000 });

        await FillAndConfirmAsync("#signup-first", "Journey");
        await FillAndConfirmAsync("#signup-last", $"User{tag}");
        await FillAndConfirmAsync("#signup-name", $"Journey {tag}");
        await FillAndConfirmAsync("#signup-email", email);
        await FillAndConfirmAsync("#signup-password", password);

        await Page.GetByRole(AriaRole.Button, new() { Name = "Create account" }).ClickAsync();
        await Expect(Page.GetByText("Check your email").First).ToBeVisibleAsync(new() { Timeout = 20_000 });
    }

    /// <summary>
    /// Completes the confirmation the way a dev deployment really does it: the send falls back to
    /// the log, and the newest confirm link is the one this signup just minted.
    /// </summary>
    private async Task ConfirmFromLogAsync()
    {
        var log = ApiLogPath!;
        string? link = null;
        for (var attempt = 0; attempt < 20 && link is null; attempt++)
        {
            var text = await File.ReadAllTextAsync(log);
            link = text.Split('\n')
                .Where(l => l.Contains("/confirm-email?userId="))
                .Select(l => l[l.IndexOf("/confirm-email?userId=", StringComparison.Ordinal)..].Trim())
                .LastOrDefault();
            if (link is null) await Task.Delay(500);
        }
        Assert.That(link, Is.Not.Null, "No confirmation link reached the API log.");

        await Page.GotoAsync($"{BaseUrl}{link}");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Confirm my email" })
            .ClickAsync(new() { Timeout = 15_000 });
        await Expect(Page.GetByText("confirmed", new() { Exact = false }).First)
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    private async Task<(string Email, string Password)> NewConfirmedUserAsync(string tag)
    {
        var email = $"journey{tag}@example.com";
        const string password = "J0urney!Pass";
        await SignUpAsync(tag, email, password);
        await ConfirmFromLogAsync();
        return (email, password);
    }

    // ── the journey ──────────────────────────────────────────────────────────

    [Test]
    public async Task A_new_person_founds_a_group_gets_a_tier_members_a_case_and_an_investigation()
    {
        if (ApiLogPath is null || !File.Exists(ApiLogPath))
            Assert.Ignore("BEN_API_LOG not set — the journey needs the API's log for confirmation links.");

        var run = Unique;
        var groupName = $"Journey Group {run}";
        var groupSlug = $"journey-{run}";

        // ── 1. The founder arrives cold ──────────────────────────────────────
        var founder = await NewConfirmedUserAsync($"f{run}");
        await LoginAsync(founder.Email, founder.Password);

        // ── 2. Founds the group — through the founder's own door, not the admin's ──
        await Page.GotoAsync($"{BaseUrl}/organizations/new");
        await FillAndConfirmAsync("#newgroup-name", groupName);
        await FillAndConfirmAsync("#newgroup-url", groupSlug);
        await ClickUntilAsync(
            Page.Locator("#newgroup-create"),
            Main.GetByText(groupName, new() { Exact = false }));

        // The founder can open their own hub — and turns on applications, which is what makes
        // the group joinable at all.
        Assert.That(await OpenOrganizationAsync(groupName), Is.True, "The new group is not in the founder's list.");
        await ClickUntilAsync(
            Main.GetByRole(AriaRole.Button, new() { Name = "Edit", Exact = true }),
            Page.Locator("#edit-accepting-apps"));
        await Page.Locator("#edit-accepting-apps").CheckAsync();
        await ClickUntilAsync(
            Main.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }),
            Main.GetByText("Yes", new() { Exact = false }));

        // ── 3. The platform bills the group (the manual provider, with a coupon) ──
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/admin/org-subscriptions");
        var row = Main.Locator("tr", new() { HasTextString = groupName }).First;
        await Expect(row).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await row.GetByRole(AriaRole.Button, new() { Name = "Set" }).ClickAsync();

        await Page.Locator("#sub-status").SelectOptionAsync(new SelectOptionValue { Label = "Active (paid)" });
        await Page.Locator("#sub-tier").SelectOptionAsync(new SelectOptionValue { Index = 2 });  // Small group
        await Page.Locator("#sub-start").FillAsync(DateTime.UtcNow.ToString("yyyy-MM-dd"));
        await Page.Locator("#sub-end").FillAsync(DateTime.UtcNow.AddMonths(1).ToString("yyyy-MM-dd"));
        await Page.Locator("#sub-coupon").FillAsync("LAUNCH25");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();

        // The grid reloads with the group now Active — and the coupon redeemed, which the
        // campaign's redemption report will show.
        await Expect(row.GetByText("Active")).ToBeVisibleAsync(new() { Timeout = 15_000 });

        // ── 4. Two more people arrive, apply, and are approved ───────────────
        var members = new List<(string Email, string Password)>();
        for (var i = 1; i <= 2; i++)
        {
            var member = await NewConfirmedUserAsync($"m{i}{run}");
            members.Add(member);

            await LoginAsync(member.Email, member.Password);
            await Page.GotoAsync($"{BaseUrl}/o/{groupSlug}");
            await Expect(Page.Locator("#apply-submit")).ToBeVisibleAsync(new() { Timeout = 15_000 });
            await Page.Locator("#apply-message").FillAsync($"Journey member {i}");
            await ClickUntilAsync(
                Page.Locator("#apply-submit"),
                Page.GetByText("Application sent", new() { Exact = false }));
        }

        await LoginAsync(founder.Email, founder.Password);
        Assert.That(await OpenOrganizationAsync(groupName), Is.True);
        await Main.GetByRole(AriaRole.Tab, new() { Name = "Members", Exact = true }).ClickAsync();

        for (var i = 1; i <= 2; i++)
        {
            // "Accept", not "Approve" — the review grid's own word.
            var accept = Main.GetByRole(AriaRole.Button, new() { Name = "Accept", Exact = true }).First;
            await Expect(accept).ToBeVisibleAsync(new() { Timeout = 15_000 });
            await accept.ClickAsync();
            await Page.WaitForTimeoutAsync(1_500);
        }

        // The roster now holds three people.
        await Expect(Main.GetByText($"Journey m1{run}", new() { Exact = false }).First)
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(Main.GetByText($"Journey m2{run}", new() { Exact = false }).First)
            .ToBeVisibleAsync(new() { Timeout = 10_000 });

        // ── 5. A case, and an investigation on it ────────────────────────────
        var caseTitle = $"Journey case {run}";
        await Main.GetByRole(AriaRole.Tab, new() { Name = "Cases", Exact = true }).ClickAsync();
        await ClickUntilAsync(
            Main.GetByRole(AriaRole.Button, new() { Name = "New Case", Exact = false }),
            Page.Locator("#casecreatepage-case-title-b1b1"));

        await FillAndConfirmAsync("#casecreatepage-case-title-b1b1", caseTitle);
        await FillAndConfirmAsync("#casecreatepage-street-address-5b76", "13 Journey Lane");
        await FillAndConfirmAsync("#casecreatepage-city-4662", "Nashville");
        await FillAndConfirmAsync("#casecreatepage-state-7b45", "TN");
        await FillAndConfirmAsync("#casecreatepage-zip-code-ba79", "37201");
        await ClickUntilAsync(
            Page.GetByRole(AriaRole.Button, new() { Name = "Open Case", Exact = true }),
            Main.GetByText(caseTitle, new() { Exact = false }));

        // On the case page: schedule the investigation.
        var invTitle = $"First night {run}";
        await Main.GetByRole(AriaRole.Tab, new() { Name = "Investigations", Exact = true }).ClickAsync();
        await ClickUntilAsync(
            Main.GetByRole(AriaRole.Button, new() { Name = "Schedule Investigation", Exact = false }),
            Page.Locator("#investigationpanel-title-092d"));
        await FillAndConfirmAsync("#investigationpanel-title-092d", invTitle);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();

        await Expect(Main.GetByText(invTitle, new() { Exact = false }).First)
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
    }
}
