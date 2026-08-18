using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Tests for all SuperAdmin pages:
/// users, user detail, user create, file types, roles, audit log,
/// experience taxonomy, and lookup types.
/// All tests authenticate as SuperAdmin.
/// </summary>
[TestFixture]
[Category("Admin")]
public class AdminTests : BenTestBase
{
    [SetUp]
    public async Task SignInAsSuperAdmin() => await LoginAsync(SuperAdminEmail, SuperAdminPassword);

    // ── Admin user list ───────────────────────────────────────────────────────

    [Test]
    public async Task AdminUsers_RendersUserList()
    {
        await Page.GotoAsync($"{BaseUrl}/admin/users");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Expect(Page.GetByText("AverageBen", new() { Exact = false }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    [Test]
    public async Task AdminUsers_HasCreateUserButton()
    {
        await Page.GotoAsync($"{BaseUrl}/admin/users");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var createBtn = Page.GetByText("Create", new() { Exact = false })
                            .Or(Page.GetByText("New User", new() { Exact = false }))
                            .First;
        await Expect(createBtn).ToBeVisibleAsync(new() { Timeout = 8_000 });
    }

    [Test]
    public async Task AdminUsers_BlockedForRegularUser()
    {
        await LogoutAsync();
        await LoginAsync(UserEmail, UserPassword);
        await Page.GotoAsync($"{BaseUrl}/admin/users");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        // Should not show user management data
        var isAdminPage = await Main.GetByText("AverageBen").First.IsVisibleAsync();
        Assert.That(isAdminPage, Is.False, "Regular user should not see admin user list.");
    }

    // ── User detail ───────────────────────────────────────────────────────────

    [Test]
    public async Task AdminUserDetail_NavigateFromList()
    {
        await Page.GotoAsync($"{BaseUrl}/admin/users");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var viewLink = Page.GetByRole(AriaRole.Link, new() { Name = "View" })
                           .Or(Page.GetByRole(AriaRole.Button, new() { Name = "View" }))
                           .Or(Page.GetByRole(AriaRole.Button, new() { Name = "Detail" }))
                           .First;
        await Expect(viewLink).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await viewLink.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        Assert.That(Page.Url, Does.Match(@"/admin/users/[0-9a-f\-]+"), "Expected navigation to user detail.");
    }

    [Test]
    public async Task AdminUserDetail_HasProfileTab()
    {
        await Page.GotoAsync($"{BaseUrl}/admin/users");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var viewLink = Page.GetByRole(AriaRole.Link, new() { Name = "View" })
                           .Or(Page.GetByRole(AriaRole.Button, new() { Name = "View" }))
                           .Or(Page.GetByRole(AriaRole.Button, new() { Name = "Detail" }))
                           .First;
        await viewLink.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var profileTab = Page.GetByText("Profile", new() { Exact = false });
        await Expect(profileTab).ToBeVisibleAsync(new() { Timeout = 8_000 });
    }

    // ── Create user ───────────────────────────────────────────────────────────

    [Test]
    public async Task AdminUserCreate_PageRenders()
    {
        await Page.GotoAsync($"{BaseUrl}/admin/users/create");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Expect(Page.Locator("input[type='email'], input[type='text']").First)
            .ToBeVisibleAsync(new() { Timeout = 8_000 });
    }

    // ── File types ────────────────────────────────────────────────────────────

    [Test]
    public async Task AdminFileTypes_RendersFileTypeList()
    {
        await Page.GotoAsync($"{BaseUrl}/admin/file-types");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var body = await Page.InnerTextAsync("body");
        Assert.That(body, Does.Not.Contain("An unhandled error has occurred"));
        // Should show at least one file type (audio seeded by UploadFileTypeSeeder)
        Assert.That(body, Does.Contain("Audio").Or.Contain("Image").Or.Contain("Document"),
            "Expected at least one seeded file type.");
    }

    [Test]
    public async Task AdminFileTypes_HasNewFileTypeButton()
    {
        await Page.GotoAsync($"{BaseUrl}/admin/file-types");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var newBtn = Page.GetByText("New File Type", new() { Exact = false })
                         .Or(Page.GetByRole(AriaRole.Button, new() { Name = "New" }))
                         .First;
        await Expect(newBtn).ToBeVisibleAsync(new() { Timeout = 8_000 });
    }

    // ── Roles ─────────────────────────────────────────────────────────────────

    [Test]
    public async Task AdminRoles_RendersSuperAdminRole()
    {
        await Page.GotoAsync($"{BaseUrl}/admin/roles");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Expect(Page.GetByText("SuperAdmin", new() { Exact = false }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    // ── Audit log ─────────────────────────────────────────────────────────────

    [Test]
    public async Task AdminAuditLog_PageRenders()
    {
        await Page.GotoAsync($"{BaseUrl}/admin/audit-log");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var body = await Page.InnerTextAsync("body");
        Assert.That(body, Does.Not.Contain("An unhandled error has occurred"));
    }

    // ── Experience taxonomy ───────────────────────────────────────────────────

    [Test]
    public async Task AdminExperienceTaxonomy_ShowsCategories()
    {
        await Page.GotoAsync($"{BaseUrl}/admin/experience-taxonomy");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        // ExperienceTaxonomySeeder seeds Audible, Visual, Physical, Olfactory, Psychological
        // .First because the taxonomy page lists each category in both its tree and its detail
        // pane, so an unscoped match trips strict mode.
        await Expect(Main.GetByText("Audible", new() { Exact = false }).First)
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    // ── Lookup types ──────────────────────────────────────────────────────────

    [Test]
    public async Task AdminLookupTypes_PageRenders()
    {
        await Page.GotoAsync($"{BaseUrl}/admin/lookup-types");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var body = await Page.InnerTextAsync("body");
        Assert.That(body, Does.Not.Contain("An unhandled error has occurred"));
    }

    // ── Admin side panel ──────────────────────────────────────────────────────

    [Test]
    public async Task AdminSidePanel_OpensOnAdministrationButtonClick()
    {
        await Page.GotoAsync(BaseUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // The admin tools used to live in a right-hand side panel. They are now a group inside the
        // sidebar: Administration opens into Users, which in turn holds Manage Users. Opening both
        // levels is the equivalent journey.
        var admin = Page.Locator(".nav-menu > li").Filter(new() { HasText = "Administration" }).First;
        await Expect(admin).ToBeVisibleAsync(new() { Timeout = 8_000 });
        await admin.Locator("> a").ClickAsync();

        var users = admin.Locator("> ul > li").Filter(new() { HasText = "Users" }).First;
        await Expect(users).ToBeVisibleAsync(new() { Timeout = 5_000 });
        await users.Locator("> a").ClickAsync();

        var usersLink = Page.GetByText("Manage Users", new() { Exact = false });
        await Expect(usersLink).ToBeVisibleAsync(new() { Timeout = 5_000 });
    }
}
