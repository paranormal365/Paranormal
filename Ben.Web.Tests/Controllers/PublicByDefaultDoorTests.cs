using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// The 2026-09-17 rule as the doors actually apply it: an account that pays nothing is public by
/// default at a public place, and a paying one chooses.
/// </summary>
/// <remarks>
/// <para><b>Why a fixture of its own, at the controller level.</b>
/// <c>InvestigationVisibilityTests</c> covers the predicate, and a predicate that is right is worth
/// nothing if a door forgets to ask it. The rule this replaces was asked by
/// <see cref="InvestigationController"/> and <b>not</b> by
/// <see cref="OrgInvestigationsController"/>, so the same visit was refused or allowed depending on
/// which screen booked it — for a year, with tests passing, because every test went through one
/// door. So the load-bearing tests here are the paired ones: each asserts the same thing through
/// both doors, and neither can be made to pass by fixing one controller.</para>
///
/// <para>InMemory is enough: nothing here deletes, and the subscription lookup is a plain Any().</para>
/// </remarks>
public sealed class PublicByDefaultDoorTests
{
    private const string Landmark = "Cragfont";

    private static IDbContextFactory<BenDataContext> CreateFactory()
        => new PooledDbContextFactory<BenDataContext>(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static IMapper Mapper()
    {
        var m = new Mock<IMapper>();
        m.Setup(x => x.Map<InvestigationRecord>(It.IsAny<object>()))
            .Returns<object>(o => o is Investigation i
                ? new InvestigationRecord
                {
                    Id = i.Id, CaseId = i.CaseId, Title = i.Title, Status = i.Status,
                    Visibility = i.Visibility, ScheduledDateTime = i.ScheduledDateTime,
                    CreatedByAppUserId = i.CreatedByAppUserId,
                }
                : new InvestigationRecord
                {
                    Title = "", ScheduledDateTime = DateTime.UtcNow, CreatedByAppUserId = Guid.Empty,
                });
        m.Setup(x => x.Map<CaseRecord>(It.IsAny<object>()))
            .Returns<object>(o => o is Case c
                ? new CaseRecord
                {
                    Id = c.Id, OrganizationId = c.OrganizationId, Title = c.Title,
                    Status = c.Status, IsPublic = c.IsPublic,
                    StreetAddress1 = c.StreetAddress1, City = c.City, State = c.State,
                    ZipCode = c.ZipCode, Country = c.Country,
                }
                : new CaseRecord { Title = "" });
        return m.Object;
    }

    private static T AsUser<T>(T controller, Guid userId) where T : ControllerBase
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Bearer")),
            },
        };
        return controller;
    }

    private static InvestigationController CaseNestedDoor(IDbContextFactory<BenDataContext> f, Guid userId)
        => AsUser(new InvestigationController(
            f, Mapper(),
            new Ben.Data.WebApi.Services.Billing.SubscriptionLimitGuard(f),
            new Ben.Service.RepositoryService.Services.OrganizationSecurityService(f),
            TestMailer.Quiet()), userId);

    private static OrgInvestigationsController FlatDoor(IDbContextFactory<BenDataContext> f, Guid userId)
        => AsUser(new OrgInvestigationsController(
            f, Mapper(),
            new Mock<Ben.Service.RepositoryService.GenericInterfaces.IAuditLogService>().Object,
            new Ben.Service.RepositoryService.Services.OrganizationSecurityService(f)), userId);

    private static CaseController Cases(IDbContextFactory<BenDataContext> f, Guid userId)
        => AsUser(new CaseController(
            f, Mapper(),
            new Ben.Data.WebApi.Services.Billing.SubscriptionLimitGuard(f),
            new Ben.Service.RepositoryService.Services.OrganizationSecurityService(f),
            new Ben.Data.WebApi.Services.RequestReviewNotifier(
                f, new Ben.Data.WebApi.Services.PlatformMessageService(f)),
            TestMailer.Quiet(),
            new Ben.Data.WebApi.Services.CmsMarkupSanitizer()), userId);

    private sealed record World(
        IDbContextFactory<BenDataContext> Factory, Guid OrgId, Guid UserId,
        Guid CaseId, Guid LandmarkId, Guid HomeId);

    /// <summary>A group, a case, a landmark and a home. Paying or not, as asked.</summary>
    private static async Task<World> SeedAsync(bool paying)
    {
        var f = CreateFactory();
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var caseId = Guid.NewGuid();
        var landmarkId = Guid.NewGuid();
        var homeId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using (var db = await f.CreateDbContextAsync())
        {
            db.Users.Add(new AppUser
            {
                Id = userId, UserName = "owner@t.com", NormalizedUserName = "OWNER@T.COM",
                Email = "owner@t.com", NormalizedEmail = "OWNER@T.COM", DateCreated = now,
            });
            db.Organizations.Add(new Organization
            {
                Id = orgId, Name = "Test Org", UrlName = "test",
                DateCreated = now, CreatedByAppUserId = userId,
            });
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = userId,
                Role = OrganizationMemberRole.Owner, IsActive = true,
                DateCreated = now, CreatedByAppUserId = userId,
            });
            db.Cases.Add(new Case
            {
                Id = caseId, OrganizationId = orgId, Title = "A case", CaseYear = 2026,
                OrgCaseNumber = 1, StreetAddress1 = "123 Main", City = "Castalian Springs",
                State = "TN", ZipCode = "37031", Country = "US",
                DateCaseOpened = now, DateCreated = now, CreatedByAppUserId = userId,
            });

            // Coordinates on purpose: PlaceGeocoder is asked to trust supplied ones rather than
            // reach for the network in a unit test.
            db.Places.Add(new Place
            {
                Id = landmarkId, Name = Landmark, StreetAddress1 = "200 Cragfont Rd",
                City = "Castalian Springs", State = "TN", ZipCode = "37031", Country = "US",
                Latitude = 36.3839m, Longitude = -86.3281m,
                Kind = PlaceKind.PublicLocation, DateCreated = now, CreatedByAppUserId = userId,
            });
            db.Places.Add(new Place
            {
                Id = homeId, StreetAddress1 = "9 Quiet Lane", City = "Nashville",
                State = "TN", ZipCode = "37201", Country = "US",
                Latitude = 36.15m, Longitude = -86.75m,
                Kind = PlaceKind.PrivateResidence, DateCreated = now, CreatedByAppUserId = userId,
            });

            if (paying)
            {
                db.OrganizationSubscriptions.Add(new OrganizationSubscription
                {
                    Id = Guid.NewGuid(), OrganizationId = orgId,
                    Status = SubscriptionStatus.Active, Interval = BillingInterval.Monthly,
                    DateCreated = now, CreatedByAppUserId = userId,
                });
            }
            await db.SaveChangesAsync();
        }

        await TestSeeds.BridgeAsync(f, orgId);
        return new World(f, orgId, userId, caseId, landmarkId, homeId);
    }

    private static UpsertInvestigationRequest NestedVisit(
        Guid placeId, InvestigationVisibility? visibility = null) => new(
            Title: "A night at " + Landmark,
            Description: null,
            Location: null,
            ScheduledDateTime: DateTime.UtcNow.AddDays(7),
            EndDateTime: DateTime.UtcNow.AddDays(7).AddHours(4),
            Status: InvestigationStatus.Scheduled,
            Notes: null,
            OrgCalendarEventId: null,
            EvidenceDueDate: null,
            PlaceId: placeId,
            NewPlace: null,
            Visibility: visibility);

    private static CreateOrgInvestigationRequest FlatVisit(
        Guid placeId, InvestigationVisibility? visibility = null) => new(
            Title: "A night at " + Landmark,
            ScheduledDateTime: DateTime.UtcNow.AddDays(7),
            EndDateTime: DateTime.UtcNow.AddDays(7).AddHours(4),
            PlaceId: placeId, Visibility: visibility);

    private static async Task<InvestigationVisibility> SavedScopeAsync(World w)
    {
        await using var db = await w.Factory.CreateDbContextAsync();
        return (await db.Investigations.AsNoTracking().OrderByDescending(i => i.DateCreated)
                                       .FirstAsync()).Visibility;
    }

    // ── The default, through both doors ──────────────────────────────────────

    [Fact]
    public async Task An_unpaid_accounts_landmark_visit_is_public_through_the_case_door()
    {
        var w = await SeedAsync(paying: false);

        var result = await CaseNestedDoor(w.Factory, w.UserId)
            .Create(w.OrgId, w.CaseId, NestedVisit(w.LandmarkId), default);

        Assert.IsNotType<BadRequestObjectResult>(result.Result);
        Assert.Equal(InvestigationVisibility.Public, await SavedScopeAsync(w));
    }

    [Fact]
    public async Task An_unpaid_accounts_landmark_visit_is_public_through_the_flat_door()
    {
        var w = await SeedAsync(paying: false);

        var result = await FlatDoor(w.Factory, w.UserId)
            .Create(w.OrgId, FlatVisit(w.LandmarkId), default);

        Assert.IsNotType<BadRequestObjectResult>(result.Result);
        Assert.Equal(InvestigationVisibility.Public, await SavedScopeAsync(w));
    }

    [Fact]
    public async Task A_paying_accounts_landmark_visit_still_starts_with_fellow_investigators()
    {
        foreach (var flat in new[] { false, true })
        {
            var w = await SeedAsync(paying: true);

            if (flat) await FlatDoor(w.Factory, w.UserId).Create(w.OrgId, FlatVisit(w.LandmarkId), default);
            else await CaseNestedDoor(w.Factory, w.UserId).Create(w.OrgId, w.CaseId, NestedVisit(w.LandmarkId), default);

            Assert.Equal(InvestigationVisibility.PlaceInvestigators, await SavedScopeAsync(w));
        }
    }

    // ── The refusal, through both doors ──────────────────────────────────────

    [Theory]
    [InlineData(InvestigationVisibility.GroupOnly)]
    [InlineData(InvestigationVisibility.PlaceInvestigators)]
    public async Task An_unpaid_account_cannot_narrow_a_landmark_visit_at_either_door(
        InvestigationVisibility narrower)
    {
        var nested = await SeedAsync(paying: false);
        var nestedResult = await CaseNestedDoor(nested.Factory, nested.UserId)
            .Create(nested.OrgId, nested.CaseId, NestedVisit(nested.LandmarkId, narrower), default);

        var flat = await SeedAsync(paying: false);
        var flatResult = await FlatDoor(flat.Factory, flat.UserId)
            .Create(flat.OrgId, FlatVisit(flat.LandmarkId, narrower), default);

        foreach (var refused in new[] { nestedResult.Result, flatResult.Result })
        {
            var bad = Assert.IsType<BadRequestObjectResult>(refused);
            var said = Assert.IsType<string>(bad.Value);
            Assert.Contains("shared with everyone", said);
        }
    }

    [Fact]
    public async Task A_paying_account_may_narrow_a_landmark_visit_at_either_door()
    {
        var nested = await SeedAsync(paying: true);
        Assert.IsNotType<BadRequestObjectResult>((await CaseNestedDoor(nested.Factory, nested.UserId)
            .Create(nested.OrgId, nested.CaseId,
                    NestedVisit(nested.LandmarkId, InvestigationVisibility.GroupOnly), default)).Result);
        Assert.Equal(InvestigationVisibility.GroupOnly, await SavedScopeAsync(nested));

        var flat = await SeedAsync(paying: true);
        Assert.IsNotType<BadRequestObjectResult>((await FlatDoor(flat.Factory, flat.UserId)
            .Create(flat.OrgId,
                    FlatVisit(flat.LandmarkId, InvestigationVisibility.GroupOnly), default)).Result);
        Assert.Equal(InvestigationVisibility.GroupOnly, await SavedScopeAsync(flat));
    }

    /// <summary>
    /// A home is the paid lane and the plan rule does not reach it. An unpaid account's visit to a
    /// residence stays with the group, and is not refused for staying there.
    /// </summary>
    [Fact]
    public async Task An_unpaid_accounts_home_visit_is_untouched_at_either_door()
    {
        var nested = await SeedAsync(paying: false);
        Assert.IsNotType<BadRequestObjectResult>((await CaseNestedDoor(nested.Factory, nested.UserId)
            .Create(nested.OrgId, nested.CaseId, NestedVisit(nested.HomeId), default)).Result);
        Assert.Equal(InvestigationVisibility.GroupOnly, await SavedScopeAsync(nested));

        var flat = await SeedAsync(paying: false);
        Assert.IsNotType<BadRequestObjectResult>((await FlatDoor(flat.Factory, flat.UserId)
            .Create(flat.OrgId, FlatVisit(flat.HomeId), default)).Result);
        Assert.Equal(InvestigationVisibility.GroupOnly, await SavedScopeAsync(flat));
    }

    // ── Cases ────────────────────────────────────────────────────────────────

    private static CreateCaseRequest NewCase() => new(
        "A case at " + Landmark, null, "200 Cragfont Rd", null,
        "Castalian Springs", "TN", "37031", "US", null, null);

    [Fact]
    public async Task An_unpaid_accounts_new_case_arrives_public()
    {
        var w = await SeedAsync(paying: false);

        var created = Assert.IsType<CreatedAtActionResult>(
            (await Cases(w.Factory, w.UserId).Create(w.OrgId, NewCase(), default)).Result);

        Assert.True(Assert.IsType<CaseRecord>(created.Value).IsPublic);
    }

    [Fact]
    public async Task A_paying_accounts_new_case_does_not()
    {
        var w = await SeedAsync(paying: true);

        var created = Assert.IsType<CreatedAtActionResult>(
            (await Cases(w.Factory, w.UserId).Create(w.OrgId, NewCase(), default)).Result);

        Assert.False(Assert.IsType<CaseRecord>(created.Value).IsPublic);
    }

    [Fact]
    public async Task An_unpaid_account_cannot_take_its_case_private_again()
    {
        var w = await SeedAsync(paying: false);
        var ctrl = Cases(w.Factory, w.UserId);
        var caseId = Assert.IsType<CaseRecord>(
            Assert.IsType<CreatedAtActionResult>(
                (await ctrl.Create(w.OrgId, NewCase(), default)).Result).Value).Id;

        var refused = await ctrl.Update(w.OrgId, caseId,
            new UpdateCaseRequest("A case at " + Landmark, null, CaseStatus.Accepted, null,
                                  IsPublic: false, null), default);

        var bad = Assert.IsType<BadRequestObjectResult>(refused.Result);
        Assert.Contains("is public", Assert.IsType<string>(bad.Value));
    }

    /// <summary>
    /// Nothing in hand is disturbed. A case that was already private when the rule arrived can be
    /// saved over and over without meeting it — the same grandfathering the member cap uses.
    /// </summary>
    [Fact]
    public async Task A_case_that_was_already_private_stays_saveable()
    {
        var w = await SeedAsync(paying: false);
        var caseId = Guid.NewGuid();
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            db.Cases.Add(new Case
            {
                Id = caseId, OrganizationId = w.OrgId, Title = "Older work", CaseYear = 2025,
                OrgCaseNumber = 9, StreetAddress1 = "1 Old Rd", City = "Nashville",
                State = "TN", ZipCode = "37201", Country = "US", IsPublic = false,
                Status = CaseStatus.Active,
                DateCaseOpened = DateTime.UtcNow.AddYears(-1), DateCreated = DateTime.UtcNow.AddYears(-1),
                CreatedByAppUserId = w.UserId,
            });
            await db.SaveChangesAsync();
        }

        var result = await Cases(w.Factory, w.UserId).Update(w.OrgId, caseId,
            new UpdateCaseRequest("Older work", null, CaseStatus.Active, null,
                                  IsPublic: false, null), default);

        Assert.IsType<OkObjectResult>(result.Result);
    }
}
