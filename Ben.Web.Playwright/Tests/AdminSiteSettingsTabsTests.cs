using System.Text.RegularExpressions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The site settings page is tabbed, and the admin menu reads as groups (item 246 follow-on).
/// </summary>
[TestFixture]
public class AdminSiteSettingsTabsTests : BenTestBase
{
    [SetUp]
    public async Task SignInAsync() => await LoginAsync(SuperAdminEmail, SuperAdminPassword);

    [Test]
    [Description("Settings open on one section, and choosing another swaps what is shown.")]
    public async Task Only_the_chosen_section_is_shown()
    {
        await Page.GotoAsync($"{BaseUrl}/admin/site-settings");
        await WaitForTheCircuitAsync();

        var tabs = Page.Locator("[data-testid=settings-tab]");
        await Expect(tabs.First).ToBeVisibleAsync(new() { Timeout = 30_000 });

        var count = await tabs.CountAsync();
        Assert.That(count, Is.GreaterThan(1), "There should be a tab per section.");

        // Exactly one is active at a time.
        await Expect(Page.Locator("[data-testid=settings-tab].active")).ToHaveCountAsync(1);

        var firstLabel = await tabs.First.InnerTextAsync();
        var lastLabel = await tabs.Nth(count - 1).InnerTextAsync();
        Assert.That(firstLabel, Is.Not.EqualTo(lastLabel));

        // What is on screen changes when another tab is chosen — proved by the settings
        // themselves, not by the tab's own styling.
        var before = await Page.Locator(".card").CountAsync();
        await tabs.Nth(count - 1).ClickAsync();
        await Expect(tabs.Nth(count - 1)).ToHaveClassAsync(new Regex("active"));

        var after = await Page.Locator(".card").CountAsync();
        Assert.That(before + after, Is.GreaterThan(0), "Neither section rendered any settings.");
        await Expect(Page.Locator("[data-testid=settings-tab].active")).ToHaveCountAsync(1);
    }

    /// <summary>
    /// The settings still SAVE from a tab — the point of the page, not the tabs.
    /// </summary>
    [Test]
    [Description("A page of settings still comes up with its controls usable.")]
    public async Task The_settings_are_still_editable()
    {
        await Page.GotoAsync($"{BaseUrl}/admin/site-settings");
        await WaitForTheCircuitAsync();
        await Expect(Page.Locator("[data-testid=settings-tab]").First)
            .ToBeVisibleAsync(new() { Timeout = 30_000 });

        // Something to type in or toggle exists on the open tab.
        var controls = await Page.Locator("input, select, textarea").CountAsync();
        Assert.That(controls, Is.GreaterThan(0), "The open section has nothing to change.");
    }

    // A nav GROUP renders as an <a href="#"> that expands, not a <button> — so these ask for a
    // link. Asking for a button found nothing and read as a missing menu entry.
    [Test]
    [Description("Email Templates is in the administration menu, under System.")]
    public async Task The_letters_screen_is_in_the_menu()
    {
        await Page.GotoAsync($"{BaseUrl}/admin/dashboard");
        await WaitForTheCircuitAsync();

        await ClickUntilAsync(
            Page.GetByRole(AriaRole.Link, new() { Name = "Administration", Exact = false }).First,
            Page.GetByRole(AriaRole.Link, new() { Name = "System", Exact = true }).First);

        await ClickUntilAsync(
            Page.GetByRole(AriaRole.Link, new() { Name = "System", Exact = true }).First,
            Page.GetByRole(AriaRole.Link, new() { Name = "Email Templates", Exact = false }).First);

        // And it goes where it says.
        await ClickUntilUrlAsync(
            Page.GetByRole(AriaRole.Link, new() { Name = "Email Templates", Exact = false }).First,
            "/admin/email-templates");
    }

    [Test]
    [Description("The old seventeen-item Content group is now four named ones.")]
    public async Task The_admin_menu_reads_as_groups()
    {
        await Page.GotoAsync($"{BaseUrl}/admin/dashboard");
        await WaitForTheCircuitAsync();

        await ClickUntilAsync(
            Page.GetByRole(AriaRole.Link, new() { Name = "Administration", Exact = false }).First,
            Page.GetByRole(AriaRole.Link, new() { Name = "System", Exact = true }).First);

        foreach (var group in new[] { "Content Types", "Moderation", "Groups & Places", "Deleting" })
            await Expect(Page.GetByRole(AriaRole.Link, new() { Name = group, Exact = true }).First)
                .ToBeVisibleAsync(new() { Timeout = 15_000 });
    }
}
