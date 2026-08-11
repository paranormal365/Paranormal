using AutoMapper;
using Ben.Data.Common.Constants;
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
/// Tests for CaseTransferController — propose, respond, and cancel case transfers between orgs.
/// </summary>
public class CaseTransferControllerTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    // Non-pooled factory avoids context-reuse contamination across IsOrgAdminAsync + Propose body
    private sealed class SimpleFactory(DbContextOptions<BenDataContext> options) : IDbContextFactory<BenDataContext>
    {
        public BenDataContext CreateDbContext() => new(options);
        public Task<BenDataContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(new BenDataContext(options));
    }

    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var options = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SimpleFactory(options);
    }

    private static IMapper CreateMapper()
    {
        var m = new Mock<IMapper>();
        m.Setup(x => x.Map<CaseTransferLogRecord>(It.IsAny<object>()))
            .Returns<object>(o => o is CaseTransferLog l
                ? new CaseTransferLogRecord { Id = l.Id, CaseId = l.CaseId, FromOrganizationId = l.FromOrganizationId, ToOrganizationId = l.ToOrganizationId, ProposedByAppUserId = l.ProposedByAppUserId, Status = l.Status, DateProposed = l.DateProposed }
                : new CaseTransferLogRecord { DateProposed = DateTime.UtcNow });
        m.Setup(x => x.Map<IEnumerable<CaseTransferLogRecord>>(It.IsAny<object>()))
            .Returns<object>(o => o is IEnumerable<CaseTransferLog> list
                ? list.Select(l => new CaseTransferLogRecord { Id = l.Id, CaseId = l.CaseId, FromOrganizationId = l.FromOrganizationId, ToOrganizationId = l.ToOrganizationId, Status = l.Status, DateProposed = l.DateProposed })
                : []);
        return m.Object;
    }

    private static CaseTransferController BuildController(IDbContextFactory<BenDataContext> factory, Guid userId, bool isAdmin = true)
    {
        var ctrl = new CaseTransferController(factory, CreateMapper());
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"))
            }
        };
        return ctrl;
    }

    /// <summary>Seeds two orgs: fromOrg (with admin userId) and toOrg (with toUserId).</summary>
    private static async Task<(IDbContextFactory<BenDataContext>, Guid fromOrgId, Guid toOrgId, Guid caseId, Guid fromUserId, Guid toUserId)> SeedAsync()
    {
        var factory    = CreateFactory();
        var fromOrgId  = Guid.NewGuid();
        var toOrgId    = Guid.NewGuid();
        var caseId     = Guid.NewGuid();
        var fromUserId = Guid.NewGuid();
        var toUserId   = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();
        db.Users.Add(new AppUser { Id = fromUserId, UserName = "from@test.com", NormalizedUserName = "FROM@TEST.COM", Email = "from@test.com", NormalizedEmail = "FROM@TEST.COM", DateCreated = DateTime.UtcNow });
        db.Users.Add(new AppUser { Id = toUserId,   UserName = "to@test.com",   NormalizedUserName = "TO@TEST.COM",   Email = "to@test.com",   NormalizedEmail = "TO@TEST.COM",   DateCreated = DateTime.UtcNow });
        db.Organizations.Add(new Organization { Id = fromOrgId, Name = "From Org", UrlName = "from", DateCreated = DateTime.UtcNow, CreatedByAppUserId = fromUserId });
        db.Organizations.Add(new Organization { Id = toOrgId,   Name = "To Org",   UrlName = "to",   DateCreated = DateTime.UtcNow, CreatedByAppUserId = toUserId });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = fromOrgId, AppUserId = fromUserId,
            Role = OrganizationMemberRole.Owner, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = fromUserId,
        });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = toOrgId, AppUserId = toUserId,
            Role = OrganizationMemberRole.Owner, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = toUserId,
        });
        db.Cases.Add(new Case
        {
            Id = caseId, OrganizationId = fromOrgId, Title = "Transfer Case",
            CaseYear = 2026, OrgCaseNumber = 1, Status = CaseStatus.Accepted,
            StreetAddress1 = "1 Main", City = "Nashville", State = "TN", ZipCode = "37201", Country = "US",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = fromUserId,
        });
        await db.SaveChangesAsync();
        return (factory, fromOrgId, toOrgId, caseId, fromUserId, toUserId);
    }

    // ── GetAll ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_NonMember_ReturnsForbid()
    {
        var (factory, fromOrgId, _, caseId, _, _) = await SeedAsync();
        var ctrl = BuildController(factory, Guid.NewGuid());

        var result = await ctrl.GetAll(fromOrgId, caseId, default);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task GetAll_Member_ReturnsEmptyList()
    {
        var (factory, fromOrgId, _, caseId, fromUserId, _) = await SeedAsync();
        var ctrl = BuildController(factory, fromUserId);

        var result = await ctrl.GetAll(fromOrgId, caseId, default);

        var ok   = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsAssignableFrom<IEnumerable<CaseTransferLogRecord>>(ok.Value);
        Assert.Empty(list);
    }

    // ── Propose ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Propose_ValidRequest_CreatesLogAndMarksCaseTransferred()
    {
        var (factory, fromOrgId, toOrgId, caseId, fromUserId, _) = await SeedAsync();
        var ctrl = BuildController(factory, fromUserId);

        var result = await ctrl.Propose(fromOrgId, caseId, new ProposeCaseTransferRequest(toOrgId, "Better fit"), default);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var dto = Assert.IsType<CaseTransferLogRecord>(created.Value);
        Assert.Equal(CaseTransferStatus.Pending, dto.Status);
        Assert.Equal(fromOrgId, dto.FromOrganizationId);
        Assert.Equal(toOrgId,   dto.ToOrganizationId);

        await using var db = await factory.CreateDbContextAsync();
        var c = await db.Cases.FindAsync(caseId);
        Assert.Equal(CaseStatus.Transferred, c!.Status);
    }

    [Fact]
    public async Task Propose_TargetOrgNotFound_ReturnsBadRequest()
    {
        var (factory, fromOrgId, _, caseId, fromUserId, _) = await SeedAsync();
        var ctrl = BuildController(factory, fromUserId);

        var result = await ctrl.Propose(fromOrgId, caseId, new ProposeCaseTransferRequest(Guid.NewGuid(), null), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Propose_CaseNotInOrg_ReturnsNotFound()
    {
        var (factory, fromOrgId, toOrgId, _, fromUserId, _) = await SeedAsync();
        var ctrl = BuildController(factory, fromUserId);

        var result = await ctrl.Propose(fromOrgId, Guid.NewGuid(), new ProposeCaseTransferRequest(toOrgId, null), default);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task Propose_NonAdmin_ReturnsForbid()
    {
        var (factory, fromOrgId, toOrgId, caseId, fromUserId, _) = await SeedAsync();
        // Change role to Member (not admin)
        await using var db = await factory.CreateDbContextAsync();
        var membership = await db.OrganizationUserMemberships.FirstAsync(m => m.AppUserId == fromUserId);
        membership.Role = OrganizationMemberRole.Member;
        await db.SaveChangesAsync();

        var ctrl = BuildController(factory, fromUserId);
        var result = await ctrl.Propose(fromOrgId, caseId, new ProposeCaseTransferRequest(toOrgId, null), default);

        Assert.IsType<ForbidResult>(result.Result);
    }

    // ── Respond ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Respond_Accept_TransfersCaseToReceivingOrg()
    {
        var (factory, fromOrgId, toOrgId, caseId, fromUserId, toUserId) = await SeedAsync();
        var proposer  = BuildController(factory, fromUserId);
        var propResult = await proposer.Propose(fromOrgId, caseId, new ProposeCaseTransferRequest(toOrgId, null), default);
        var logId = ((CaseTransferLogRecord)((CreatedAtActionResult)propResult.Result!).Value!).Id;

        var responder = BuildController(factory, toUserId);
        var result = await responder.Respond(toOrgId, caseId, logId, new RespondTransferRequest(true, null), default);

        var ok  = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<CaseTransferLogRecord>(ok.Value);
        Assert.Equal(CaseTransferStatus.Accepted, dto.Status);

        await using var db = await factory.CreateDbContextAsync();
        var c = await db.Cases.FindAsync(caseId);
        Assert.Equal(toOrgId, c!.OrganizationId);
        Assert.Equal(CaseStatus.Accepted, c.Status);
    }

    [Fact]
    public async Task Respond_Reject_KeepsCaseInFromOrg()
    {
        var (factory, fromOrgId, toOrgId, caseId, fromUserId, toUserId) = await SeedAsync();
        var proposer  = BuildController(factory, fromUserId);
        var propResult = await proposer.Propose(fromOrgId, caseId, new ProposeCaseTransferRequest(toOrgId, null), default);
        var logId = ((CaseTransferLogRecord)((CreatedAtActionResult)propResult.Result!).Value!).Id;

        var responder = BuildController(factory, toUserId);
        var result = await responder.Respond(toOrgId, caseId, logId, new RespondTransferRequest(false, "Not our area"), default);

        var ok  = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<CaseTransferLogRecord>(ok.Value);
        Assert.Equal(CaseTransferStatus.Rejected, dto.Status);

        await using var db = await factory.CreateDbContextAsync();
        var c = await db.Cases.FindAsync(caseId);
        Assert.Equal(fromOrgId, c!.OrganizationId); // unchanged
    }

    [Fact]
    public async Task Respond_AlreadyResponded_ReturnsBadRequest()
    {
        var (factory, fromOrgId, toOrgId, caseId, fromUserId, toUserId) = await SeedAsync();
        var proposer  = BuildController(factory, fromUserId);
        var propResult = await proposer.Propose(fromOrgId, caseId, new ProposeCaseTransferRequest(toOrgId, null), default);
        var logId = ((CaseTransferLogRecord)((CreatedAtActionResult)propResult.Result!).Value!).Id;
        var responder = BuildController(factory, toUserId);
        await responder.Respond(toOrgId, caseId, logId, new RespondTransferRequest(true, null), default);

        var result = await responder.Respond(toOrgId, caseId, logId, new RespondTransferRequest(false, null), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    // ── Cancel ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cancel_Pending_SetsCancelledAndRestoresCaseStatus()
    {
        var (factory, fromOrgId, toOrgId, caseId, fromUserId, _) = await SeedAsync();
        var proposer  = BuildController(factory, fromUserId);
        var propResult = await proposer.Propose(fromOrgId, caseId, new ProposeCaseTransferRequest(toOrgId, null), default);
        var logId = ((CaseTransferLogRecord)((CreatedAtActionResult)propResult.Result!).Value!).Id;

        var result = await proposer.Cancel(fromOrgId, caseId, logId, default);

        var ok  = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<CaseTransferLogRecord>(ok.Value);
        Assert.Equal(CaseTransferStatus.Cancelled, dto.Status);

        await using var db = await factory.CreateDbContextAsync();
        var c = await db.Cases.FindAsync(caseId);
        Assert.Equal(CaseStatus.Accepted, c!.Status); // restored
    }

    [Fact]
    public async Task Cancel_AlreadyAccepted_ReturnsBadRequest()
    {
        var (factory, fromOrgId, toOrgId, caseId, fromUserId, toUserId) = await SeedAsync();
        var proposer   = BuildController(factory, fromUserId);
        var propResult = await proposer.Propose(fromOrgId, caseId, new ProposeCaseTransferRequest(toOrgId, null), default);
        var logId = ((CaseTransferLogRecord)((CreatedAtActionResult)propResult.Result!).Value!).Id;
        var responder  = BuildController(factory, toUserId);
        await responder.Respond(toOrgId, caseId, logId, new RespondTransferRequest(true, null), default);

        var result = await proposer.Cancel(fromOrgId, caseId, logId, default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    // ── Cross-org chain (Phase B) ────────────────────────────────────────────

    [Fact]
    public async Task GetAll_CaseBelongsToDifferentOrg_ReturnsNotFound()
    {
        // toUserId is a real, active member of toOrgId — but caseId belongs to fromOrgId, and
        // GetAll previously checked only "is caller a member of the route org," never that the
        // case actually belonged to it.
        var (factory, _, toOrgId, caseId, _, toUserId) = await SeedAsync();
        var ctrl = BuildController(factory, toUserId);

        var result = await ctrl.GetAll(toOrgId, caseId, default);

        Assert.IsType<NotFoundResult>(result.Result);
    }
}
