using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Public;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Controllers;

public class PublicCaseControllerTests
{
    private static PublicCaseController Build(IDbContextFactory<BenDataContext> factory)
        => new(factory, Mock.Of<IMapper>());

    private static async Task<(Organization org, Case c)> SeedPublicCaseAsync(
        IDbContextFactory<BenDataContext> factory,
        CaseStatus status = CaseStatus.Public,
        bool isPublic = true,
        string? pseudonym = null)
    {
        await using var db = await factory.CreateDbContextAsync();

        var org = new Organization { Id = Guid.NewGuid(), Name = "Test Org", UrlName = "test-org", CreatedByAppUserId = Guid.NewGuid() };
        var @case = new Case
        {
            Id                 = Guid.NewGuid(),
            OrganizationId     = org.Id,
            Title              = "The Haunted Manor",
            City               = "Springfield",
            State              = "IL",
            Country            = "US",
            CaseYear           = 2026,
            OrgCaseNumber      = 1,
            Status             = status,
            IsPublic           = isPublic,
            DateCaseOpened     = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            Description        = "<p>Strange events.</p>",
            PublicPseudonym    = pseudonym,
            CreatedByAppUserId = org.CreatedByAppUserId,
        };

        db.Organizations.Add(org);
        db.Cases.Add(@case);
        await db.SaveChangesAsync();
        return (org, @case);
    }

