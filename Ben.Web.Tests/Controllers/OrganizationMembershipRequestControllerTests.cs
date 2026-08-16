using AutoMapper;
using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Tests for OrganizationMembershipRequestController — apply, respond, withdraw, and vote lifecycle.
/// </summary>
public class OrganizationMembershipRequestControllerTests
{
    // Non-pooled: Apply/Respond use FirstAsync with required Includes (Organization, Applicant)
    private sealed class SimpleFactory(DbContextOptions<BenDataContext> options) : IDbContextFactory<BenDataContext>
    {
        public BenDataContext CreateDbContext() => new(options);
        public Task<BenDataContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(new BenDataContext(options));
    }

    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var opts = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SimpleFactory(opts);
    }

    private static IMapper CreateMapper()
    {
        var m = new Mock<IMapper>();
        m.Setup(x => x.Map<OrganizationMembershipRequestRecord>(It.IsAny<object>()))
            .Returns<object>(o => o is OrganizationMembershipRequest r
                ? new OrganizationMembershipRequestRecord { Id = r.Id, OrganizationId = r.OrganizationId, OrganizationName = "", AppUserId = r.AppUserId, ApplicantDisplayName = "", ApplicantEmail = "", Status = r.Status, RequestMessage = r.RequestMessage, DateCreated = r.DateCreated }
                : new OrganizationMembershipRequestRecord { OrganizationName = "", ApplicantDisplayName = "", ApplicantEmail = "", DateCreated = DateTime.UtcNow });
        m.Setup(x => x.Map<List<OrganizationMembershipRequestRecord>>(It.IsAny<object>()))
            .Returns<object>(o => o is IEnumerable<OrganizationMembershipRequest> list
                ? list.Select(r => new OrganizationMembershipRequestRecord { Id = r.Id, OrganizationId = r.OrganizationId, OrganizationName = "", AppUserId = r.AppUserId, ApplicantDisplayName = "", ApplicantEmail = "", Status = r.Status, DateCreated = r.DateCreated }).ToList()
                : []);
        return m.Object;
    }

    private static OrganizationMembershipRequestController Build(
        IDbContextFactory<BenDataContext> factory,
        Guid userId,
        bool hasPermission = true,
        bool isSuperAdmin = false)
    {
        var security = new Mock<IOrganizationSecurityService>();
        security.Setup(s => s.HasAccessAsync(It.IsAny<Guid>(), It.IsAny<Guid>(),
            It.IsAny<OrganizationSecurityTable>(), It.IsAny<OrganizationSecurityAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(hasPermission);

        var audit = new Mock<IAuditLogService>();
        audit.Setup(a => a.LogCreateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<object>(), It.IsAny<Guid>(), It.IsAny<string>()))
             .Returns(Task.CompletedTask);
        audit.Setup(a => a.LogUpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<object>(), It.IsAny<object>(), It.IsAny<Guid>(), It.IsAny<string>()))
             .Returns(Task.CompletedTask);

        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
        if (isSuperAdmin) claims.Add(new Claim(ClaimTypes.Role, RoleNames.SuperAdmin));

        var ctrl = new OrganizationMembershipRequestController(factory, CreateMapper(), security.Object, audit.Object);
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"))
            }
        };
        return ctrl;
    }

    private static async Task<(IDbContextFactory<BenDataContext>, Guid orgId, Guid applicantId, Guid adminId)> SeedAsync(bool acceptingApps = true)
    {
        var factory     = CreateFactory();
        var orgId       = Guid.NewGuid();
        var applicantId = Guid.NewGuid();
        var adminId     = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();
        db.Users.Add(new AppUser { Id = applicantId, UserName = "app@t.com", NormalizedUserName = "APP@T.COM", Email = "app@t.com", NormalizedEmail = "APP@T.COM", DateCreated = DateTime.UtcNow });
        db.Users.Add(new AppUser { Id = adminId,     UserName = "adm@t.com", NormalizedUserName = "ADM@T.COM", Email = "adm@t.com", NormalizedEmail = "ADM@T.COM", DateCreated = DateTime.UtcNow });
        db.Organizations.Add(new Organization { Id = orgId, Name = "Test Org", UrlName = "test", IsAcceptingApplications = acceptingApps, DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership { Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = adminId, Role = OrganizationMemberRole.Owner, IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId });
        // Seed a UserMessageType so the Respond notification doesn't fail
        db.UserMessageTypes.Add(new UserMessageType { Id = new Guid("00000000-0000-0000-0000-000000000001"), Name = "Org Membership Response", DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId });
        await db.SaveChangesAsync();
        return (factory, orgId, applicantId, adminId);
    }

    // ── Apply ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Apply_CreatesRequest()
    {
        var (factory, orgId, applicantId, _) = await SeedAsync();
        var ctrl   = Build(factory, applicantId);
        var result = await ctrl.Apply(orgId, new ApplyForMembershipRequest("Please let me join"), default);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var dto     = Assert.IsType<OrganizationMembershipRequestRecord>(created.Value);
        Assert.Equal(OrganizationMembershipRequestStatus.Pending, dto.Status);
        Assert.Equal(applicantId, dto.AppUserId);
    }

    [Fact]
    public async Task Apply_NotAcceptingApplications_ReturnsBadRequest()
    {
        var (factory, orgId, applicantId, _) = await SeedAsync(acceptingApps: false);
        var ctrl   = Build(factory, applicantId);
        var result = await ctrl.Apply(orgId, new ApplyForMembershipRequest(null), default);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Apply_DuplicatePending_ReturnsConflict()
    {
        var (factory, orgId, applicantId, _) = await SeedAsync();
        var ctrl = Build(factory, applicantId);
        await ctrl.Apply(orgId, new ApplyForMembershipRequest(null), default);
        Assert.IsType<ConflictObjectResult>((await ctrl.Apply(orgId, new ApplyForMembershipRequest(null), default)).Result);
    }

    [Fact]
    public async Task Apply_AlreadyMember_ReturnsConflict()
    {
        var (factory, orgId, applicantId, adminId) = await SeedAsync();
        await using var db = await factory.CreateDbContextAsync();
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership { Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = applicantId, Role = OrganizationMemberRole.Member, IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId });
        await db.SaveChangesAsync();

        var ctrl = Build(factory, applicantId);
        Assert.IsType<ConflictObjectResult>((await ctrl.Apply(orgId, new ApplyForMembershipRequest(null), default)).Result);
    }

    [Fact]
    public async Task Apply_ConcurrentApplications_OnlyOneSucceeds()
    {
        // Regression for the check-then-insert race: two concurrent Apply calls for the same
        // (org, user) used to both be able to pass the AnyAsync "no pending request" check before
        // either inserted, so the loser hit an unhandled DbUpdateException (raw 500) once the
        // filtered unique index was in place. The fix catches that and returns the same Conflict
        // the pre-check already returns for the non-racing case.
        var (factory, orgId, applicantId, _) = await SeedAsync();
        var ctrl1 = Build(factory, applicantId);
        var ctrl2 = Build(factory, applicantId);

        var results = await Task.WhenAll(
            ctrl1.Apply(orgId, new ApplyForMembershipRequest("First"), default),
            ctrl2.Apply(orgId, new ApplyForMembershipRequest("Second"), default));

        Assert.All(results, r => Assert.True(r.Result is CreatedAtActionResult or ConflictObjectResult));

        await using var verify = await factory.CreateDbContextAsync();
        var pending = await verify.OrganizationMembershipRequests
            .Where(r => r.OrganizationId == orgId && r.AppUserId == applicantId
                     && r.Status == OrganizationMembershipRequestStatus.Pending)
            .ToListAsync();
        Assert.Single(pending);
    }

    // ── Respond (accept) ──────────────────────────────────────────────────────

    [Fact]
    public async Task Respond_Accept_AddsMembershipAndUpdatesStatus()
    {
        var (factory, orgId, applicantId, adminId) = await SeedAsync();
        var applicant = Build(factory, applicantId);
        var reqId = ((OrganizationMembershipRequestRecord)((CreatedAtActionResult)(await applicant.Apply(orgId, new ApplyForMembershipRequest(null), default)).Result!).Value!).Id;

        var admin  = Build(factory, adminId, hasPermission: true);
        var result = await admin.Respond(orgId, reqId, new RespondToMembershipRequest(OrganizationMembershipRequestStatus.Accepted, null), default);

        Assert.IsType<OkObjectResult>(result.Result);

        await using var db = await factory.CreateDbContextAsync();
        Assert.True(await db.OrganizationUserMemberships.AnyAsync(m => m.OrganizationId == orgId && m.AppUserId == applicantId && m.IsActive));
        var req = await db.OrganizationMembershipRequests.FindAsync(reqId);
        Assert.Equal(OrganizationMembershipRequestStatus.Accepted, req!.Status);
    }

    [Fact]
    public async Task Respond_Deny_UpdatesStatusWithReason()
    {
        var (factory, orgId, applicantId, adminId) = await SeedAsync();
        var applicant = Build(factory, applicantId);
        var reqId = ((OrganizationMembershipRequestRecord)((CreatedAtActionResult)(await applicant.Apply(orgId, new ApplyForMembershipRequest(null), default)).Result!).Value!).Id;

        var admin  = Build(factory, adminId, hasPermission: true);
        var result = await admin.Respond(orgId, reqId, new RespondToMembershipRequest(OrganizationMembershipRequestStatus.Denied, "Not a fit", false, "Capacity issues"), default);

        Assert.IsType<OkObjectResult>(result.Result);
        await using var db = await factory.CreateDbContextAsync();
        var req = await db.OrganizationMembershipRequests.FindAsync(reqId);
        Assert.Equal(OrganizationMembershipRequestStatus.Denied, req!.Status);
        Assert.Equal("Capacity issues", req.DenialReason);
    }

    [Fact]
    public async Task Respond_AlreadyResponded_ReturnsConflict()
    {
        var (factory, orgId, applicantId, adminId) = await SeedAsync();
        var applicant = Build(factory, applicantId);
        var reqId = ((OrganizationMembershipRequestRecord)((CreatedAtActionResult)(await applicant.Apply(orgId, new ApplyForMembershipRequest(null), default)).Result!).Value!).Id;

        var admin = Build(factory, adminId, hasPermission: true);
        await admin.Respond(orgId, reqId, new RespondToMembershipRequest(OrganizationMembershipRequestStatus.Accepted, null), default);
        Assert.IsType<ConflictObjectResult>((await admin.Respond(orgId, reqId, new RespondToMembershipRequest(OrganizationMembershipRequestStatus.Denied, null), default)).Result);
    }

    // ── Withdraw ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Withdraw_Applicant_SetsWithdrawnStatus()
    {
        var (factory, orgId, applicantId, _) = await SeedAsync();
        var ctrl  = Build(factory, applicantId);
        var reqId = ((OrganizationMembershipRequestRecord)((CreatedAtActionResult)(await ctrl.Apply(orgId, new ApplyForMembershipRequest(null), default)).Result!).Value!).Id;

        Assert.IsType<NoContentResult>(await ctrl.Withdraw(orgId, reqId, default));
        await using var db = await factory.CreateDbContextAsync();
        var req = await db.OrganizationMembershipRequests.FindAsync(reqId);
        Assert.Equal(OrganizationMembershipRequestStatus.Withdrawn, req!.Status);
    }

    [Fact]
    public async Task Withdraw_OtherUser_ReturnsForbid()
    {
        var (factory, orgId, applicantId, _) = await SeedAsync();
        var applicant = Build(factory, applicantId);
        var reqId = ((OrganizationMembershipRequestRecord)((CreatedAtActionResult)(await applicant.Apply(orgId, new ApplyForMembershipRequest(null), default)).Result!).Value!).Id;

        var other = Build(factory, Guid.NewGuid());
        Assert.IsType<ForbidResult>(await other.Withdraw(orgId, reqId, default));
    }

    [Fact]
    public async Task Withdraw_AlreadyAccepted_ReturnsConflict()
    {
        var (factory, orgId, applicantId, adminId) = await SeedAsync();
        var applicant = Build(factory, applicantId);
        var reqId = ((OrganizationMembershipRequestRecord)((CreatedAtActionResult)(await applicant.Apply(orgId, new ApplyForMembershipRequest(null), default)).Result!).Value!).Id;

        var admin = Build(factory, adminId, hasPermission: true);
        await admin.Respond(orgId, reqId, new RespondToMembershipRequest(OrganizationMembershipRequestStatus.Accepted, null), default);

        Assert.IsType<ConflictObjectResult>(await applicant.Withdraw(orgId, reqId, default));
    }

    // ── GetVotes cross-org chain (Phase B) ───────────────────────────────────

    [Fact]
    public async Task GetVotes_RequestBelongsToDifferentOrg_ReturnsNotFound()
    {
        // The core of the fix: GetVotes checked the caller's HasAccessAsync permission for the
        // route orgId, but never that the requestId (id) actually belonged to that org — an
        // admin with real MembershipRequests-Update permission in THEIR OWN org could read the
        // vote list for any other org's application just by knowing/guessing its id.
        var (factory, orgId, applicantId, _) = await SeedAsync();
        var applicant = Build(factory, applicantId);
        var reqId = ((OrganizationMembershipRequestRecord)((CreatedAtActionResult)(await applicant.Apply(orgId, new ApplyForMembershipRequest(null), default)).Result!).Value!).Id;

        var otherOrgId = Guid.NewGuid();
        var attacker = Build(factory, Guid.NewGuid(), hasPermission: true); // has real permission in otherOrgId

        var result = await attacker.GetVotes(otherOrgId, reqId, default);

        Assert.IsType<NotFoundResult>(result.Result);
    }
}
