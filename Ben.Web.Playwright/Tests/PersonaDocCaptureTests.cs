using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Captures the website screenshots for the per-user-type developer documents.
/// </summary>
/// <remarks>
/// <para><b>One run per seat, and that is the point.</b> The site shows different things to a
/// SuperAdmin, a group owner, an ordinary member, a viewer, a client, and somebody with no account
/// at all — so a document that mixed them would describe a site nobody actually uses. Each persona
/// signs in for real and is photographed walking only the surfaces its own seat reaches.</para>
///
/// <para>This is the same discipline the suite already follows for correctness: an administrator
/// passes every permission check by role, so a surface broken for everyone else looks perfect from
/// that seat. Documenting from one seat would repeat exactly that mistake in prose.</para>
///
/// <para>Runs only when asked, because it writes files and takes minutes:
/// <c>BEN_PERSONA=member dotnet vstest … --TestCaseFilter:PersonaDocCaptureTests</c></para>
/// </remarks>
[TestFixture]
[Category("PersonaDocs")]
public class PersonaDocCaptureTests : BenTestBase
{
    /// <summary>The seeded public location every seat is photographed at.</summary>
    /// <remarks>
    /// One place across the documents on purpose: the point of a per-seat set is that the reader
    /// can compare the same screen between seats, which a different place per persona would
    /// destroy.
    /// </remarks>
    private const string PlaceId = "40000001-0000-0000-0000-000000000001";

    private static string Persona =>
        Environment.GetEnvironmentVariable("BEN_PERSONA")?.Trim().ToLowerInvariant() ?? "";

    private string OutputDirectory
    {
        get
        {
            var root = Environment.GetEnvironmentVariable("BEN_PERSONA_OUT")
                       ?? "docs/web-media";
            var dir = Path.Combine(root, Persona);
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>Full-page, so a document reader sees the whole surface rather than a viewport.</summary>
    private async Task ShotAsync(string name)
    {
        await WaitUntilLoadedAsync();
        await Page.ScreenshotAsync(new()
        {
            Path = Path.Combine(OutputDirectory, name + ".png"),
            FullPage = true,
        });
    }

    /// <summary>Goes to a route and photographs it, saying nothing if the route is not for this seat.</summary>
    private async Task VisitAsync(string name, string route)
    {
        await Page.GotoAsync($"{BaseUrl}{route}");
        await WaitUntilLoadedAsync();
        // No assertion on content: a refusal IS the documentation for a seat that may not go here,
        // and photographing it is more honest than skipping the page silently.
        await ShotAsync(name);
    }

    [Test]
    [Description("Captures one persona's view of the site. Set BEN_PERSONA.")]
    public async Task CaptureThisPersona()
    {
        if (Persona.Length == 0)
            Assert.Ignore("No BEN_PERSONA set — this fixture only runs for a documentation capture.");

        // NOTHING is cleared here, and that is deliberate. The folder is shared:
        // HostedEventPersonaWalk writes its own screens into the same per-seat directories, and a
        // fixture that emptied it would silently delete another's work — which is precisely what
        // happened when this tried to (2026-09-17), taking five screens out of the documents.
        // The consequence is that a screen must never be RENUMBERED: the old file would be left
        // behind and the document would show the page twice. Append new numbers instead.

        // Wide enough that the sidebar is open and tables are not stacked into their mobile form,
        // which is what a developer reading the document needs to see.
        await Page.SetViewportSizeAsync(1440, 900);

        // Dark, by emulating the media query rather than by writing the site's stored preference.
        // ben-boot.js falls back to prefers-color-scheme when no choice has been saved, so this is
        // the site choosing dark for itself — the same path a visitor's OS setting takes — instead
        // of a test reaching into localStorage and pretending a person had chosen it.
        await Page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Dark });

        switch (Persona)
        {
            case "visitor": await VisitorAsync(); break;
            case "client": await ClientAsync(); break;
            case "member": await MemberAsync(); break;
            case "viewer": await ViewerAsync(); break;
            case "owner": await OwnerAsync(); break;
            case "superadmin": await SuperAdminAsync(); break;
            default: Assert.Fail($"Unknown persona '{Persona}'."); break;
        }
    }

    // ── Anonymous ────────────────────────────────────────────────────────────

    private async Task VisitorAsync()
    {
        await VisitAsync("10-home", "/");
        await VisitAsync("11-find-groups", "/find");
        await VisitAsync("12-feed", "/feed");
        await VisitAsync("13-request-an-investigation", "/my-requests/new");
        await VisitAsync("14-sign-in", "/login");
        await VisitAsync("15-sign-up", "/signup");
        await VisitAsync("16-help", "/help");
        // A public location, as somebody with no account: published investigations, the field
        // archive and the posts, and no box to add anything (2026-09-17). Worth a picture in this
        // document because the same page looks different from every seat below, which is the
        // whole reason these documents are per-seat.
        // A seat with no account meeting a page that needs one: the refusal itself is worth
        // showing, because "a refusal must never render as nothing here" is a rule of this site.
        await VisitAsync("17-refused-my-cases", "/my-cases");
        // Appended, not inserted: 18 and 19 belong to HostedEventPersonaWalk, and renumbering
        // anything here would orphan the old file into the document.
        await VisitAsync("1a-a-public-place", $"/places/{PlaceId}");
    }