    // ── GetPublicCases ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetPublicCases_UnknownOrg_Returns404()
    {
        var factory = TestDbFactory.Create();
        var result  = await Build(factory).GetPublicCases("unknown-org", CancellationToken.None);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetPublicCases_ReturnsOnlyPublicAndHauntedCases()
    {
        var factory = TestDbFactory.Create();
        await using var db = await factory.CreateDbContextAsync();

        var org = new Organization { Id = Guid.NewGuid(), Name = "O", UrlName = "testorg", CreatedByAppUserId = Guid.NewGuid() };
        db.Organizations.Add(org);

        var statuses = Enum.GetValues<CaseStatus>();
        int i = 1;
        foreach (var s in statuses)
        {
            db.Cases.Add(new Case
            {
                Id = Guid.NewGuid(), OrganizationId = org.Id, Title = $"Case {s}",
                City = "City", State = "ST", Country = "US",
                CaseYear = 2026, OrgCaseNumber = i++, Status = s, IsPublic = true,
                DateCaseOpened     = DateTime.UtcNow,
                CreatedByAppUserId = org.CreatedByAppUserId,
            });
        }
        await db.SaveChangesAsync();

        var result = await Build(factory).GetPublicCases("testorg", CancellationToken.None);
        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var list   = Assert.IsAssignableFrom<IEnumerable<PublicCaseListItem>>(ok.Value);
        Assert.All(list, item => Assert.True(item.IsHaunted || item.Status == CaseStatus.Public || item.Status == CaseStatus.Haunted));
        Assert.DoesNotContain(list, item => item.Status == CaseStatus.Proposed);
        Assert.DoesNotContain(list, item => item.Status == CaseStatus.Accepted);
    }

    [Fact]
    public async Task GetPublicCases_PrivateCases_AreExcluded()
    {
        var factory = TestDbFactory.Create();
        await SeedPublicCaseAsync(factory, CaseStatus.Public, isPublic: false);

        var result = await Build(factory).GetPublicCases("test-org", CancellationToken.None);
        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var list   = Assert.IsAssignableFrom<IEnumerable<PublicCaseListItem>>(ok.Value);
        Assert.Empty(list);
    }

    // ── GetPublicCase ─────────────────────────────────────────────────────────

    /// <summary>
    /// Renamed and inverted deliberately when cases gained readable addresses (item #89).
    /// </summary>
    /// <remarks>
    /// This asserted a 400 saying "expected format 2026-042", which was right while a case could
    /// only be reached by reference. Now that the same segment carries a slug, "not-a-ref" is an
    /// ordinary address that happens not to exist — and complaining about its format would be wrong
    /// for every readable URL on the site.
    /// </remarks>
    [Fact]
    public async Task GetPublicCase_UnknownAddress_Returns404()
    {
        var factory = TestDbFactory.Create();
        var result  = await Build(factory).GetPublicCase("test-org", "not-a-ref", CancellationToken.None);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetPublicCase_UnknownOrg_Returns404()
    {
        var factory = TestDbFactory.Create();
        var result  = await Build(factory).GetPublicCase("unknown-org", "2026-001", CancellationToken.None);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetPublicCase_UnknownCase_Returns404()
    {
        var factory = TestDbFactory.Create();
        await SeedPublicCaseAsync(factory);  // seeds case #2026-001

        var result = await Build(factory).GetPublicCase("test-org", "2026-999", CancellationToken.None);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetPublicCase_PrivateCase_Returns404()
    {
        var factory = TestDbFactory.Create();
        await SeedPublicCaseAsync(factory, CaseStatus.Public, isPublic: false);

        var result = await Build(factory).GetPublicCase("test-org", "2026-001", CancellationToken.None);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetPublicCase_AcceptedStatus_Returns404()
    {
        var factory = TestDbFactory.Create();
        await SeedPublicCaseAsync(factory, CaseStatus.Accepted, isPublic: true);

        var result = await Build(factory).GetPublicCase("test-org", "2026-001", CancellationToken.None);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetPublicCase_ValidPublicCase_ReturnsDetail()
    {
        var factory    = TestDbFactory.Create();
        var (org, _)   = await SeedPublicCaseAsync(factory, CaseStatus.Public);

        var result = await Build(factory).GetPublicCase("test-org", "2026-001", CancellationToken.None);
        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var detail = Assert.IsType<PublicCaseDetail>(ok.Value);

        Assert.Equal("#2026-001", detail.CaseReference);
        Assert.Equal("The Haunted Manor", detail.Title);
        Assert.Equal("Springfield", detail.City);
        Assert.Equal(org.Name, detail.OrgName);
    }

    [Fact]
    public async Task GetPublicCase_HauntedStatus_IsHauntedTrue()
    {
        var factory = TestDbFactory.Create();
        await SeedPublicCaseAsync(factory, CaseStatus.Haunted);

        var result = await Build(factory).GetPublicCase("test-org", "2026-001", CancellationToken.None);
        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var detail = Assert.IsType<PublicCaseDetail>(ok.Value);
        Assert.True(detail.IsHaunted);
    }

    [Fact]
    public async Task GetPublicCase_WithPseudonym_SubstitutesClientName()
    {
        var factory = TestDbFactory.Create();
        await SeedPublicCaseAsync(factory, CaseStatus.Public, pseudonym: "The Smith Family");

        var result = await Build(factory).GetPublicCase("test-org", "2026-001", CancellationToken.None);
        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var detail = Assert.IsType<PublicCaseDetail>(ok.Value);
        Assert.Equal("The Smith Family", detail.ClientName);
    }

    [Fact]
    public async Task GetPublicCase_NoPseudonym_ClientNameIsNull()
    {
        var factory = TestDbFactory.Create();
        await SeedPublicCaseAsync(factory, CaseStatus.Public, pseudonym: null);

        var result = await Build(factory).GetPublicCase("test-org", "2026-001", CancellationToken.None);
        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var detail = Assert.IsType<PublicCaseDetail>(ok.Value);
        Assert.Null(detail.ClientName);
    }

    [Fact]
    public async Task GetPublicCase_ResponseDoesNotContainCoordinates()
    {
        var factory = TestDbFactory.Create();
        await SeedPublicCaseAsync(factory);

        var result = await Build(factory).GetPublicCase("test-org", "2026-001", CancellationToken.None);
        var ok     = Assert.IsType<OkObjectResult>(result.Result);
        var detail = Assert.IsType<PublicCaseDetail>(ok.Value);

        // Privacy assertions — lat/lon must never appear in public case response
        var props = typeof(PublicCaseDetail).GetProperties().Select(p => p.Name);
        Assert.DoesNotContain("Latitude",      props);
        Assert.DoesNotContain("Longitude",     props);
        Assert.DoesNotContain("StreetAddress1", props);
    }

    // ── Readable addresses (item #89) ────────────────────────────────────────

    /// <summary>
    /// A case is reachable by its readable slug — the thing people actually paste to each other.
    /// </summary>
    [Fact]
    public async Task A_case_opens_by_its_readable_slug()
    {
        var factory = TestDbFactory.Create();
        var (org, c) = await SeedPublicCaseAsync(factory);

        await using (var db = await factory.CreateDbContextAsync())
        {
            var row = await db.Cases.SingleAsync(x => x.Id == c.Id);
            row.UrlName = "the-haunted-manor";
            await db.SaveChangesAsync();
        }

        var result = await Build(factory).GetPublicCase(org.UrlName, "the-haunted-manor", default);
        Assert.IsType<OkObjectResult>(result.Result);
    }

    /// <summary>
    /// The old reference still opens the same case. It is what an organization says out loud to a
    /// client, and turning it into a dead end would be a regression dressed up as an improvement.
    /// </summary>
    [Fact]
    public async Task The_case_reference_still_opens_the_same_case()
    {
        var factory = TestDbFactory.Create();
        var (org, c) = await SeedPublicCaseAsync(factory);

        await using (var db = await factory.CreateDbContextAsync())
        {
            var row = await db.Cases.SingleAsync(x => x.Id == c.Id);
            row.UrlName = "the-haunted-manor";
            await db.SaveChangesAsync();
        }

        foreach (var reference in new[] { "2026-001", "#2026-001" })
            Assert.IsType<OkObjectResult>(
                (await Build(factory).GetPublicCase(org.UrlName, reference, default)).Result);
    }

    /// <summary>
    /// A segment that is neither a slug nor a reference is simply not found. It used to be a 400
    /// saying "expected format 2026-042", which would now be wrong for every readable address.
    /// </summary>
    [Fact]
    public async Task An_unknown_address_is_not_found_rather_than_a_complaint_about_format()
    {
        var factory = TestDbFactory.Create();
        var (org, _) = await SeedPublicCaseAsync(factory);

        var result = await Build(factory).GetPublicCase(org.UrlName, "no-such-case", default);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Another_organizations_slug_does_not_resolve_here()
    {
        var factory = TestDbFactory.Create();
        var (_, c) = await SeedPublicCaseAsync(factory);

        await using (var db = await factory.CreateDbContextAsync())
        {
            var row = await db.Cases.SingleAsync(x => x.Id == c.Id);
            row.UrlName = "the-haunted-manor";
            db.Organizations.Add(new Organization
            { Id = Guid.NewGuid(), Name = "Rivals", UrlName = "rivals", CreatedByAppUserId = Guid.NewGuid() });
            await db.SaveChangesAsync();
        }

        var result = await Build(factory).GetPublicCase("rivals", "the-haunted-manor", default);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    // ── The group's own finding (site evaluation 2026-09-06, W-P3) ───────────
    //
    // Three conditions, none implying another: the case is public, the report is Published, and
    // the group switched this report's summary on. Each of the first three tests removes exactly
    // one of them.

    private static async Task<CaseReport> SeedReportAsync(
        IDbContextFactory<BenDataContext> factory, Guid caseId,
        CaseReportStatus status = CaseReportStatus.Published,
        bool showPublicly = true,
        string? summary = "<p>Every recording had a mundane source.</p>",
        string? conclusion = "<p>Not haunted.</p>")
    {
        await using var db = await factory.CreateDbContextAsync();
        var report = new CaseReport
        {
            Id                     = Guid.NewGuid(),
            CaseId                 = caseId,
            Title                  = "Final report",
            Summary                = summary,
            Conclusion             = conclusion,
            Status                 = status,
            IsPublicSummaryVisible = showPublicly,
            PublishedAt            = status == CaseReportStatus.Published ? new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc) : null,
            DateCreated            = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            CreatedByAppUserId     = Guid.NewGuid(),
        };
        db.CaseReports.Add(report);
        await db.SaveChangesAsync();
        return report;
    }

    private static async Task<PublicCaseDetail> GetDetailAsync(
        IDbContextFactory<BenDataContext> factory, Organization org, Case c)
    {
        var result = await Build(factory).GetPublicCase(org.UrlName, $"{c.CaseYear}-{c.OrgCaseNumber:D3}", CancellationToken.None);
        return Assert.IsType<PublicCaseDetail>(Assert.IsType<OkObjectResult>(result.Result).Value);
    }

    [Fact]
    public async Task A_public_summary_shows_only_when_switched_on()
    {
        var factory = TestDbFactory.Create();
        var (org, c) = await SeedPublicCaseAsync(factory);
        await SeedReportAsync(factory, c.Id, showPublicly: false);

        var detail = await GetDetailAsync(factory, org, c);

        Assert.Null(detail.Report);
    }

    [Fact]
    public async Task An_unpublished_report_is_never_shown_however_the_switch_is_set()
    {
        // Publishing a report delivers it to the client. Switching this on says the group is
        // willing to show it. A draft is neither, and the switch must not stand in for the work.
        var factory = TestDbFactory.Create();
        var (org, c) = await SeedPublicCaseAsync(factory);
        await SeedReportAsync(factory, c.Id, status: CaseReportStatus.Draft, showPublicly: true);

        var detail = await GetDetailAsync(factory, org, c);

        Assert.Null(detail.Report);
    }

    [Fact]
    public async Task A_case_that_is_not_public_carries_no_report_because_it_has_no_public_page()
    {
        var factory = TestDbFactory.Create();
        var (org, c) = await SeedPublicCaseAsync(factory, status: CaseStatus.Active, isPublic: false);
        await SeedReportAsync(factory, c.Id);

        var result = await Build(factory).GetPublicCase(org.UrlName, $"{c.CaseYear}-{c.OrgCaseNumber:D3}", CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task A_switched_on_published_report_shows_its_summary_and_conclusion()
    {
        var factory = TestDbFactory.Create();
        var (org, c) = await SeedPublicCaseAsync(factory);
        await SeedReportAsync(factory, c.Id);

        var detail = await GetDetailAsync(factory, org, c);

        Assert.NotNull(detail.Report);
        Assert.Equal("Final report", detail.Report!.Title);
        Assert.Contains("mundane source", detail.Report.Summary);
        Assert.Contains("Not haunted", detail.Report.Conclusion);
        Assert.Equal(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), detail.Report.PublishedAt);
    }

    [Fact]
    public async Task An_empty_report_is_not_a_heading_over_nothing()
    {
        // A group that switches the summary on before writing it would otherwise publish a
        // "What we found" heading with no words under it, which reads as a fault.
        var factory = TestDbFactory.Create();
        var (org, c) = await SeedPublicCaseAsync(factory);
        await SeedReportAsync(factory, c.Id, summary: "   ", conclusion: null);

        var detail = await GetDetailAsync(factory, org, c);

        Assert.Null(detail.Report);
    }

    [Fact]
    public async Task The_newest_switched_on_report_is_the_one_shown()
    {
        // Nothing stops a group switching on two. One conclusion is better than two.
        var factory = TestDbFactory.Create();
        var (org, c) = await SeedPublicCaseAsync(factory);
        await SeedReportAsync(factory, c.Id, summary: "<p>The first pass.</p>", conclusion: null);

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.CaseReports.Add(new CaseReport
            {
                Id = Guid.NewGuid(), CaseId = c.Id, Title = "Revised report",
                Summary = "<p>The second pass.</p>", Status = CaseReportStatus.Published,
                IsPublicSummaryVisible = true,
                PublishedAt = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
                DateCreated = new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc),
                CreatedByAppUserId = Guid.NewGuid(),
            });
            await db.SaveChangesAsync();
        }

        var detail = await GetDetailAsync(factory, org, c);

        Assert.Equal("Revised report", detail.Report!.Title);
        Assert.Contains("second pass", detail.Report.Summary);
    }

    [Fact]
    public async Task A_private_engagements_names_are_substituted_in_the_report_too()
    {
        // The report is prose about a case, so it gets exactly the substitution the title and the
        // timeline get. A report that named the client would undo the whole designation.
        var factory = TestDbFactory.Create();
        var (org, c) = await SeedPublicCaseAsync(factory, pseudonym: "The Westside Family");

        var clientId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Users.Add(new AppUser
            {
                Id = clientId, UserName = "casey@t.com", NormalizedUserName = "CASEY@T.COM",
                Email = "casey@t.com", NormalizedEmail = "CASEY@T.COM",
                FirstName = "Casey", LastName = "Evaluator", DisplayName = "Casey Evaluator",
                DateCreated = DateTime.UtcNow,
            });
            var request = new ClientRequest
            {
                Id = Guid.NewGuid(), AppUserId = clientId, StreetAddress1 = "1 Main",
                City = "Springfield", State = "IL", ZipCode = "62701", Country = "US",
                Status = ClientRequestStatus.Assigned, DateCreated = DateTime.UtcNow,
                CreatedByAppUserId = clientId,
            };
            db.ClientRequests.Add(request);

            var tracked = await db.Cases.FirstAsync(x => x.Id == c.Id);
            tracked.ClientRequestId     = request.Id;
            tracked.IsPrivateEngagement = true;
            await db.SaveChangesAsync();
        }

        await SeedReportAsync(factory, c.Id,
            summary: "<p>We met Casey Evaluator on site.</p>", conclusion: null);

        var detail = await GetDetailAsync(factory, org, c);

        Assert.NotNull(detail.Report);
        Assert.DoesNotContain("Evaluator", detail.Report!.Summary);
    }
}
