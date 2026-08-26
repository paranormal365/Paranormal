using AutoMapper;
using Ben.Data.Common.Interfaces;
using Ben.Data.WebApi.Controllers;
using Ben.Data.WebApi.Services;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Data.WebApi.Services.Places;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Item 184 Phase C: the moments a case BECOMES private-lane work are gated by
/// <see cref="TierCapability.PrivateResidenceCases"/> — accepting a client request, binding a
/// residence place, receiving a private case, publishing one — and a case already designated is
/// never re-gated (the grandfather rule, pinned here).
/// </summary>
/// <remarks>
/// Probe-regressed as a set: with the gate wiring stashed, every refusal test here fails by
/// succeeding — the request goes through — and the pass/grandfather pins hold either way.
/// </remarks>
public class PrivateCaseGateTests
{
    // ── Harness ──────────────────────────────────────────────────────────────

    private sealed class SimpleFactory(DbContextOptions<BenDataContext> options) : IDbContextFactory<BenDataContext>
    {
        public BenDataContext CreateDbContext() => new(options);
        public Task<BenDataContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(new BenDataContext(options));
    }

    private static IDbContextFactory<BenDataContext> CreateFactory() =>
        new SimpleFactory(new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    /// <summary>Puts the org on a tier whose plan excludes private-residence cases.</summary>
    private static async Task ExcludePrivateCasesAsync(
        IDbContextFactory<BenDataContext> factory, Guid orgId, string tierName = "Free")
    {
        await using var db = await factory.CreateDbContextAsync();
        var tierId = Guid.NewGuid();
        var creator = Guid.NewGuid();
        db.SubscriptionTiers.Add(new SubscriptionTier
        {
            Id = tierId, Name = tierName, MinMembers = 1, SortOrder = 1, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = creator,
        });
        db.SubscriptionTierExcludedCapabilities.Add(new SubscriptionTierExcludedCapability
        {
            SubscriptionTierId = tierId, Capability = TierCapability.PrivateResidenceCases,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = creator,
        });
        db.OrganizationSubscriptions.Add(new OrganizationSubscription
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, SubscriptionTierId = tierId,
            Status = SubscriptionStatus.Free, DateCreated = DateTime.UtcNow, CreatedByAppUserId = creator,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<(IDbContextFactory<BenDataContext> Factory, Guid OrgId, Guid UserId)> SeedOrgAsync()
    {
        var factory = CreateFactory();
        Guid orgId = Guid.NewGuid(), userId = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        db.Users.Add(new AppUser { Id = userId, UserName = "u@t.com", NormalizedUserName = "U@T.COM", Email = "u@t.com", NormalizedEmail = "U@T.COM", DateCreated = DateTime.UtcNow });
        db.Organizations.Add(new Organization { Id = orgId, Name = "Gate Org", UrlName = "gate-org", DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = userId,
            Role = OrganizationMemberRole.Owner, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        await db.SaveChangesAsync();
        await TestSeeds.BridgeAsync(factory, orgId);
        return (factory, orgId, userId);
    }

    private static async Task<Guid> SeedCaseAsync(
        IDbContextFactory<BenDataContext> factory, Guid orgId, Guid userId,
        bool isPrivate = false, bool isPublic = false, CaseStatus status = CaseStatus.Active)
    {
        await using var db = await factory.CreateDbContextAsync();
        var caseId = Guid.NewGuid();
        db.Cases.Add(new Case
        {
            Id = caseId, OrganizationId = orgId, Title = "Gate Case",
            CaseYear = 2026, OrgCaseNumber = 7, Status = status, IsPublic = isPublic,
            IsPrivateEngagement = isPrivate,
            StreetAddress1 = "1 Main", City = "Nashville", State = "TN", ZipCode = "37201", Country = "US",
            DateCaseOpened = DateTime.UtcNow, DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        await db.SaveChangesAsync();
        return caseId;
    }

    private static async Task<Guid> SeedPlaceAsync(
        IDbContextFactory<BenDataContext> factory, PlaceKind kind)
    {
        await using var db = await factory.CreateDbContextAsync();
        var placeId = Guid.NewGuid();
        db.Places.Add(new Place
        {
            Id = placeId, Name = kind == PlaceKind.PrivateResidence ? "A Home" : "A Landmark",
            City = "Nashville", State = "TN", Kind = kind,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.NewGuid(),
        });
        await db.SaveChangesAsync();
        return placeId;
    }

    private static CaseController BuildCaseController(IDbContextFactory<BenDataContext> factory, Guid userId)
    {
        var ctrl = new CaseController(factory, Mock.Of<IMapper>(),
            new Ben.Data.WebApi.Services.Billing.SubscriptionLimitGuard(factory),
            new OrganizationSecurityService(factory),
            new Ben.Data.WebApi.Services.RequestReviewNotifier(factory, new Ben.Data.WebApi.Services.PlatformMessageService(factory)));
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Bearer")),
            },
        };
        return ctrl;
    }

    // ── The placement door (shared by all three placement call sites) ────────

    [Fact]
    public async Task Binding_a_residence_to_an_undesignated_case_is_refused_naming_the_tier()
    {
        var (factory, orgId, userId) = await SeedOrgAsync();
        await ExcludePrivateCasesAsync(factory, orgId, "Free");
        var caseId = await SeedCaseAsync(factory, orgId, userId);
        var placeId = await SeedPlaceAsync(factory, PlaceKind.PrivateResidence);

        await using var db = await factory.CreateDbContextAsync();
        var investigation = new Investigation { Id = Guid.NewGuid(), OrganizationId = orgId, CaseId = caseId };
        var result = await InvestigationPlacement.ApplyAsync(db, investigation, placeId, null, userId, default);

        Assert.NotNull(result.Error);
        Assert.Contains("private-residence", result.Error);
        Assert.Contains("(Free)", result.Error);

        await using var check = await factory.CreateDbContextAsync();
        Assert.False((await check.Cases.SingleAsync(c => c.Id == caseId)).IsPrivateEngagement,
            "a refused binding must not half-apply the designation");
    }

    [Fact]
    public async Task Binding_a_residence_designates_the_case_when_the_plan_allows()
    {
        var (factory, orgId, userId) = await SeedOrgAsync();   // no tier rows: fail-open, included
        var caseId = await SeedCaseAsync(factory, orgId, userId);
        var placeId = await SeedPlaceAsync(factory, PlaceKind.PrivateResidence);

        await using var db = await factory.CreateDbContextAsync();
        var investigation = new Investigation { Id = Guid.NewGuid(), OrganizationId = orgId, CaseId = caseId };
        var result = await InvestigationPlacement.ApplyAsync(db, investigation, placeId, null, userId, default);
        Assert.Null(result.Error);
        await db.SaveChangesAsync();

        await using var check = await factory.CreateDbContextAsync();
        Assert.True((await check.Cases.SingleAsync(c => c.Id == caseId)).IsPrivateEngagement,
            "designation setter b: a residence binding marks the case private-lane");
    }

    [Fact]
    public async Task An_already_designated_case_is_never_regated_at_placement()
    {
        // The grandfather pin: pre-gate residence cases held by free groups keep working.
        var (factory, orgId, userId) = await SeedOrgAsync();
        await ExcludePrivateCasesAsync(factory, orgId);
        var caseId = await SeedCaseAsync(factory, orgId, userId, isPrivate: true);
        var placeId = await SeedPlaceAsync(factory, PlaceKind.PrivateResidence);

        await using var db = await factory.CreateDbContextAsync();
        var investigation = new Investigation { Id = Guid.NewGuid(), OrganizationId = orgId, CaseId = caseId };
        var result = await InvestigationPlacement.ApplyAsync(db, investigation, placeId, null, userId, default);

        Assert.Null(result.Error);
        Assert.Equal(placeId, investigation.PlaceId);
    }

    [Fact]
    public async Task A_public_location_binding_needs_no_plan()
    {
        var (factory, orgId, userId) = await SeedOrgAsync();
        await ExcludePrivateCasesAsync(factory, orgId);
        var caseId = await SeedCaseAsync(factory, orgId, userId);
        var placeId = await SeedPlaceAsync(factory, PlaceKind.PublicLocation);

        await using var db = await factory.CreateDbContextAsync();
        var investigation = new Investigation { Id = Guid.NewGuid(), OrganizationId = orgId, CaseId = caseId };
        var result = await InvestigationPlacement.ApplyAsync(db, investigation, placeId, null, userId, default);

        Assert.Null(result.Error);
        await using var check = await factory.CreateDbContextAsync();
        Assert.False((await check.Cases.SingleAsync(c => c.Id == caseId)).IsPrivateEngagement,
            "public-place work stays free-lane");
    }

    // ── Accepting a client request ───────────────────────────────────────────

    private static async Task<Guid> SeedClientRequestAsync(
        IDbContextFactory<BenDataContext> factory, Guid orgId)
    {
        await using var db = await factory.CreateDbContextAsync();
        Guid clientId = Guid.NewGuid(), requestId = Guid.NewGuid();
        db.Users.Add(new AppUser { Id = clientId, UserName = "cl@t.com", NormalizedUserName = "CL@T.COM", Email = "cl@t.com", NormalizedEmail = "CL@T.COM", DisplayName = "Daniel Park", DateCreated = DateTime.UtcNow });
        db.ClientRequests.Add(new ClientRequest
        {
            Id = requestId, AppUserId = clientId, Status = ClientRequestStatus.Submitted,
            StreetAddress1 = "1 Elm", City = "Nashville", State = "TN", ZipCode = "37201",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = clientId,
        });
        db.ClientRequestOrganizations.Add(new ClientRequestOrganization
        {
            Id = Guid.NewGuid(), ClientRequestId = requestId, OrganizationId = orgId,
            Status = ClientOrgRequestStatus.Pending,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = clientId,
        });
        await db.SaveChangesAsync();
        return requestId;
    }

    [Fact]
    public async Task Accepting_a_client_request_is_refused_without_the_plan()
    {
        var (factory, orgId, userId) = await SeedOrgAsync();
        await ExcludePrivateCasesAsync(factory, orgId, "Free");
        var requestId = await SeedClientRequestAsync(factory, orgId);

        var result = await BuildCaseController(factory, userId).AcceptClientRequest(
            orgId, requestId, new AcceptClientRequestAsCaseRequest(null, null), default);

        var refusal = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("private-residence", refusal.Value!.ToString());
        Assert.Contains("(Free)", refusal.Value!.ToString());

        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(0, await db.Cases.CountAsync());
        Assert.Equal(ClientOrgRequestStatus.Pending,
            (await db.ClientRequestOrganizations.SingleAsync()).Status);
    }

    [Fact]
    public async Task Accepting_a_client_request_creates_a_case_born_private()
    {
        var (factory, orgId, userId) = await SeedOrgAsync();   // fail-open: included
        var requestId = await SeedClientRequestAsync(factory, orgId);

        var result = await BuildCaseController(factory, userId).AcceptClientRequest(
            orgId, requestId, new AcceptClientRequestAsCaseRequest(null, null), default);

        Assert.IsType<CreatedAtActionResult>(result.Result);
        await using var db = await factory.CreateDbContextAsync();
        Assert.True((await db.Cases.SingleAsync()).IsPrivateEngagement,
            "designation setter a: a case born from a client request is private-lane");
    }

    // ── Publishing and manual designation (CaseController.Update) ────────────

    [Fact]
    public async Task Making_a_private_case_public_is_refused_without_the_plan()
    {
        var (factory, orgId, userId) = await SeedOrgAsync();
        await ExcludePrivateCasesAsync(factory, orgId, "Free");
        var caseId = await SeedCaseAsync(factory, orgId, userId, isPrivate: true, isPublic: false);

        var result = await BuildCaseController(factory, userId).Update(orgId, caseId,
            new UpdateCaseRequest("Gate Case", null, CaseStatus.Public, null, IsPublic: true, null), default);

        var refusal = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("private-residence", refusal.Value!.ToString());

        await using var db = await factory.CreateDbContextAsync();
        Assert.False((await db.Cases.SingleAsync(c => c.Id == caseId)).IsPublic);
    }

    [Fact]
    public async Task An_already_public_private_case_can_still_be_edited()
    {
        // Grandfathered publication: the gate fires on the FLIP to public, never on a case that
        // is already out there — editing it must not be held hostage.
        var (factory, orgId, userId) = await SeedOrgAsync();
        await ExcludePrivateCasesAsync(factory, orgId);
        var caseId = await SeedCaseAsync(factory, orgId, userId, isPrivate: true, isPublic: true, CaseStatus.Public);

        var result = await BuildCaseController(factory, userId).Update(orgId, caseId,
            new UpdateCaseRequest("Renamed", null, CaseStatus.Public, null, IsPublic: true, null), default);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task Manually_designating_a_case_private_needs_the_plan_and_clearing_is_free()
    {
        var (factory, orgId, userId) = await SeedOrgAsync();
        await ExcludePrivateCasesAsync(factory, orgId, "Free");
        var caseId = await SeedCaseAsync(factory, orgId, userId, isPrivate: false);

        var setResult = await BuildCaseController(factory, userId).Update(orgId, caseId,
            new UpdateCaseRequest("Gate Case", null, CaseStatus.Active, null, IsPublic: false, null,
                IsPrivateEngagement: true), default);
        Assert.IsType<BadRequestObjectResult>(setResult.Result);

        var privateCaseId = await SeedCaseAsync(factory, orgId, userId, isPrivate: true);
        var clearResult = await BuildCaseController(factory, userId).Update(orgId, privateCaseId,
            new UpdateCaseRequest("Gate Case", null, CaseStatus.Active, null, IsPublic: false, null,
                IsPrivateEngagement: false), default);
        Assert.IsType<OkObjectResult>(clearResult.Result);

        await using var db = await factory.CreateDbContextAsync();
        Assert.False((await db.Cases.SingleAsync(c => c.Id == privateCaseId)).IsPrivateEngagement);
    }

    [Fact]
    public async Task An_update_that_does_not_touch_the_designation_leaves_it_alone()
    {
        // Every pre-184 caller sends no IsPrivateEngagement at all; null must mean "unchanged"
        // or an old client clears designations as a side effect of renaming a case.
        var (factory, orgId, userId) = await SeedOrgAsync();
        var caseId = await SeedCaseAsync(factory, orgId, userId, isPrivate: true);

        var result = await BuildCaseController(factory, userId).Update(orgId, caseId,
            new UpdateCaseRequest("Renamed", null, CaseStatus.Active, null, IsPublic: false, null), default);
        Assert.IsType<OkObjectResult>(result.Result);

        await using var db = await factory.CreateDbContextAsync();
        Assert.True((await db.Cases.SingleAsync(c => c.Id == caseId)).IsPrivateEngagement);
    }

    [Fact]
    public async Task Republishing_consumes_the_lapse_memory()
    {
        // Phase D: the banner's one click flips IsPublic back on; the memory that showed the
        // banner must clear with it, or the banner survives its own purpose.
        var (factory, orgId, userId) = await SeedOrgAsync();
        Guid caseId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            caseId = Guid.NewGuid();
            db.Cases.Add(new Case
            {
                Id = caseId, OrganizationId = orgId, Title = "Lapsed Case",
                CaseYear = 2026, OrgCaseNumber = 8, Status = CaseStatus.Public,
                IsPublic = false, IsPrivateEngagement = true, WasPublicBeforeLapse = true,
                UrlName = "lapsed-case",
                StreetAddress1 = "1 Main", City = "N", State = "TN", ZipCode = "1", Country = "US",
                DateCaseOpened = DateTime.UtcNow, DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
            });
            await db.SaveChangesAsync();
        }

        var result = await BuildCaseController(factory, userId).Update(orgId, caseId,
            new UpdateCaseRequest("Lapsed Case", null, CaseStatus.Public, null, IsPublic: true, null), default);
        Assert.IsType<OkObjectResult>(result.Result);

        await using (var check = await factory.CreateDbContextAsync())
        {
            var c = await check.Cases.SingleAsync(x => x.Id == caseId);
            Assert.True(c.IsPublic);
            Assert.Null(c.WasPublicBeforeLapse);
        }
    }

    // ── Receiving a private case (transfer accept + the client's pick) ───────

    private static CaseTransferController BuildTransferController(
        IDbContextFactory<BenDataContext> factory, Guid userId)
    {
        var ctrl = new CaseTransferController(factory, Mock.Of<IMapper>(),
            new PlatformMessageService(factory),
            new OrganizationSecurityService(factory));
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Bearer")),
            },
        };
        return ctrl;
    }

    [Fact]
    public async Task Accepting_a_transferred_private_case_is_refused_without_the_plan()
    {
        var (factory, fromOrgId, fromUserId) = await SeedOrgAsync();
        var (_, _, _) = (factory, fromOrgId, fromUserId);
        var caseId = await SeedCaseAsync(factory, fromOrgId, fromUserId, isPrivate: true);

        // The receiving org: may transfer cases (no CaseTransfers exclusion), but its plan
        // excludes private-residence work — precisely the combination this gate exists for.
        Guid toOrgId = Guid.NewGuid(), toUserId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Users.Add(new AppUser { Id = toUserId, UserName = "r@t.com", NormalizedUserName = "R@T.COM", Email = "r@t.com", NormalizedEmail = "R@T.COM", DateCreated = DateTime.UtcNow });
            db.Organizations.Add(new Organization { Id = toOrgId, Name = "Receiver", UrlName = "receiver", DateCreated = DateTime.UtcNow, CreatedByAppUserId = toUserId });
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = toOrgId, AppUserId = toUserId,
                Role = OrganizationMemberRole.Owner, IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = toUserId,
            });
            db.CaseTransferLogs.Add(new CaseTransferLog
            {
                Id = Guid.NewGuid(), CaseId = caseId,
                FromOrganizationId = fromOrgId, ToOrganizationId = toOrgId,
                ProposedByAppUserId = fromUserId, Status = CaseTransferStatus.Pending,
                DateProposed = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        await TestSeeds.BridgeAsync(factory, toOrgId);
        await ExcludePrivateCasesAsync(factory, toOrgId, "Free");

        Guid logId;
        await using (var db = await factory.CreateDbContextAsync())
            logId = (await db.CaseTransferLogs.SingleAsync()).Id;

        var result = await BuildTransferController(factory, toUserId)
            .Respond(toOrgId, caseId, logId, new RespondTransferRequest(true, null), default);

        var refusal = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("private-residence", refusal.Value!.ToString());

        await using (var db = await factory.CreateDbContextAsync())
        {
            Assert.Equal(CaseTransferStatus.Pending, (await db.CaseTransferLogs.SingleAsync()).Status);
            Assert.Equal(fromOrgId, (await db.Cases.SingleAsync(c => c.Id == caseId)).OrganizationId);
        }
    }

    [Fact]
    public async Task A_client_picking_an_excluded_group_for_their_private_case_is_told_at_pick_time()
    {
        var (factory, orgId, userId) = await SeedOrgAsync();

        // The client's paused private case, owned by them via its originating request.
        Guid clientId = Guid.NewGuid(), caseId = Guid.NewGuid(), destOrgId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var requestId = Guid.NewGuid();
            db.Users.Add(new AppUser { Id = clientId, UserName = "pk@t.com", NormalizedUserName = "PK@T.COM", Email = "pk@t.com", NormalizedEmail = "PK@T.COM", DateCreated = DateTime.UtcNow });
            db.ClientRequests.Add(new ClientRequest
            {
                Id = requestId, AppUserId = clientId, Status = ClientRequestStatus.Assigned,
                StreetAddress1 = "1 Elm", City = "N", State = "TN", ZipCode = "1",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = clientId,
            });
            db.Cases.Add(new Case
            {
                Id = caseId, OrganizationId = orgId, ClientRequestId = requestId,
                Title = "Client Case", CaseYear = 2026, OrgCaseNumber = 9,
                Status = CaseStatus.Paused, IsPrivateEngagement = true,
                StreetAddress1 = "1 Elm", City = "N", State = "TN", ZipCode = "1", Country = "US",
                DateCaseOpened = DateTime.UtcNow, DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
            });
            db.Organizations.Add(new Organization { Id = destOrgId, Name = "Dest", UrlName = "dest", DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId });
            await db.SaveChangesAsync();
        }
        await ExcludePrivateCasesAsync(factory, destOrgId, "Free");

        var storage = new Mock<IFileStorageService>();
        var email = new Mock<IEmailService>();
        email.Setup(e => e.IsConfigured).Returns(false);
        var ctrl = new MyCaseController(
            factory, Mock.Of<IMapper>(), storage.Object,
            new FileMetadataExtractorService(),
            Mock.Of<IAuditLogService>(),
            email.Object, new ConfigurationBuilder().Build(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<MyCaseController>.Instance,
            Microsoft.Extensions.Options.Options.Create(new Ben.Data.Common.SiteIdentity()),
            new PlatformMessageService(factory),
            Ben.Web.Tests.TestMedia.Ingest());
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, clientId.ToString())], "Bearer")),
            },
        };

        var result = await ctrl.ReassignCase(caseId,
            new MyCaseController.ReassignCaseRequest(destOrgId, false, false, null), default);

        var refusal = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("private-residence", refusal.Value!.ToString());
        Assert.Contains("Pick a different group", refusal.Value!.ToString());

        await using (var db = await factory.CreateDbContextAsync())
            Assert.Equal(0, await db.CaseTransferLogs.CountAsync());
    }
}
