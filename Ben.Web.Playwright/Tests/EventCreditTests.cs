using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Event credits, end to end on the running site (item 235, phase 1B).
/// </summary>
/// <remarks>
/// <para>The card on a group's billing page was the last piece of the credit that a person could
/// actually see. Before it, a credit somebody had bought was invisible everywhere on the site until
/// it was spent, so "what have we paid for?" had no answer and a refusal to publish sent people
/// nowhere.</para>
///
/// <para>A fresh group is registered through the same endpoint the Start a Group wizard uses, then
/// deleted whatever happened — a group with a plan already on it would prove nothing about the
/// credit path, which only opens for groups whose plan does not include hosting.</para>
///
/// <para><b>What this does not do:</b> it never completes a Stripe payment. Checkout is proved as
/// far as the session Stripe hands back, which exercises permission, price, tax and the metadata
/// the webhook later reads; the rest of the purchase is covered by the fulfilment tests, which can
/// replay a webhook and this cannot.</para>
/// </remarks>
[TestFixture]
[Category("EventCredits")]
public class EventCreditTests : BenTestBase
{
    [Test]
    public async Task A_group_sees_what_a_credit_costs_and_that_it_holds_none()
    {
        var api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        var (orgId, headers) = await RegisterGroupAsync(api);

        try
        {
            await LoginAsync(UserEmail, UserPassword);
            await Page.GotoAsync($"{BaseUrl}/organizations/{orgId}/billing");
            await WaitUntilLoadedAsync();

            var card = Page.Locator("#event-credits");
            await Expect(card).ToBeVisibleAsync(new() { Timeout = 20_000 });

            // The honest empty state. A brand-new group holds none, and the card has to say so
            // rather than show an empty table somebody reads as a loading failure.
            await Expect(card).ToContainTextAsync("no credits");

            // The price, because a Buy button with no number beside it is a guess.
            await Expect(card.GetByText("$", new() { Exact = false }).First)
                .ToBeVisibleAsync(new() { Timeout = 10_000 });
            await Expect(Page.Locator("#buy-event-credits")).ToBeVisibleAsync();

            Assert.That(await Page.InnerTextAsync("body"), Does.Not.Contain("An unhandled error has occurred"));

            // A picture of the card, because "look and style are a deliverable" and a card nobody
            // has looked at is a card nobody has checked.
            var shot = Path.Combine(Path.GetTempPath(), "event-credits-card.png");
            await card.ScreenshotAsync(new() { Path = shot });
            TestContext.Out.WriteLine($"Card captured at {shot}");
        }
        finally
        {
            await DeleteGroupAsync(api, orgId);
            await api.DisposeAsync();
        }
    }

    [Test]
    public async Task The_refusal_to_publish_leads_to_the_place_credits_are_bought()
    {
        var api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        var (orgId, headers) = await RegisterGroupAsync(api);

        try
        {
            // How this group pays for an event. With no tier on the site excluding HostEvents,
            // every capability fail-opens and this group is on the plan path — which is correct
            // behaviour for a half-configured price list, and means there is no credit refusal to
            // look at. That is a configuration fact, not a defect.
            var plan = await api.GetAsync($"/api/organizations/{orgId}/events/plan", new() { Headers = headers });
            Assert.That(plan.Ok, Is.True, await plan.TextAsync());
            var paysWith = (await plan.JsonAsync())!.Value.GetProperty("paysWith").GetString();
            if (paysWith != "credit")
                Assert.Ignore("No band on this deployment excludes hosting events, so this group pays by plan.");

            await LoginAsync(UserEmail, UserPassword);
            await Page.GotoAsync($"{BaseUrl}/organizations/{orgId}/events");
            await WaitUntilLoadedAsync();

            var note = Page.Locator("#events-plan-note");
            await Expect(note).ToBeVisibleAsync(new() { Timeout = 20_000 });

            // The refusal names the price — a refusal that does not is one somebody has to go and
            // research — and carries the way to fix it. A dead end here is the whole failure.
            await Expect(note).ToContainTextAsync("$99");
            var link = Page.Locator("#events-buy-credits");
            await Expect(link).ToBeVisibleAsync();
            Assert.That(await link.GetAttributeAsync("href"),
                Is.EqualTo($"/organizations/{orgId}/billing#event-credits"));
        }
        finally
        {
            await DeleteGroupAsync(api, orgId);
            await api.DisposeAsync();
        }
    }

    [Test]
    public async Task Buying_credits_reaches_a_real_checkout_page()
    {
        // Not a click-through: pressing the button leaves the site, and what is worth proving is
        // the server path behind it — permission, price, tax and the metadata the webhook reads —
        // which ends at the session Stripe hands back.
        var api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        var (orgId, headers) = await RegisterGroupAsync(api);

        try
        {
            var start = await api.PostAsync(
                $"/api/organizations/{orgId}/subscription/checkout/event-credits?quantity=2",
                new() { Headers = headers, DataObject = new { } });

            if (start.Status == 503)
                Assert.Ignore("Online payment is not configured on this deployment.");

            Assert.That(start.Ok, Is.True, await start.TextAsync());
            var url = (await start.JsonAsync())!.Value.GetProperty("redirectUrl").GetString();
            Assert.That(url, Does.Contain("stripe.com"),
                "the buy control has to land on a real checkout page, not somewhere in-app");
        }
        finally
        {
            await DeleteGroupAsync(api, orgId);
            await api.DisposeAsync();
        }
    }

    // ── the group these tests are about ───────────────────────────────────────

    private async Task<(string OrgId, Dictionary<string, string> Headers)> RegisterGroupAsync(IAPIRequestContext api)
    {
        var login = await api.PostAsync("/login", new() { DataObject = new { email = UserEmail, password = UserPassword } });
        Assert.That(login.Ok, Is.True, "the seeded member should be able to sign in");
        var token = (await login.JsonAsync())!.Value.GetProperty("accessToken").GetString();
        var headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" };

        var slug = $"pw-credits-{Guid.NewGuid():N}"[..22];
        var register = await api.PostAsync("/api/security/organizations/register", new()
        {
            Headers = headers,
            DataObject = new { name = "Playwright Event House", urlName = slug },
        });
        Assert.That(register.Ok, Is.True, await register.TextAsync());

        return ((await register.JsonAsync())!.Value.GetProperty("organizationId").GetString()!, headers);
    }

    private async Task DeleteGroupAsync(IAPIRequestContext api, string orgId)
    {
        var admin = await api.PostAsync("/login", new() { DataObject = new { email = SuperAdminEmail, password = SuperAdminPassword } });
        if (!admin.Ok) return;
        var adminToken = (await admin.JsonAsync())!.Value.GetProperty("accessToken").GetString();
        await api.DeleteAsync($"/api/organizations/{orgId}",
            new() { Headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {adminToken}" } });
    }
}
