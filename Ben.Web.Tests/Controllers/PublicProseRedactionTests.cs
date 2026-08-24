using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Cms;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Item 184 Phase B: every public prose surface substitutes real names on a private-engagement
/// case, and renders a non-private case exactly as written (Ben's scope rule, pinned per surface).
/// </summary>
/// <remarks>
/// Probe-regressed: each redaction assertion here was run against the unpatched endpoints first
/// and failed by showing the client's real name — which is precisely the leak the wiring closes.
/// The CMS test exercises <see cref="CmsEmbed.ResolveAsync"/> itself, which both the live page
/// and the authenticated preview call, so preview and live cannot disagree.
/// </remarks>
public class PublicProseRedactionTests
{
    private const string RealFirst = "Daniel";
    private const string RealLast  = "Vexley";   // Distinctive: never a word in ordinary prose.

    private sealed record Seeded(Organization Org, Case Case, AppUser Client);

    private static async Task<Seeded> SeedCaseAsync(
        IDbContextFactory<BenDataContext> factory,
        bool isPrivate,
        string? pseudonym = "The Hargrove Family")
    {
        await using var db = await factory.CreateDbContextAsync();

        var org = new Organization
        {
            Id = Guid.NewGuid(), Name = "Redaction Org", UrlName = $"redaction-org-{Guid.NewGuid():N}"[..20],
            CreatedByAppUserId = Guid.NewGuid(),
        };
        var client = new AppUser
        {
            Id = Guid.NewGuid(), UserName = $"c{Guid.NewGuid():N}@t", Email = "c@t",
            FirstName = RealFirst, LastName = RealLast, DisplayName = $"{RealFirst} {RealLast}",
            DateCreated = DateTime.UtcNow,
        };
        var request = new ClientRequest
        {
            Id = Guid.NewGuid(), AppUserId = client.Id, Status = ClientRequestStatus.Assigned,
            StreetAddress1 = "1 Elm", City = "Nashville", State = "TN", ZipCode = "37201",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = client.Id,
        };
        var @case = new Case
        {
            Id = Guid.NewGuid(), OrganizationId = org.Id,
            Title = $"The {RealLast} house",
            Description = $"<p>{RealFirst} {RealLast} reported knocking. <strong>{RealLast}</strong> heard it too.</p>",
            City = "Nashville", State = "TN", Country = "US",
            CaseYear = 2026, OrgCaseNumber = 42, UrlName = $"vexley-{Guid.NewGuid():N}"[..12],
            Status = CaseStatus.Public, IsPublic = true,
            IsPrivateEngagement = isPrivate,
            ClientRequestId = request.Id,
            PublicPseudonym = pseudonym,
            DateCaseOpened = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            CreatedByAppUserId = org.CreatedByAppUserId,
        };
        var entry = new CaseTimelineEntry
        {
            Id = Guid.NewGuid(), CaseId = @case.Id,
            EntryType = CaseTimelineEntryType.InvestigatorNote,
            Visibility = CaseTimelineVisibility.Public,
            Title = $"{RealFirst} let us in",
            Body = $"<p>{RealFirst} showed us the landing where {RealLast} heard the steps.</p>",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = org.CreatedByAppUserId,
        };

        db.Organizations.Add(org);
        db.Users.Add(client);
        db.ClientRequests.Add(request);
        db.Cases.Add(@case);
        db.CaseTimelineEntries.Add(entry);
        await db.SaveChangesAsync();
        return new Seeded(org, @case, client);
    }

    private static async Task<Investigation> SeedInvestigationAsync(
        IDbContextFactory<BenDataContext> factory, Seeded seeded)
    {
        await using var db = await factory.CreateDbContextAsync();
        var place = new Place
        {
            Id = Guid.NewGuid(), Name = $"The {RealLast} Residence",
            City = "Nashville", State = "TN", Kind = PlaceKind.PrivateResidence,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = seeded.Org.CreatedByAppUserId,
        };
        var investigation = new Investigation
        {
            Id = Guid.NewGuid(), OrganizationId = seeded.Org.Id, CaseId = seeded.Case.Id,
            PlaceId = place.Id,
            Title = $"Night one at {RealLast}'s",
            Notes = $"<p>{RealFirst} stayed downstairs while we set up.</p>",
            UrlName = $"night-one-{Guid.NewGuid():N}"[..14],
            ScheduledDateTime = DateTime.UtcNow, Status = InvestigationStatus.Completed,
            Visibility = InvestigationVisibility.Public,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = seeded.Org.CreatedByAppUserId,
        };
        db.Places.Add(place);
        db.Investigations.Add(investigation);
        await db.SaveChangesAsync();
        return investigation;
    }

