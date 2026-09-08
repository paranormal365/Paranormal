using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Public;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Covers the fix removing per-request external geocoding from this unauthenticated,
/// public endpoint — it must now surface whatever coordinates are already stored on the
/// Case, never call out to a geocoding service on the request path.
/// </summary>
public class PublicCaseDiscoveryControllerTests
{
    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var opts = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PooledDbContextFactory<BenDataContext>(opts);
    }

    private static PublicCaseDiscoveryController Build(IDbContextFactory<BenDataContext> factory)
        => new(factory);

    private static Organization MakeOrg() => new()
    {
        Id = Guid.NewGuid(), Name = "Test Org", UrlName = $"org-{Guid.NewGuid():N}",
        DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid(),
    };

    private static Case MakeCase(Guid orgId, string title, decimal? lat = null, decimal? lon = null,
        bool isPublic = true, CaseStatus status = CaseStatus.Public) => new()
    {
        Id = Guid.NewGuid(), OrganizationId = orgId, Title = title,
        CaseYear = 2026, OrgCaseNumber = 1,
        StreetAddress1 = "1 Main St", City = "Nashville", State = "TN", ZipCode = "37201", Country = "US",
        Latitude = lat, Longitude = lon,
        IsPublic = isPublic, Status = status,
        DateCaseOpened = DateTime.UtcNow, DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid(),
    };

    /// <summary>
    /// The exact stored coordinates must never reach a public response.
    /// </summary>
    /// <remarks>
    /// This test previously asserted the opposite — that the endpoint returns the stored values
    /// verbatim — while the fields it read were named <c>ApproxLatitude</c>/<c>ApproxLongitude</c>.
    /// The names promised an approximation nothing performed, and the test pinned the leak in place.
    /// A case's coordinates are somebody's home.
    /// </remarks>
    [Fact]
    public async Task GetAll_PublishesAnApproximation_NeverTheStoredCoordinates()
    {
        const decimal trueLat = 36.16m, trueLon = -86.78m;

        var factory = CreateFactory();
        var org     = MakeOrg();
        var c       = MakeCase(org.Id, "Haunted House", lat: trueLat, lon: trueLon);
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Organizations.Add(org);
            db.Cases.Add(c);
            await db.SaveChangesAsync();
        }
        var ctrl = Build(factory);

        var result = await ctrl.GetAll(ct: default);
        var body   = Assert.IsType<PublicCaseDiscoveryPagedResponse>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        var item = Assert.Single(body.Items);

        Assert.NotNull(item.ApproxLatitude);
        Assert.NotNull(item.ApproxLongitude);
        Assert.NotEqual(trueLat, item.ApproxLatitude);
        Assert.NotEqual(trueLon, item.ApproxLongitude);

        // Still useful: near enough that the map puts the case in the right area.
        Assert.True(Math.Abs(item.ApproxLatitude!.Value - trueLat) < 0.2m);
        Assert.True(Math.Abs(item.ApproxLongitude!.Value - trueLon) < 0.2m);

        // Identical on a second call. A per-request offset would let anyone average many responses
        // back to the true point, which is why this is snapped rather than jittered.
        var second = await Build(factory).GetAll(ct: default);
        var repeat = Assert.Single(Assert.IsType<PublicCaseDiscoveryPagedResponse>(
            Assert.IsType<OkObjectResult>(second.Result).Value).Items);
        Assert.Equal(item.ApproxLatitude, repeat.ApproxLatitude);
        Assert.Equal(item.ApproxLongitude, repeat.ApproxLongitude);
    }

    /// <summary>
    /// Two properties on opposite sides of the same street are published at the same point — the
    /// obfuscation is only worth anything if neighbours are indistinguishable.
    /// </summary>
    [Fact]
    public async Task GetAll_PublishesNeighbouringCasesAtTheSamePoint()
    {
        var factory = CreateFactory();
        var org     = MakeOrg();
        var a       = MakeCase(org.Id, "Number 12", lat: 36.1601m, lon: -86.7801m);
        var b       = MakeCase(org.Id, "Number 15", lat: 36.1604m, lon: -86.7799m);
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Organizations.Add(org);
            db.Cases.Add(a);
            db.Cases.Add(b);
            await db.SaveChangesAsync();
        }

        var result = await Build(factory).GetAll(ct: default);
        var items  = Assert.IsType<PublicCaseDiscoveryPagedResponse>(
            Assert.IsType<OkObjectResult>(result.Result).Value).Items;

        Assert.Equal(2, items.Count);
        Assert.Single(items.Select(i => (i.ApproxLatitude, i.ApproxLongitude)).Distinct());
    }

    [Fact]
    public async Task GetAll_OmitsCoordinates_WhenCaseHasNoneStored()
    {
        var factory = CreateFactory();
        var org     = MakeOrg();
        var c       = MakeCase(org.Id, "Unresolved Address");
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Organizations.Add(org);
            db.Cases.Add(c);
            await db.SaveChangesAsync();
        }
        var ctrl = Build(factory);

        var result = await ctrl.GetAll(ct: default);

        var ok   = Assert.IsType<OkObjectResult>(result.Result);
        var body = Assert.IsType<PublicCaseDiscoveryPagedResponse>(ok.Value);
        var item = Assert.Single(body.Items);
        Assert.Null(item.ApproxLatitude);
        Assert.Null(item.ApproxLongitude);
    }

    [Fact]
    public async Task GetAll_ExcludesNonPublicAndNonQualifyingStatusCases()
    {
        var factory = CreateFactory();
        var org = MakeOrg();
        var visible    = MakeCase(org.Id, "Visible", status: CaseStatus.Haunted);
        var notPublic  = MakeCase(org.Id, "Private", isPublic: false, status: CaseStatus.Public);
        var wrongState = MakeCase(org.Id, "Proposed", status: CaseStatus.Proposed);
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Organizations.Add(org);
            db.Cases.AddRange(visible, notPublic, wrongState);
            await db.SaveChangesAsync();
        }
        var ctrl = Build(factory);

        var result = await ctrl.GetAll(ct: default);

        var ok   = Assert.IsType<OkObjectResult>(result.Result);
        var body = Assert.IsType<PublicCaseDiscoveryPagedResponse>(ok.Value);
        var item = Assert.Single(body.Items);
        Assert.Equal("Visible", item.Title);
    }

    [Fact]
    public async Task GetAll_PaginatesAcrossOrganizations()
    {
        var factory = CreateFactory();
        var org = MakeOrg();
        var cases = Enumerable.Range(1, 5).Select(i => MakeCase(org.Id, $"Case {i}")).ToArray();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Organizations.Add(org);
            db.Cases.AddRange(cases);
            await db.SaveChangesAsync();
        }
        var ctrl = Build(factory);

        var result = await ctrl.GetAll(page: 1, pageSize: 2, ct: default);

        var ok   = Assert.IsType<OkObjectResult>(result.Result);
        var body = Assert.IsType<PublicCaseDiscoveryPagedResponse>(ok.Value);
        Assert.Equal(2, body.Items.Count);
        Assert.Equal(5, body.TotalCount);
    }

    /// <summary>
    /// The tally on a public case card counts votes on the CASE.
    /// </summary>
    /// <remarks>
    /// <para>W-H1 of the 2026-09-06 evaluation: a card read "No votes yet" beside its own
    /// "✓ 3 ✗ 0 ? 0 · 3 votes". Neither number was wrong. This endpoint counted EvidenceVotes —
    /// votes on individual files, reached through the timeline — and the widget on the same card
    /// counted CaseVotes, and both were labelled "votes".</para>
    ///
    /// <para>The card's own buttons cast a CaseVote, so this is the number that has to move when
    /// somebody clicks one. Written with a case that has three case votes and no evidence votes:
    /// against the old query it returns zero, which is the bug.</para>
    /// </remarks>
    [Fact]
    public async Task GetAll_CountsVotesOnTheCase_NotOnItsEvidence()
    {
        var factory = CreateFactory();
        var org     = MakeOrg();
        var c       = MakeCase(org.Id, "Bell Witch Cave");

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Organizations.Add(org);
            db.Cases.Add(c);
            db.CaseVotes.AddRange(
                new CaseVote { Id = Guid.NewGuid(), CaseId = c.Id, VoterAppUserId = Guid.NewGuid(),
                               VoteType = EvidenceVoteType.Confirms, DateVoted = DateTime.UtcNow },
                new CaseVote { Id = Guid.NewGuid(), CaseId = c.Id, VoterAppUserId = Guid.NewGuid(),
                               VoteType = EvidenceVoteType.Confirms, DateVoted = DateTime.UtcNow },
                new CaseVote { Id = Guid.NewGuid(), CaseId = c.Id, VoterAppUserId = Guid.NewGuid(),
                               VoteType = EvidenceVoteType.Disputes, DateVoted = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var result = await Build(factory).GetAll(1, 20, "date", default);
        var page   = Assert.IsType<PublicCaseDiscoveryPagedResponse>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        var item   = Assert.Single(page.Items);

        Assert.Equal(3, item.TotalVotes);
        Assert.Equal(2, item.ConfirmsCount);
        Assert.Equal(1, item.DisputesCount);
        Assert.Equal(0, item.InconclusiveCount);
    }

    /// <summary>
    /// And the same number the card's own vote widget reads back.
    /// </summary>
    /// <remarks>
    /// The two endpoints are what the visitor sees above and below one line of a card. Asserting
    /// them equal is the whole of W-H1 — the complaint was never about a wrong count, it was about
    /// two counts.
    /// </remarks>
    [Fact]
    public async Task The_list_and_the_vote_summary_report_the_same_tally()
    {
        var factory = CreateFactory();
        var org     = MakeOrg();
        var c       = MakeCase(org.Id, "Old Mill");

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Organizations.Add(org);
            db.Cases.Add(c);
            db.CaseVotes.Add(new CaseVote
            {
                Id = Guid.NewGuid(), CaseId = c.Id, VoterAppUserId = Guid.NewGuid(),
                VoteType = EvidenceVoteType.Inconclusive, DateVoted = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var ctrl = Build(factory);
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext(),
        };

        var listed = Assert.IsType<PublicCaseDiscoveryPagedResponse>(
            Assert.IsType<OkObjectResult>((await ctrl.GetAll(1, 20, "date", default)).Result).Value);
        var summaries = (IReadOnlyList<Ben.Service.Models.Entities.CaseVoteSummary>)
            Assert.IsType<OkObjectResult>((await ctrl.GetVoteSummaries([c.Id], default)).Result).Value!;

        var card    = Assert.Single(listed.Items);
        var summary = Assert.Single(summaries);

        Assert.Equal(summary.TotalVotes,        card.TotalVotes);
        Assert.Equal(summary.ConfirmsCount,     card.ConfirmsCount);
        Assert.Equal(summary.DisputesCount,     card.DisputesCount);
        Assert.Equal(summary.InconclusiveCount, card.InconclusiveCount);
    }
}
