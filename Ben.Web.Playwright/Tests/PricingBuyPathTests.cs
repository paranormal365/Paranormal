using System.Text.RegularExpressions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Every band on the pricing page leads somewhere you can buy it (beta feedback, 2026-09-14).
/// </summary>
/// <remarks>
/// The page used to show prices and nothing to press. Ben's rule: a visitor with no group is sent to start one on that
/// plan; somebody who runs a group goes straight to that group's billing page. These follow both roads to where they
/// end, and check the bands wear their redesign rather than a screenshot of it.
/// </remarks>
[TestFixture]
[Category("PublicSurface")]
public class PricingBuyPathTests : BenTestBase
{
    private ILocator Bands => Page.Locator("[data-testid='pricing-band']");

    private ILocator PaidBandCta => Bands
        .Filter(new() { HasNotText = "Free" })
        .Locator("[data-testid='pricing-cta'] a, [data-testid='pricing-cta'] button")
        .First;

    [Test]
    public async Task A_visitor_is_sent_to_start_a_group_and_sign_in_keeps_the_chosen_plan()
    {
        await Page.GotoAsync($"{BaseUrl}/pricing");
        await WaitForTheCircuitAsync();
        await Expect(Bands.First).ToBeVisibleAsync(new() { Timeout = 15_000 });

        var start = Bands.GetByRole(AriaRole.Link, new() { Name = "Start a group on this plan" }).First;
        await Expect(start).ToBeVisibleAsync(new() { Timeout = 10_000 });
        var href = await start.GetAttributeAsync("href");
        Assert.That(href, Does.Match(@"^/organizations/new\?tier=[0-9a-f\-]{36}$"));

        await start.ClickAsync();
        // Not signed in: the wizard sends the visitor to sign in, and the way back still names the plan.
        await Expect(Page).ToHaveURLAsync(new Regex(@"/login\?returnUrl=.*tier%3D[0-9a-f\-]{36}", RegexOptions.IgnoreCase),
            new() { Timeout = 15_000 });
    }