    // ── PublicCaseController ─────────────────────────────────────────────────

    [Fact]
    public async Task Case_list_and_detail_substitute_on_a_private_case()
    {
        var factory = TestDbFactory.Create();
        var seeded = await SeedCaseAsync(factory, isPrivate: true);
        var controller = new PublicCaseController(factory, Mock.Of<IMapper>());

        var list = Assert.IsAssignableFrom<IEnumerable<PublicCaseListItem>>(
            Assert.IsType<OkObjectResult>(
                (await controller.GetPublicCases(seeded.Org.UrlName, CancellationToken.None)).Result).Value).ToList();
        Assert.DoesNotContain(RealLast, list[0].Title);
        Assert.Contains("The Hargrove Family", list[0].Title);

        var detail = Assert.IsType<PublicCaseDetail>(
            Assert.IsType<OkObjectResult>(
                (await controller.GetPublicCase(seeded.Org.UrlName, seeded.Case.UrlName!, CancellationToken.None)).Result).Value);

        Assert.DoesNotContain(RealLast, detail.Title);
        Assert.DoesNotContain(RealFirst, detail.Description!);
        Assert.DoesNotContain(RealLast, detail.Description!);
        Assert.Contains("<strong>", detail.Description!);          // markup survives redaction
        Assert.DoesNotContain(RealFirst, detail.Timeline[0].Title!);
        Assert.DoesNotContain(RealLast, detail.Timeline[0].Body!);
    }

    [Fact]
    public async Task Case_list_and_detail_render_a_non_private_case_verbatim()
    {
        var factory = TestDbFactory.Create();
        var seeded = await SeedCaseAsync(factory, isPrivate: false);
        var controller = new PublicCaseController(factory, Mock.Of<IMapper>());

        var detail = Assert.IsType<PublicCaseDetail>(
            Assert.IsType<OkObjectResult>(
                (await controller.GetPublicCase(seeded.Org.UrlName, seeded.Case.UrlName!, CancellationToken.None)).Result).Value);

        // Ben's scope rule: a case not designated private renders exactly as written.
        Assert.Equal(seeded.Case.Title, detail.Title);
        Assert.Equal(seeded.Case.Description, detail.Description);
    }

    // ── PublicCaseDiscoveryController ────────────────────────────────────────

    [Fact]
    public async Task Cross_org_discovery_substitutes_the_title_of_a_private_case()
    {
        var factory = TestDbFactory.Create();
        var seededPrivate = await SeedCaseAsync(factory, isPrivate: true);
        var seededPublic  = await SeedCaseAsync(factory, isPrivate: false);
        var controller = new PublicCaseDiscoveryController(factory);

        var page = Assert.IsType<PublicCaseDiscoveryPagedResponse>(
            Assert.IsType<OkObjectResult>(
                (await controller.GetAll(ct: CancellationToken.None)).Result).Value);

        var privateItem = page.Items.Single(i => i.CaseId == seededPrivate.Case.Id);
        var publicItem  = page.Items.Single(i => i.CaseId == seededPublic.Case.Id);
        Assert.DoesNotContain(RealLast, privateItem.Title);
        Assert.Equal(seededPublic.Case.Title, publicItem.Title);
    }

    // ── PublicInvestigationController ────────────────────────────────────────