    // ── Client ───────────────────────────────────────────────────────────────

    private async Task ClientAsync()
    {
        await LoginAsync(ClientEmail, ClientPassword);
        await VisitAsync("20-my-cases", "/my-cases");
        await VisitAsync("21-my-requests", "/my-requests");
        await VisitAsync("22-notifications", "/notifications");
        await VisitAsync("23-my-evidence", "/my-evidence");
        await VisitAsync("24-pricing", "/pricing");
        await VisitAsync("25-profile", "/profile");
        await VisitAsync("26-refused-admin", "/admin/users");
    }

    // ── Ordinary member ──────────────────────────────────────────────────────

    private async Task MemberAsync()
    {
        await LoginAsync(MemberEmail, MemberPassword);
        await VisitAsync("30-home", "/");
        await VisitAsync("31-organizations", "/organizations");
        await VisitAsync("32-my-investigations", "/my-investigations");
        await VisitAsync("33-media-library", "/media-library");
        await VisitAsync("34-my-equipment", "/my-equipment");
        await VisitAsync("35-events", "/events");
        await VisitAsync("36-feed", "/feed");
        await VisitAsync("37-profile", "/profile");
        await VisitAsync("38-refused-admin", "/admin/users");
        // The same place, signed in as a member: their groups' visits and cases beside what other
        // groups shared, and the buttons that start work here. 39 and 3a are the event walk's.
        await VisitAsync("3b-a-public-place", $"/places/{PlaceId}");
    }

    // ── Viewer ───────────────────────────────────────────────────────────────

    private async Task ViewerAsync()
    {
        await LoginAsync(ViewerEmail, ViewerPassword);
        await VisitAsync("40-home", "/");
        await VisitAsync("41-organizations", "/organizations");
        await VisitAsync("42-my-investigations", "/my-investigations");
        await VisitAsync("43-media-library", "/media-library");
        await VisitAsync("44-my-equipment", "/my-equipment");
    }

    // ── Group owner / administrator ──────────────────────────────────────────

    private async Task OwnerAsync()
    {
        await LoginAsync(UserEmail, UserPassword);
        await VisitAsync("50-home", "/");
        await VisitAsync("51-organizations", "/organizations");
        await VisitAsync("52-my-cases", "/my-cases");
        await VisitAsync("53-my-investigations", "/my-investigations");
        await VisitAsync("54-my-equipment", "/my-equipment");
        await VisitAsync("55-events", "/events");
        // The owner's OWN billing page. This used to visit /admin/org-subscriptions - an app-admin
        // route - and photographed the redirect to Home as "subscriptions" (found 2026-09-03).
        await VisitAsync("56-org-subscriptions", $"/organizations/{await OrgIdBySlugAsync("benco")}/billing");
        await VisitAsync("57-profile", "/profile");
        // Item 233: the tours screen, for a group that runs them. BenCo does not, so this
        // photographs the door rather than pretending otherwise — an owner of a walking-tour
        // business sees the list here, and one who does not run tours sees the page say so.
        await VisitAsync("58-tours", $"/organizations/{await OrgIdBySlugAsync("benco")}/tours");
        // The place, and a new case naming it (2026-09-17) — the offer, and the kind choice that
        // has no default, arrived at with the place already settled as it is from a place's own
        // page. 5a to 5c are the event walk's, so these follow them.
        await VisitAsync("5d-a-public-place", $"/places/{PlaceId}");
        await VisitAsync("5e-new-case-at-a-place",
            $"/organizations/{await OrgIdBySlugAsync("benco")}/cases/new?place={PlaceId}");
    }

    // ── SuperAdmin ───────────────────────────────────────────────────────────

    private async Task SuperAdminAsync()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await VisitAsync("60-dashboard", "/admin/dashboard");
        await VisitAsync("61-users", "/admin/users");
        await VisitAsync("62-admin-cases", "/admin/cases");
        await VisitAsync("63-site-settings", "/admin/site-settings");
        await VisitAsync("64-audit-log", "/admin/audit-log");
        await VisitAsync("65-outgoing-mail", "/admin/mail");
        await VisitAsync("66-rate-limits", "/admin/rate-limits");
        await VisitAsync("67-billing-ledger", "/admin/billing-ledger");
        await VisitAsync("68-referrals", "/admin/referrals");
        await VisitAsync("69-support-tickets", "/admin/support-tickets");
    }
}