    [Test]
    public async Task Somebody_who_runs_one_group_goes_to_its_billing_page_with_the_cadence_they_were_reading()
    {
        // Sarah administers Paranormal365 only; the member-seat seats she may hold elsewhere are not groups she runs.
        await LoginAsync(UserEmail, UserPassword);
        await Page.GotoAsync($"{BaseUrl}/pricing");
        await WaitForTheCircuitAsync();
        await Expect(Bands.First).ToBeVisibleAsync(new() { Timeout = 15_000 });

        var yearly = Page.GetByRole(AriaRole.Button, new() { Name = "Yearly" });
        if (await yearly.CountAsync() > 0) await yearly.First.ClickAsync();

        var choose = Bands.GetByRole(AriaRole.Link, new() { Name = "Choose this plan" }).First
            .Or(Bands.GetByRole(AriaRole.Button, new() { Name = "Choose this plan" }).First);
        await Expect(choose.First).ToBeVisibleAsync(new() { Timeout = 20_000 });

        if (await Bands.GetByRole(AriaRole.Link, new() { Name = "Choose this plan" }).CountAsync() > 0)
        {
            var link = Bands.GetByRole(AriaRole.Link, new() { Name = "Choose this plan" }).First;
            var href = await link.GetAttributeAsync("href") ?? "";
            Assert.That(href, Does.Match(@"^/organizations/[0-9a-f\-]{36}/billing\?interval=\d+&tier=[0-9a-f\-]{36}$"));
            if (await yearly.CountAsync() > 0) Assert.That(href, Does.Contain("interval=12"));

            await link.ClickAsync();
        }
        else
        {
            // Runs several: a chooser names them, and each goes to that group's billing page.
            await Bands.GetByRole(AriaRole.Button, new() { Name = "Choose this plan" }).First.ClickAsync();
            var option = Page.Locator("#pricing-group-chooser a").First;
            await Expect(option).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await option.ClickAsync();
        }

        await Expect(Page).ToHaveURLAsync(new Regex(@"/organizations/[0-9a-f\-]{36}/billing\?interval=\d+&tier="), new() { Timeout = 15_000 });
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Billing" })).ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    [Test]
    public async Task The_start_a_group_wizard_names_the_plan_that_was_chosen()
    {
        // A paid band's id, from the same public price list the pricing page draws.
        var api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        var tiers = (await (await api.GetAsync("/api/public/pricing")).JsonAsync())!.Value.EnumerateArray().ToList();
        var paid = tiers.FirstOrDefault(t => t.GetProperty("prices").EnumerateArray().Any(p => p.GetProperty("price").GetDecimal() > 0));
        if (paid.ValueKind == System.Text.Json.JsonValueKind.Undefined)
            Assert.Ignore("The price list on this database has no paid band.");
        var tierId = paid.GetProperty("id").GetString();
        var tierName = paid.GetProperty("name").GetString()!;

        await LoginAsync(UserEmail, UserPassword);
        await Page.GotoAsync($"{BaseUrl}/organizations/new?tier={tierId}");
        await WaitForTheCircuitAsync();

        var chip = Page.Locator("#newgroup-plan-chosen");
        await Expect(chip).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(chip).ToContainTextAsync(tierName);
        await Expect(chip).ToContainTextAsync("billing page");
    }

    [Test]
    public async Task The_bands_are_designed_cards_with_one_flagged_and_their_limits_as_a_checklist()
    {
        await Page.SetViewportSizeAsync(1280, 900);
        await Page.GotoAsync($"{BaseUrl}/pricing");
        await WaitForTheCircuitAsync();
        await Expect(Bands.First).ToBeVisibleAsync(new() { Timeout = 15_000 });

        var count = await Bands.CountAsync();
        Assert.That(count, Is.GreaterThanOrEqualTo(2));

        var hues = new HashSet<string>();
        for (var i = 0; i < count; i++)
        {
            var band = Bands.Nth(i);
            hues.Add(await band.EvaluateAsync<string>("e => getComputedStyle(e).getPropertyValue('--band-hue').trim()"));
            await Expect(band.Locator(".pricing-band__head .sa-icon")).ToHaveCountAsync(1);
            Assert.That(await band.Locator(".pricing-band__limits li").CountAsync(), Is.GreaterThan(0));
        }
        Assert.That(hues.Count, Is.EqualTo(Math.Min(count, 6)), "Bands next to each other share a colour.");

        var paidMemberBands = await Bands.Filter(new() { HasNotText = "Free" }).Filter(new() { HasNotText = "Tour and event businesses" }).CountAsync();
        await Expect(Page.Locator("[data-testid='pricing-band-featured']")).ToHaveCountAsync(paidMemberBands >= 2 ? 1 : 0);

        // The price is the figure the eye lands on.
        var amountSize = await Bands.First.Locator(".pricing-band__amount").EvaluateAsync<string>("e => getComputedStyle(e).fontSize");
        Assert.That(double.Parse(amountSize.Replace("px", "")), Is.GreaterThanOrEqualTo(36));
    }

    [Test]
    public async Task The_bands_stack_on_a_phone_without_scrolling_sideways()
    {
        await Page.SetViewportSizeAsync(375, 812);
        await Page.GotoAsync($"{BaseUrl}/pricing");
        await WaitForTheCircuitAsync();
        await Expect(Bands.First).ToBeVisibleAsync(new() { Timeout = 15_000 });

        var overflow = await Page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - document.documentElement.clientWidth");
        Assert.That(overflow, Is.LessThanOrEqualTo(0), $"the pricing page scrolls sideways by {overflow}px");

        var first = (await Bands.Nth(0).BoundingBoxAsync())!;
        var second = (await Bands.Nth(1).BoundingBoxAsync())!;
        Assert.That(second.Y, Is.GreaterThan(first.Y + first.Height - 1), "bands sit side by side on a phone");
    }
}