    [Fact]
    public async Task Published_investigation_bound_to_a_private_case_is_redacted()
    {
        var factory = TestDbFactory.Create();
        var seeded = await SeedCaseAsync(factory, isPrivate: true);
        var investigation = await SeedInvestigationAsync(factory, seeded);
        var controller = new PublicInvestigationController(factory);

        var list = Assert.IsAssignableFrom<IReadOnlyList<PublicInvestigationListItem>>(
            Assert.IsType<OkObjectResult>(
                (await controller.GetPublished(seeded.Org.UrlName, CancellationToken.None)).Result).Value);
        Assert.DoesNotContain(RealLast, list[0].Title);
        Assert.DoesNotContain(RealLast, list[0].PlaceName ?? "");

        var detail = Assert.IsType<PublicInvestigationDetail>(
            Assert.IsType<OkObjectResult>(
                (await controller.GetPublished(seeded.Org.UrlName, investigation.UrlName!, CancellationToken.None)).Result).Value);
        Assert.DoesNotContain(RealLast, detail.Title);
        Assert.DoesNotContain(RealFirst, detail.Notes ?? "");
        Assert.DoesNotContain(RealLast, detail.PlaceName ?? "");
    }

    [Fact]
    public async Task Published_investigation_on_a_non_private_case_is_verbatim()
    {
        var factory = TestDbFactory.Create();
        var seeded = await SeedCaseAsync(factory, isPrivate: false);
        var investigation = await SeedInvestigationAsync(factory, seeded);
        var controller = new PublicInvestigationController(factory);

        var detail = Assert.IsType<PublicInvestigationDetail>(
            Assert.IsType<OkObjectResult>(
                (await controller.GetPublished(seeded.Org.UrlName, investigation.UrlName!, CancellationToken.None)).Result).Value);
        Assert.Equal(investigation.Title, detail.Title);
        Assert.Equal(investigation.Notes, detail.Notes);
    }

    // ── PublicPlaceController ────────────────────────────────────────────────

    [Fact]
    public async Task Place_page_redacts_investigation_titles_bound_to_private_cases()
    {
        var factory = TestDbFactory.Create();
        var seeded = await SeedCaseAsync(factory, isPrivate: true);
        var investigation = await SeedInvestigationAsync(factory, seeded);
        var controller = new PublicPlaceController(factory);

        var response = Assert.IsType<PublicPlaceResponse>(
            Assert.IsType<OkObjectResult>(
                (await controller.GetById(investigation.PlaceId!.Value, CancellationToken.None)).Result).Value);

        Assert.DoesNotContain(RealLast, response.Investigations[0].Title);
    }

    // ── CmsEmbed (live page AND preview go through the same resolver) ────────

    [Fact]
    public async Task Cms_embedded_case_and_investigation_are_redacted_when_private()
    {
        var factory = TestDbFactory.Create();
        var seeded = await SeedCaseAsync(factory, isPrivate: true);
        var investigation = await SeedInvestigationAsync(factory, seeded);
        await using var db = await factory.CreateDbContextAsync();

        var caseJson = await CmsEmbed.ResolveAsync(
            db, seeded.Org.Id, CmsSectionType.EmbeddedCases,
            CmsEmbed.WriteSettings(new CmsEmbed.Settings([seeded.Case.Id])), CancellationToken.None);
        Assert.DoesNotContain(RealLast, caseJson);
        Assert.DoesNotContain(RealFirst, caseJson);

        var invJson = await CmsEmbed.ResolveAsync(
            db, seeded.Org.Id, CmsSectionType.EmbeddedInvestigations,
            CmsEmbed.WriteSettings(new CmsEmbed.Settings([investigation.Id], ShowApproximateLocation: true)),
            CancellationToken.None);
        Assert.DoesNotContain(RealLast, invJson);
        Assert.DoesNotContain(RealFirst, invJson);
    }

    [Fact]
    public async Task Cms_embedded_case_is_verbatim_when_not_private()
    {
        var factory = TestDbFactory.Create();
        var seeded = await SeedCaseAsync(factory, isPrivate: false);
        await using var db = await factory.CreateDbContextAsync();

        var caseJson = await CmsEmbed.ResolveAsync(
            db, seeded.Org.Id, CmsSectionType.EmbeddedCases,
            CmsEmbed.WriteSettings(new CmsEmbed.Settings([seeded.Case.Id])), CancellationToken.None);
        Assert.Contains(RealLast, caseJson);   // as written — the scope rule again
    }
}
