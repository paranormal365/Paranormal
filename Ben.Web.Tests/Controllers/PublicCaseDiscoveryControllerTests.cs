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

    private static PublicCaseDiscoveryController Build(
        IDbContextFactory<BenDataContext> factory, Guid? asUser = null, bool superAdmin = false)
    {
        var claims = new List<System.Security.Claims.Claim>();
        if (asUser is { } id)
            claims.Add(new System.Security.Claims.Claim(
                System.Security.Claims.ClaimTypes.NameIdentifier, id.ToString()));
        if (superAdmin)
            claims.Add(new System.Security.Claims.Claim(
                System.Security.Claims.ClaimTypes.Role, Ben.Data.Common.Constants.RoleNames.SuperAdmin));

        return new(factory, new Ben.Service.RepositoryService.Services.OrganizationSecurityService(factory))
        {
            ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
            {
                HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
                {
                    User = new System.Security.Claims.ClaimsPrincipal(
                        new System.Security.Claims.ClaimsIdentity(claims, asUser is null ? null : "Bearer"))
                }
            }
        };
    }

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

    // ── The viewport (2026-09-09) ────────────────────────────────────────────
    //
    // The page used to ask for 500 cases once and pan over a set that never changed, so past 500
    // it quietly stopped being the whole picture. The bounds are all-or-nothing, and the corners
    // are normalised because map libraries disagree about which one they hand over first.

    private static async Task<IDbContextFactory<BenDataContext>> SeedTwoCitiesAsync()
    {
        var factory = CreateFactory();
        await using var db = await factory.CreateDbContextAsync();
        var org = MakeOrg();
        db.Organizations.Add(org);
        db.Cases.Add(MakeCase(org.Id, "Nashville case",  36.16m, -86.78m));
        db.Cases.Add(MakeCase(org.Id, "Louisville case", 38.25m, -85.75m));
        await db.SaveChangesAsync();
        return factory;
    }

    private static async Task<PublicCaseDiscoveryPagedResponse> GetAsync(
        IDbContextFactory<BenDataContext> factory,
        double? north = null, double? south = null, double? east = null, double? west = null)
    {
        var result = await Build(factory).GetAll(1, 50, "date", north, south, east, west, default);
        return Assert.IsType<PublicCaseDiscoveryPagedResponse>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
    }

    [Fact]
    public async Task No_bounds_still_answers_the_whole_map()
    {
        var page = await GetAsync(await SeedTwoCitiesAsync());

        Assert.Equal(2, page.TotalCount);
    }

    [Fact]
    public async Task Bounds_answer_only_what_is_in_view()
    {
        var page = await GetAsync(await SeedTwoCitiesAsync(),
            north: 36.5, south: 35.8, east: -86.4, west: -87.2);

        Assert.Single(page.Items);
        Assert.Equal("Nashville case", page.Items[0].Title);
    }

    [Fact]
    public async Task Corners_handed_over_backwards_still_mean_the_same_box()
    {
        // North and south swapped, east and west swapped. A reversed box must not read as
        // "nothing here" — that is a mistake wearing the costume of an answer.
        var page = await GetAsync(await SeedTwoCitiesAsync(),
            north: 35.8, south: 36.5, east: -87.2, west: -86.4);

        Assert.Single(page.Items);
        Assert.Equal("Nashville case", page.Items[0].Title);
    }

    [Fact]
    public async Task Three_of_the_four_bounds_is_refused_rather_than_guessed()
    {
        var result = await Build(await SeedTwoCitiesAsync())
            .GetAll(1, 50, "date", north: 36.5, south: 35.8, east: -86.4, west: null, default);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("Give all four bounds, or none.", bad.Value);
    }

    [Fact]
    public async Task A_case_with_no_coordinates_cannot_be_inside_a_box()
    {
        var factory = CreateFactory();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var org = MakeOrg();
            db.Organizations.Add(org);
            db.Cases.Add(MakeCase(org.Id, "Unplaced case"));
            await db.SaveChangesAsync();
        }

        Assert.Equal(1, (await GetAsync(factory)).TotalCount);

        var inView = await GetAsync(factory, north: 90, south: -90, east: 180, west: -180);
        Assert.Empty(inView.Items);
    }

    // ── Cases the caller can already see (2026-09-09) ────────────────────────
    //
    // Ben: "plot the viewer's cases on the home map. plot ones where the end user has access to
    // them or they are already public." The map was empty for a member whose group had work on it,
    // because only IsPublic cases were ever returned.
    //
    // The gates are the ones the owning screens already use, not looser restatements: org-side is
    // HasAccessAsync(Case, Read), the same call CaseController makes, and client-side is the union
    // MyCaseController builds.

    private sealed record Seeded(
        IDbContextFactory<BenDataContext> Factory, Guid OrgId, Guid MemberId, Guid PrivateCaseId);

    private static async Task<Seeded> SeedAPrivateCaseAsync()
    {
        var factory = TestDbFactory.Create();
        var org = MakeOrg();
        var memberId = Guid.NewGuid();
        var privateCase = MakeCase(org.Id, "Not for the world", 36.16m, -86.78m,
                                   isPublic: false, status: CaseStatus.Active);

        await using var db = await factory.CreateDbContextAsync();
        db.Organizations.Add(org);
        db.Cases.Add(privateCase);
        db.Cases.Add(MakeCase(org.Id, "Public one", 36.20m, -86.80m));
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = org.Id, AppUserId = memberId,
            Role = OrganizationMemberRole.Owner, IsActive = true, DateCreated = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        return new Seeded(factory, org.Id, memberId, privateCase.Id);
    }

    private static async Task<PublicCaseDiscoveryPagedResponse> AsAsync(
        IDbContextFactory<BenDataContext> factory, Guid? user = null, bool superAdmin = false)
    {
        var result = await Build(factory, user, superAdmin).GetAll(1, 50, "date", null, null, null, null, default);
        return Assert.IsType<PublicCaseDiscoveryPagedResponse>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
    }

    [Fact]
    public async Task A_visitor_still_sees_only_the_public_ones()
    {
        var seeded = await SeedAPrivateCaseAsync();

        var page = await AsAsync(seeded.Factory);

        Assert.Equal("Public one", Assert.Single(page.Items).Title);
    }

    [Fact]
    public async Task An_owner_sees_their_groups_case_as_well()
    {
        var seeded = await SeedAPrivateCaseAsync();

        var page = await AsAsync(seeded.Factory, seeded.MemberId);

        Assert.Equal(2, page.Items.Count);
        Assert.Contains(page.Items, i => i.CaseId == seeded.PrivateCaseId);
    }

    [Fact]
    public async Task Somebody_elses_account_gains_nothing()
    {
        var seeded = await SeedAPrivateCaseAsync();

        var page = await AsAsync(seeded.Factory, Guid.NewGuid());

        Assert.Equal("Public one", Assert.Single(page.Items).Title);
    }

    [Fact]
    public async Task A_co_client_sees_the_case_they_were_given_access_to()
    {
        var seeded = await SeedAPrivateCaseAsync();
        var clientId = Guid.NewGuid();
        await using (var db = await seeded.Factory.CreateDbContextAsync())
        {
            db.CaseClientAccesses.Add(new CaseClientAccess
            {
                Id = Guid.NewGuid(), CaseId = seeded.PrivateCaseId, AppUserId = clientId,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = seeded.MemberId,
            });
            await db.SaveChangesAsync();
        }

        var page = await AsAsync(seeded.Factory, clientId);

        Assert.Contains(page.Items, i => i.CaseId == seeded.PrivateCaseId);
    }

    [Fact]
    public async Task Your_own_case_is_approximated_like_everybody_elses()
    {
        // One code path, so there is no branch on which a real address could escape. A member who
        // needs the address has the case page.
        var seeded = await SeedAPrivateCaseAsync();

        var mine = (await AsAsync(seeded.Factory, seeded.MemberId))
            .Items.Single(i => i.CaseId == seeded.PrivateCaseId);

        Assert.NotEqual(36.16m, mine.ApproxLatitude);
        Assert.NotEqual(-86.78m, mine.ApproxLongitude);
    }

    [Fact]
    public async Task Membership_alone_is_not_enough_the_cases_grant_is_what_counts()
    {
        // The discriminating case, and the reason the org side calls HasAccessAsync rather than
        // asking "are they a member". A plain Member with no grant on Cases can open none of the
        // group's cases, so the map must not hand them one.
        var factory = TestDbFactory.Create();
        var org = MakeOrg();
        var plainMember = Guid.NewGuid();
        var privateCase = MakeCase(org.Id, "Not for the world", 36.16m, -86.78m,
                                   isPublic: false, status: CaseStatus.Active);

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Organizations.Add(org);
            db.Cases.Add(privateCase);
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = org.Id, AppUserId = plainMember,
                Role = OrganizationMemberRole.Member, IsActive = true, DateCreated = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        Assert.Empty((await AsAsync(factory, plainMember)).Items);

        // Grant the read and the same person sees it — so the emptiness above was the gate doing
        // its job, not the seed being wrong.
        await TestSeeds.GrantAsync(factory, org.Id, plainMember,
            OrganizationSecurityTable.Case, OrganizationSecurityAction.Read);

        Assert.Contains((await AsAsync(factory, plainMember)).Items, i => i.CaseId == privateCase.Id);
    }

    [Fact]
    public async Task A_proposed_case_is_nobodys_pin()
    {
        var factory = TestDbFactory.Create();
        var org = MakeOrg();
        var memberId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Organizations.Add(org);
            db.Cases.Add(MakeCase(org.Id, "Only proposed", 36.16m, -86.78m,
                                  isPublic: false, status: CaseStatus.Proposed));
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = org.Id, AppUserId = memberId,
                Role = OrganizationMemberRole.Owner, IsActive = true, DateCreated = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        Assert.Empty((await AsAsync(factory, memberId)).Items);
    }
}
