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
/// Email confirmation uses the dev fallback: SMTP is unconfigured, so the confirmation letter waits
/// in the outbox and the test reads its link there as the SuperAdmin
/// (<see cref="BenTestBase.LinkFromTheOutboxAsync"/>). It used to read the API's log from a
/// <c>BEN_API_LOG</c> variable that run-e2e.sh never set, so this whole journey skipped in every
/// standard run — and a skip reads as a pass.</para>
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
    /// Completes the confirmation the way a dev deployment really does it: with no mail server the
    /// letter waits in the outbox, and its link is the one this sign-up just minted.
    /// </summary>
    private async Task ConfirmFromTheOutboxAsync(string email)
    {
        var link = await LinkFromTheOutboxAsync(email, "/confirm-email?");

        await Page.GotoAsync($"{BaseUrl}{link}");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Confirm my email" })
            .ClickAsync(new() { Timeout = 15_000 });
        await Expect(Page.GetByText("confirmed", new() { Exact = false }).First)
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    private async Task<(string Email, string Password)> NewConfirmedUserAsync(string tag)
    {
        var email = $"journey{tag}@example.com";
        var password = NewTestPassword();
        await SignUpAsync(tag, email, password);
        await ConfirmFromTheOutboxAsync(email);
        return (email, password);
    }

    /// <summary>
    /// A cold account's first sign-in lands in onboarding (item 166 W2) — that IS the product
    /// now, so the journey answers it the way an impatient founder would: Skip. Skipping
    /// stamps the account, so it never reappears for this person.
    /// </summary>
    private async Task SkipOnboardingIfOfferedAsync()
    {
        var skip = Page.Locator("#onboarding-skip");
        try
        {
            await skip.WaitForAsync(new() { Timeout = 8_000 });
        }
        catch (TimeoutException) { return; }   // already onboarded, or the gate chose not to fire

        // The click's proof is LEAVING /onboarding, so ClickUntil (which waits for an element
        // to appear) is the wrong tool here.
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try { await skip.ClickAsync(new() { Timeout = 3_000 }); } catch (TimeoutException) { }
            try
            {
                await Page.WaitForURLAsync(url => !url.Contains("/onboarding"), new() { Timeout = 5_000 });
                return;
            }
            catch (TimeoutException) { /* pre-circuit click was lost — go again */ }
        }
    }

    // ── the journey ──────────────────────────────────────────────────────────

    [Test]
    public async Task A_new_person_founds_a_group_gets_a_tier_members_a_case_and_an_investigation()
    {
        var run = Unique;
        var groupName = $"Journey Group {run}";
        var groupSlug = $"journey-{run}";

        // ── 1. The founder arrives cold ──────────────────────────────────────
        var founder = await NewConfirmedUserAsync($"f{run}");
        await LoginAsync(founder.Email, founder.Password);
        await SkipOnboardingIfOfferedAsync();

        // ── 2. Founds the group — through the founder's own door, not the admin's ──
        await Page.GotoAsync($"{BaseUrl}/organizations/new");
        await FillAndConfirmAsync("#newgroup-name", groupName);
        await FillAndConfirmAsync("#newgroup-url", groupSlug);

        // The door is a WIZARD since item 166 W1: identity, two more steps, then the review's
        // Create. Each Next is walked with ClickUntil because it is only real once the circuit is
        // live.
        await ClickUntilAsync(
            Main.GetByRole(AriaRole.Button, new() { Name = "Next", Exact = true }),
            Main.Locator("#newgroup-city"));
        await ClickUntilAsync(
            Main.GetByRole(AriaRole.Button, new() { Name = "Next", Exact = true }),
            Main.Locator("#newgroup-applications"));

        // "Take cases from clients" is ON by default (550ad7c6, so a new group can be found), and
        // then the step refuses Next until it is told where the group works. This journey's case is
        // the group's own, not a client's, so the founder says no for now — the answer the page
        // itself offers. It went unnoticed because this journey never ran: it read its confirmation
        // links from a log variable run-e2e.sh never set, and skipped.
        await Main.Locator("#newgroup-clients").UncheckAsync();
        await ClickUntilAsync(
            Main.GetByRole(AriaRole.Button, new() { Name = "Next", Exact = true }),
            Main.Locator("#newgroup-review"));

        // The completion signal is the URL, not page text: creation navigates to the new hub
        // (with the wizard's ?welcome=1), and under full-suite load the hub's own render can
        // outlast a text-based retry budget. The URL changes the moment the create succeeds.
        var created = false;
        for (var attempt = 0; attempt < 5 && !created; attempt++)
        {
            try
            {
                await Main.GetByRole(AriaRole.Button, new() { Name = "Create the group", Exact = true })
                    .ClickAsync(new() { Timeout = 5_000 });
            }
            catch (TimeoutException) { }
            try
            {
                await Page.WaitForURLAsync(new System.Text.RegularExpressions.Regex(@"/organizations/[0-9a-f-]{36}(\?.*)?$"),
                    new() { Timeout = 8_000 });
                created = true;
            }
            catch (TimeoutException) { /* click lost pre-circuit, or slow — go again */ }
        }
        Assert.That(created, Is.True, "Creating the group never navigated to its hub.");
        await Expect(Main.GetByText(groupName, new() { Exact = false }).First)
            .ToBeVisibleAsync(new() { Timeout = 30_000 });

        // The founder can open their own hub.
        Assert.That(await OpenOrganizationAsync(groupName), Is.True, "The new group is not in the founder's list.");

        // ── 3. The platform bills the group (the manual provider, with a coupon) ──
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/admin/org-subscriptions");
        var row = Main.Locator("tr", new() { HasTextString = groupName }).First;
        await Expect(row).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await row.GetByRole(AriaRole.Button, new() { Name = "Set" }).ClickAsync();

        await Page.Locator("#sub-status").SelectOptionAsync(new SelectOptionValue { Label = "Active (paid)" });
        await Page.Locator("#sub-tier").SelectOptionAsync(new SelectOptionValue { Index = 2 });  // Small group
        await Page.Locator("#sub-start").FillAsync(DateTime.UtcNow.ToString("MM/dd/yyyy", System.Globalization.CultureInfo.InvariantCulture));
        await Page.Locator("#sub-start").PressAsync("Tab");
        await Page.Locator("#sub-end").FillAsync(DateTime.UtcNow.AddMonths(1).ToString("MM/dd/yyyy", System.Globalization.CultureInfo.InvariantCulture));
        await Page.Locator("#sub-end").PressAsync("Tab");
        await Page.Locator("#sub-coupon").FillAsync("LAUNCH25");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();

        // The grid reloads with the group now Active — and the coupon redeemed, which the
        // campaign's redemption report will show.
        await Expect(row.GetByText("Active")).ToBeVisibleAsync(new() { Timeout = 15_000 });

        // ── 3b. …and only now can it take applications ───────────────────────
        // A brand-new group is on the free lane, where "working with other people is part of a
        // paid plan — a free group is just you", so turning applications on before it is billed is
        // refused. This journey used to do it the other way round, from before the free lane
        // existed; it never noticed, because it never ran (see ConfirmFromTheOutboxAsync).
        await LoginAsync(founder.Email, founder.Password);
        Assert.That(await OpenOrganizationAsync(groupName), Is.True, "The billed group is not in the founder's list.");
        await ClickUntilAsync(
            Main.GetByRole(AriaRole.Button, new() { Name = "Edit", Exact = true }),
            Page.Locator("#edit-accepting-apps"));
        await Page.Locator("#edit-accepting-apps").CheckAsync();
        await ClickUntilAsync(
            Main.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }),
            Main.GetByText("Yes", new() { Exact = false }));

        // ── 4. Two more people arrive, apply, and are approved ───────────────
        var members = new List<(string Email, string Password)>();
        for (var i = 1; i <= 2; i++)
        {
            var member = await NewConfirmedUserAsync($"m{i}{run}");
            members.Add(member);

            await LoginAsync(member.Email, member.Password);
            await SkipOnboardingIfOfferedAsync();
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

        // The form now asks what kind of place this is, and holds Open Case until it is answered
        // (the public/private split of items 184-186). A place the group picked for itself is a
        // public location — the free lane; a private residence is private-engagement work that
        // needs a plan covering it. Another step this journey never learned, because it never ran.
        await Main.GetByRole(AriaRole.Radio, new() { Name = "Public location", Exact = false }).CheckAsync();
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
