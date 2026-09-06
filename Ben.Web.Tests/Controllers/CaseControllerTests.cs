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
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Tests for CaseController — org case CRUD, accept/decline client requests,
/// timeline entries, and case-number auto-assignment.
/// </summary>
public class CaseControllerTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    // Non-pooled factory required: controller calls FirstAsync with multi-level Includes
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
        m.Setup(x => x.Map<CaseRecord>(It.IsAny<object>()))
            .Returns<object>(o => o is Case c
                ? new CaseRecord { Id = c.Id, OrganizationId = c.OrganizationId, Title = c.Title, Description = c.Description, Status = c.Status, CaseYear = c.CaseYear, OrgCaseNumber = c.OrgCaseNumber, StreetAddress1 = c.StreetAddress1, City = c.City, State = c.State, ZipCode = c.ZipCode, Country = c.Country, DateCaseOpened = c.DateCaseOpened, DateCreated = c.DateCreated, CaseManagerAppUserId = c.CaseManagerAppUserId, DateCaseClosed = c.DateCaseClosed }
                : new CaseRecord { Title = "", StreetAddress1 = "", City = "", State = "", ZipCode = "", Country = "", DateCaseOpened = DateTime.UtcNow, DateCreated = DateTime.UtcNow });
        m.Setup(x => x.Map<IEnumerable<CaseRecord>>(It.IsAny<object>()))
            .Returns<object>(o => o is IEnumerable<Case> list
                ? list.Select(c => new CaseRecord { Id = c.Id, OrganizationId = c.OrganizationId, Title = c.Title, Status = c.Status, CaseYear = c.CaseYear, OrgCaseNumber = c.OrgCaseNumber, StreetAddress1 = c.StreetAddress1, City = c.City, State = c.State, ZipCode = c.ZipCode, Country = c.Country, DateCaseOpened = c.DateCaseOpened, DateCreated = c.DateCreated })
                : []);
        m.Setup(x => x.Map<CaseTimelineEntryRecord>(It.IsAny<object>()))
            .Returns<object>(o => o is CaseTimelineEntry e
                ? new CaseTimelineEntryRecord { Id = e.Id, CaseId = e.CaseId, AuthorAppUserId = e.AuthorAppUserId, EntryType = e.EntryType, Title = e.Title, Body = e.Body, Visibility = e.Visibility, InvestigationId = e.InvestigationId, DateCreated = e.DateCreated }
                : new CaseTimelineEntryRecord { DateCreated = DateTime.UtcNow });
        m.Setup(x => x.Map<IEnumerable<CaseTimelineEntryRecord>>(It.IsAny<object>()))
            .Returns<object>(o => o is IEnumerable<CaseTimelineEntry> list
                ? list.Select(e => new CaseTimelineEntryRecord { Id = e.Id, CaseId = e.CaseId, EntryType = e.EntryType, Title = e.Title, InvestigationId = e.InvestigationId, DateCreated = e.DateCreated })
                : []);
        return m.Object;
    }

    private static CaseController Build(IDbContextFactory<BenDataContext> factory, Guid userId, bool isAdmin = false, bool isSuperAdmin = false)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
        if (isSuperAdmin) claims.Add(new Claim(ClaimTypes.Role, RoleNames.SuperAdmin));
        var ctrl = new CaseController(factory, CreateMapper(), new Ben.Data.WebApi.Services.Billing.SubscriptionLimitGuard(factory), new Ben.Service.RepositoryService.Services.OrganizationSecurityService(factory), new Ben.Data.WebApi.Services.RequestReviewNotifier(factory, new Ben.Data.WebApi.Services.PlatformMessageService(factory)), Ben.Web.Tests.TestMailer.Quiet());
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"))
            }
        };
        return ctrl;
    }

    private static async Task<(IDbContextFactory<BenDataContext>, Guid orgId, Guid userId)> SeedAsync(
        bool makeAdmin = true)
    {
        var factory = CreateFactory();
        var orgId   = Guid.NewGuid();
        var userId  = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();
        db.Users.Add(new AppUser { Id = userId, UserName = "u@t.com", NormalizedUserName = "U@T.COM", Email = "u@t.com", NormalizedEmail = "U@T.COM", DateCreated = DateTime.UtcNow });
        db.Organizations.Add(new Organization { Id = orgId, Name = "Test Org", UrlName = "test", DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = userId,
            Role = makeAdmin ? OrganizationMemberRole.Owner : OrganizationMemberRole.Member,
            IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        await db.SaveChangesAsync();
        await TestSeeds.BridgeAsync(factory, orgId);
        return (factory, orgId, userId);
    }

    private static CreateCaseRequest MakeCreateRequest(string title = "Test Case") =>
        new(title, null, "123 Main St", null, "Nashville", "TN", "37201", "US", null, null);

    // ── GetAll ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_NonMember_ReturnsForbid()
    {
        var (factory, orgId, _) = await SeedAsync();
        var ctrl = Build(factory, Guid.NewGuid());
        Assert.IsType<ForbidResult>((await ctrl.GetAll(orgId, default)).Result);
    }

    [Fact]
    public async Task GetAll_Member_ReturnsEmptyList()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var ctrl = Build(factory, userId);
        var ok = Assert.IsType<OkObjectResult>((await ctrl.GetAll(orgId, default)).Result);
        Assert.Empty((IEnumerable<CaseRecord>)ok.Value!);
    }

    // ── GetById ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetById_ExistingCase_ReturnsCaseRecord()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var ctrl = Build(factory, userId);
        var created = (CaseRecord)((CreatedAtActionResult)(await ctrl.Create(orgId, MakeCreateRequest(), default)).Result!).Value!;

        var ok = Assert.IsType<OkObjectResult>((await ctrl.GetById(orgId, created.Id, default)).Result);
        Assert.Equal(created.Id, ((CaseRecord)ok.Value!).Id);
    }

    [Fact]
    public async Task GetById_MissingId_ReturnsNotFound()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var ctrl = Build(factory, userId);
        Assert.IsType<NotFoundResult>((await ctrl.GetById(orgId, Guid.NewGuid(), default)).Result);
    }

    // ── Create ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_Admin_ReturnsCreated()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var ctrl   = Build(factory, userId);
        var result = await ctrl.Create(orgId, MakeCreateRequest("Haunted House"), default);
        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var dto = Assert.IsType<CaseRecord>(created.Value);
        Assert.Equal("Haunted House", dto.Title);
        Assert.Equal(CaseStatus.Proposed, dto.Status);
    }

    [Fact]
    public async Task Create_AutoAssignsCaseNumber_StartsAtOne()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var ctrl = Build(factory, userId);
        var dto = (CaseRecord)((CreatedAtActionResult)(await ctrl.Create(orgId, MakeCreateRequest(), default)).Result!).Value!;
        Assert.Equal(1, dto.OrgCaseNumber);
        Assert.Equal(DateTime.UtcNow.Year, dto.CaseYear);
    }

    [Fact]
    public async Task Create_SecondCase_AssignsIncrementedNumber()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var ctrl = Build(factory, userId);
        await ctrl.Create(orgId, MakeCreateRequest("First"), default);
        var dto2 = (CaseRecord)((CreatedAtActionResult)(await ctrl.Create(orgId, MakeCreateRequest("Second"), default)).Result!).Value!;
        Assert.Equal(2, dto2.OrgCaseNumber);
    }

    [Fact]
    public async Task Create_NonAdmin_ReturnsForbid()
    {
        var (factory, orgId, _) = await SeedAsync(makeAdmin: false);
        var memberId = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        db.Users.Add(new AppUser { Id = memberId, UserName = "m@t.com", NormalizedUserName = "M@T.COM", Email = "m@t.com", NormalizedEmail = "M@T.COM", DateCreated = DateTime.UtcNow });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership { Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = memberId, Role = OrganizationMemberRole.Member, IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = memberId });
        await db.SaveChangesAsync();

        var ctrl = Build(factory, memberId);
        Assert.IsType<ForbidResult>((await ctrl.Create(orgId, MakeCreateRequest(), default)).Result);
    }

    // ── Update ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_Admin_UpdatesTitleAndStatus()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var ctrl    = Build(factory, userId);
        var caseId  = ((CaseRecord)((CreatedAtActionResult)(await ctrl.Create(orgId, MakeCreateRequest(), default)).Result!).Value!).Id;

        var result = await ctrl.Update(orgId, caseId, new UpdateCaseRequest("Updated", null, CaseStatus.Accepted, null, false, null), default);
        var ok  = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<CaseRecord>(ok.Value);
        Assert.Equal("Updated", dto.Title);
        Assert.Equal(CaseStatus.Accepted, dto.Status);
    }

    [Fact]
    public async Task Update_ClosedStatus_SetsDateCaseClosed()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var ctrl   = Build(factory, userId);
        var caseId = ((CaseRecord)((CreatedAtActionResult)(await ctrl.Create(orgId, MakeCreateRequest(), default)).Result!).Value!).Id;

        await ctrl.Update(orgId, caseId, new UpdateCaseRequest(null, null, CaseStatus.Closed, null, false, null), default);

        await using var db = await factory.CreateDbContextAsync();
        var c = await db.Cases.FindAsync(caseId);
        Assert.NotNull(c!.DateCaseClosed);
    }

    [Fact]
    public async Task Update_CaseManager_CanUpdateOwnCase()
    {
        var (factory, orgId, adminId) = await SeedAsync();
        var managerId = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        db.Users.Add(new AppUser { Id = managerId, UserName = "mgr@t.com", NormalizedUserName = "MGR@T.COM", Email = "mgr@t.com", NormalizedEmail = "MGR@T.COM", DateCreated = DateTime.UtcNow });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership { Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = managerId, Role = OrganizationMemberRole.Manager, IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId });
        await db.SaveChangesAsync();

        var admin = Build(factory, adminId);
        var caseId = ((CaseRecord)((CreatedAtActionResult)(await admin.Create(orgId, MakeCreateRequest(), default)).Result!).Value!).Id;
        // Assign case manager
        await admin.Update(orgId, caseId, new UpdateCaseRequest(null, null, CaseStatus.Accepted, null, false, managerId), default);

        var mgr = Build(factory, managerId);
        var result = await mgr.Update(orgId, caseId, new UpdateCaseRequest("Mgr Updated", null, CaseStatus.Accepted, null, false, managerId), default);
        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task Update_MissingCase_ReturnsNotFound()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var ctrl   = Build(factory, userId);
        var result = await ctrl.Update(orgId, Guid.NewGuid(), new UpdateCaseRequest(null, null, CaseStatus.Accepted, null, false, null), default);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    // ── Timeline ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTimeline_ReturnsEmptyList()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var ctrl   = Build(factory, userId);
        var caseId = ((CaseRecord)((CreatedAtActionResult)(await ctrl.Create(orgId, MakeCreateRequest(), default)).Result!).Value!).Id;
        var ok = Assert.IsType<OkObjectResult>((await ctrl.GetTimeline(orgId, caseId, null, default)).Result);
        Assert.Empty((IEnumerable<CaseTimelineEntryRecord>)ok.Value!);
    }

    [Fact]
    public async Task AddTimelineEntry_CreatesEntry()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var ctrl   = Build(factory, userId);
        var caseId = ((CaseRecord)((CreatedAtActionResult)(await ctrl.Create(orgId, MakeCreateRequest(), default)).Result!).Value!).Id;

        var req    = new UpsertTimelineEntryRequest(CaseTimelineEntryType.Evidence, DateTime.UtcNow, "Strange noise", "Heard in basement", CaseTimelineVisibility.Public, []);
        var result = await ctrl.AddTimelineEntry(orgId, caseId, req, default);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var dto = Assert.IsType<CaseTimelineEntryRecord>(created.Value);
        Assert.Equal("Strange noise", dto.Title);
        Assert.Equal(CaseTimelineEntryType.Evidence, dto.EntryType);
    }

    [Fact]
    public async Task DeleteTimelineEntry_AuthorCanDelete()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var ctrl   = Build(factory, userId);
        var caseId = ((CaseRecord)((CreatedAtActionResult)(await ctrl.Create(orgId, MakeCreateRequest(), default)).Result!).Value!).Id;

        var req     = new UpsertTimelineEntryRequest(CaseTimelineEntryType.Evidence, null, "To delete", null, CaseTimelineVisibility.OrgOnly, []);
        var entryId = ((CaseTimelineEntryRecord)((CreatedAtActionResult)(await ctrl.AddTimelineEntry(orgId, caseId, req, default)).Result!).Value!).Id;

        var result = await ctrl.DeleteTimelineEntry(orgId, caseId, entryId, default);
        Assert.IsType<NoContentResult>(result);

        await using var db = await factory.CreateDbContextAsync();
        Assert.False(await db.CaseTimelineEntries.AnyAsync(e => e.Id == entryId));
    }

    [Fact]
    public async Task DeleteTimelineEntry_NonAuthorNonAdmin_ReturnsForbid()
    {
        var (factory, orgId, adminId) = await SeedAsync();
        var memberId = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        db.Users.Add(new AppUser { Id = memberId, UserName = "m@t.com", NormalizedUserName = "M@T.COM", Email = "m@t.com", NormalizedEmail = "M@T.COM", DateCreated = DateTime.UtcNow });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership { Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = memberId, Role = OrganizationMemberRole.Member, IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId });
        await db.SaveChangesAsync();

        var admin  = Build(factory, adminId);
        var caseId = ((CaseRecord)((CreatedAtActionResult)(await admin.Create(orgId, MakeCreateRequest(), default)).Result!).Value!).Id;
        var entryId = ((CaseTimelineEntryRecord)((CreatedAtActionResult)(await admin.AddTimelineEntry(orgId, caseId, new UpsertTimelineEntryRequest(CaseTimelineEntryType.Evidence, null, "X", null, CaseTimelineVisibility.OrgOnly, []), default)).Result!).Value!).Id;

        var member = Build(factory, memberId);
        Assert.IsType<ForbidResult>(await member.DeleteTimelineEntry(orgId, caseId, entryId, default));
    }

    [Fact]
    public async Task UpdateTimelineEntry_CaseBelongsToDifferentOrg_ReturnsNotFound()
    {
        // The core of the fix: neither action verified caseId belonged to the route orgId at
        // all — an org admin of THEIR OWN org could edit/delete another org's timeline entries
        // just by knowing the entryId, since entry.AuthorAppUserId != userId always falls through
        // to IsOrgAdminOrSuperAsync(orgId) — which only checks the CALLER's own org.
        var (factory, victimOrgId, victimAdminId) = await SeedAsync();
        var victim  = Build(factory, victimAdminId);
        var caseId  = ((CaseRecord)((CreatedAtActionResult)(await victim.Create(victimOrgId, MakeCreateRequest(), default)).Result!).Value!).Id;
        var entryId = ((CaseTimelineEntryRecord)((CreatedAtActionResult)(await victim.AddTimelineEntry(victimOrgId, caseId, new UpsertTimelineEntryRequest(CaseTimelineEntryType.Evidence, null, "Private", null, CaseTimelineVisibility.OrgOnly, []), default)).Result!).Value!).Id;

        var (attackerFactory, attackerOrgId, attackerId) = (factory, Guid.NewGuid(), Guid.NewGuid());
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Users.Add(new AppUser { Id = attackerId, UserName = "atk@t.com", NormalizedUserName = "ATK@T.COM", Email = "atk@t.com", NormalizedEmail = "ATK@T.COM", DateCreated = DateTime.UtcNow });
            db.Organizations.Add(new Organization { Id = attackerOrgId, Name = "Attacker Org", UrlName = "attacker", DateCreated = DateTime.UtcNow, CreatedByAppUserId = attackerId });
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership { Id = Guid.NewGuid(), OrganizationId = attackerOrgId, AppUserId = attackerId, Role = OrganizationMemberRole.Owner, IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = attackerId });
            await db.SaveChangesAsync();
        }
        var attacker = Build(attackerFactory, attackerId);

        var updateResult = await attacker.UpdateTimelineEntry(attackerOrgId, caseId, entryId,
            new UpsertTimelineEntryRequest(CaseTimelineEntryType.Evidence, null, "Hijacked", null, CaseTimelineVisibility.OrgOnly, []), default);
        Assert.IsType<NotFoundResult>(updateResult.Result);

        var deleteResult = await attacker.DeleteTimelineEntry(attackerOrgId, caseId, entryId, default);
        Assert.IsType<NotFoundResult>(deleteResult);

        await using var verifyDb = await factory.CreateDbContextAsync();
        var stillThere = await verifyDb.CaseTimelineEntries.FirstAsync(e => e.Id == entryId);
        Assert.Equal("Private", stillThere.Title);
    }

    // ── Pending requests ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetPendingRequests_ReturnsOnlyPendingApplications()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var clientId = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();
        db.Users.Add(new AppUser { Id = clientId, UserName = "c@t.com", NormalizedUserName = "C@T.COM", Email = "c@t.com", NormalizedEmail = "C@T.COM", DateCreated = DateTime.UtcNow });
        var req = new ClientRequest { Id = Guid.NewGuid(), AppUserId = clientId, City = "Nashville", State = "TN", ZipCode = "37201", Country = "US", StreetAddress1 = "1 Main", Description = "Strange things", Status = ClientRequestStatus.Submitted, DateCreated = DateTime.UtcNow, CreatedByAppUserId = clientId };
        db.ClientRequests.Add(req);
        db.ClientRequestOrganizations.Add(new ClientRequestOrganization { Id = Guid.NewGuid(), ClientRequestId = req.Id, OrganizationId = orgId, Status = ClientOrgRequestStatus.Pending, DateApplied = DateTime.UtcNow, DateCreated = DateTime.UtcNow, CreatedByAppUserId = clientId });
        await db.SaveChangesAsync();

        var ctrl = Build(factory, userId);
        var ok   = Assert.IsType<OkObjectResult>((await ctrl.GetPendingRequests(orgId, default)).Result);
        var list = Assert.IsAssignableFrom<IEnumerable<OrgPendingRequestRecord>>(ok.Value);
        Assert.Single(list);
    }

    // ── Accept/Decline client requests ────────────────────────────────────────

    [Fact]
    public async Task DeclineClientRequest_SetStatusToRejected()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var clientId = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();
        db.Users.Add(new AppUser { Id = clientId, UserName = "c@t.com", NormalizedUserName = "C@T.COM", Email = "c@t.com", NormalizedEmail = "C@T.COM", DateCreated = DateTime.UtcNow });
        var req = new ClientRequest { Id = Guid.NewGuid(), AppUserId = clientId, City = "Nashville", State = "TN", ZipCode = "37201", Country = "US", StreetAddress1 = "1 Main", Description = "Desc", Status = ClientRequestStatus.Submitted, DateCreated = DateTime.UtcNow, CreatedByAppUserId = clientId };
        db.ClientRequests.Add(req);
        db.ClientRequestOrganizations.Add(new ClientRequestOrganization { Id = Guid.NewGuid(), ClientRequestId = req.Id, OrganizationId = orgId, Status = ClientOrgRequestStatus.Pending, DateApplied = DateTime.UtcNow, DateCreated = DateTime.UtcNow, CreatedByAppUserId = clientId });
        await db.SaveChangesAsync();

        var ctrl = Build(factory, userId);
        var result = await ctrl.DeclineClientRequest(orgId, req.Id, default);

        Assert.IsType<NoContentResult>(result);
        await using var db2 = await factory.CreateDbContextAsync();
        var app = await db2.ClientRequestOrganizations.FirstAsync(a => a.ClientRequestId == req.Id);
        Assert.Equal(ClientOrgRequestStatus.Rejected, app.Status);
    }

    [Fact]
    public async Task DeclineClientRequest_LastActiveOrg_SetsParentRequestToDeclined()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var clientId = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();
        db.Users.Add(new AppUser { Id = clientId, UserName = "c2@t.com", NormalizedUserName = "C2@T.COM", Email = "c2@t.com", NormalizedEmail = "C2@T.COM", DateCreated = DateTime.UtcNow });
        var req = new ClientRequest { Id = Guid.NewGuid(), AppUserId = clientId, City = "Nashville", State = "TN", ZipCode = "37201", Country = "US", StreetAddress1 = "1 Main", Description = "Desc", Status = ClientRequestStatus.Submitted, DateCreated = DateTime.UtcNow, CreatedByAppUserId = clientId };
        db.ClientRequests.Add(req);
        db.ClientRequestOrganizations.Add(new ClientRequestOrganization { Id = Guid.NewGuid(), ClientRequestId = req.Id, OrganizationId = orgId, Status = ClientOrgRequestStatus.Pending, DateApplied = DateTime.UtcNow, DateCreated = DateTime.UtcNow, CreatedByAppUserId = clientId });
        await db.SaveChangesAsync();

        var ctrl = Build(factory, userId);
        await ctrl.DeclineClientRequest(orgId, req.Id, default);

        await using var db2 = await factory.CreateDbContextAsync();
        var updated = await db2.ClientRequests.FirstAsync(r => r.Id == req.Id);
        Assert.Equal(ClientRequestStatus.Declined, updated.Status);
    }

    [Fact]
    public async Task DeclineClientRequest_OtherOrgStillPending_DoesNotDeclineParentRequest()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var secondOrgId = Guid.NewGuid();
        var clientId = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();
        db.Users.Add(new AppUser { Id = clientId, UserName = "c3@t.com", NormalizedUserName = "C3@T.COM", Email = "c3@t.com", NormalizedEmail = "C3@T.COM", DateCreated = DateTime.UtcNow });
        db.Organizations.Add(new Organization { Id = secondOrgId, Name = "Second Org", UrlName = "second", DateCreated = DateTime.UtcNow, CreatedByAppUserId = clientId });
        var req = new ClientRequest { Id = Guid.NewGuid(), AppUserId = clientId, City = "Nashville", State = "TN", ZipCode = "37201", Country = "US", StreetAddress1 = "1 Main", Description = "Desc", Status = ClientRequestStatus.Submitted, DateCreated = DateTime.UtcNow, CreatedByAppUserId = clientId };
        db.ClientRequests.Add(req);
        db.ClientRequestOrganizations.Add(new ClientRequestOrganization { Id = Guid.NewGuid(), ClientRequestId = req.Id, OrganizationId = orgId, Status = ClientOrgRequestStatus.Pending, DateApplied = DateTime.UtcNow, DateCreated = DateTime.UtcNow, CreatedByAppUserId = clientId });
        db.ClientRequestOrganizations.Add(new ClientRequestOrganization { Id = Guid.NewGuid(), ClientRequestId = req.Id, OrganizationId = secondOrgId, Status = ClientOrgRequestStatus.Pending, DateApplied = DateTime.UtcNow, DateCreated = DateTime.UtcNow, CreatedByAppUserId = clientId });
        await db.SaveChangesAsync();

        var ctrl = Build(factory, userId);
        await ctrl.DeclineClientRequest(orgId, req.Id, default);

        await using var db2 = await factory.CreateDbContextAsync();
        var updated = await db2.ClientRequests.FirstAsync(r => r.Id == req.Id);
        Assert.Equal(ClientRequestStatus.Submitted, updated.Status);
    }

    [Fact]
    public async Task AcceptClientRequest_CreatesCaseAndCmsPages()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var clientId = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();
        db.Users.Add(new AppUser { Id = clientId, UserName = "client@t.com", NormalizedUserName = "CLIENT@T.COM", Email = "client@t.com", NormalizedEmail = "CLIENT@T.COM", DisplayName = "Daniel Park", DateCreated = DateTime.UtcNow });
        var req = new ClientRequest { Id = Guid.NewGuid(), AppUserId = clientId, City = "Nashville", State = "TN", ZipCode = "37201", Country = "US", StreetAddress1 = "1 Main", Description = "Haunting", Status = ClientRequestStatus.Submitted, DateCreated = DateTime.UtcNow, CreatedByAppUserId = clientId };
        db.ClientRequests.Add(req);
        db.ClientRequestOrganizations.Add(new ClientRequestOrganization { Id = Guid.NewGuid(), ClientRequestId = req.Id, OrganizationId = orgId, Status = ClientOrgRequestStatus.Pending, DateApplied = DateTime.UtcNow, DateCreated = DateTime.UtcNow, CreatedByAppUserId = clientId });
        await db.SaveChangesAsync();

        var ctrl   = Build(factory, userId);
        var result = await ctrl.AcceptClientRequest(orgId, req.Id, new AcceptClientRequestAsCaseRequest(null, null), default);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var dto = Assert.IsType<CaseRecord>(created.Value);
        Assert.Equal(CaseStatus.Accepted, dto.Status);
        Assert.Equal(1, dto.OrgCaseNumber);
        Assert.Equal("Haunting", dto.Description);

        await using var db2 = await factory.CreateDbContextAsync();
        var cmsPages = await db2.OrganizationPages.CountAsync(p => p.CaseId == dto.Id);
        Assert.Equal(4, cmsPages);

        var app = await db2.ClientRequestOrganizations.FirstAsync(a => a.ClientRequestId == req.Id);
        Assert.Equal(ClientOrgRequestStatus.Accepted, app.Status);
    }

    // ── GetClientRequest (C1) ─────────────────────────────────────────────────

    /// <summary>Seeds a client request with optional attachments and a case pointing at it.</summary>
    private static async Task<(Guid CaseId, Guid RequestId)> SeedCaseFromRequestAsync(
        IDbContextFactory<BenDataContext> factory, Guid orgId, Guid userId,
        int attachmentCount = 0, bool linkRequest = true)
    {
        var caseId    = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();

        db.ClientRequests.Add(new ClientRequest
        {
            Id = requestId, AppUserId = userId,
            Status = ClientRequestStatus.Assigned,
            StreetAddress1 = "12 Ghost Lane", StreetAddress2 = "Apt 3",
            City = "Nashville", State = "TN", ZipCode = "37201", Country = "US",
            Gender = ClientGender.Female, BirthYear = 1984,
            Description = "<p>Footsteps upstairs every night around 2am.</p>",
            Latitude = 36.16m, Longitude = -86.78m,
            DateCreated = new DateTime(2026, 3, 4, 9, 30, 0, DateTimeKind.Utc),
            CreatedByAppUserId = userId,
        });

        for (var i = 0; i < attachmentCount; i++)
        {
            var fileId = Guid.NewGuid();
            db.UploadFiles.Add(new UploadFile
            {
                Id = fileId, UploadFileTypeId = Guid.NewGuid(), AppUserId = userId,
                FileName = $"evidence{i}.jpg", StoredFileName = $"s{i}.jpg", ContentType = "image/jpeg",
                FileSize = 100 + i, FileData = new byte[4],
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
            });
            db.ClientRequestFiles.Add(new ClientRequestFile
            {
                Id = Guid.NewGuid(), ClientRequestId = requestId, UploadFileId = fileId,
                DateCreated = DateTime.UtcNow.AddMinutes(i), CreatedByAppUserId = userId,
            });
        }

        db.Cases.Add(new Case
        {
            Id = caseId, OrganizationId = orgId, Title = "The Nashville case",
            Description = "Edited by the org, diverged from the request",
            ClientRequestId = linkRequest ? requestId : null,
            StreetAddress1 = "12 Ghost Lane", City = "Nashville", State = "TN",
            ZipCode = "37201", Country = "US",
            DateCaseOpened = DateTime.UtcNow, DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = userId,
        });
        await db.SaveChangesAsync();
        return (caseId, requestId);
    }

    private static async Task<CaseClientRequestRecord> GetRequestAsync(
        IDbContextFactory<BenDataContext> factory, Guid userId, Guid orgId, Guid caseId)
    {
        var result = await Build(factory, userId).GetClientRequest(orgId, caseId, default);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return Assert.IsType<CaseClientRequestRecord>(ok.Value);
    }

    [Fact]
    public async Task GetClientRequest_ReturnsTheClientsOwnWords_NotTheEditedCase()
    {
        // The whole point: the case description is a snapshot the org then edits, so it stops being
        // what the client said. This endpoint has to return the request's text, not the case's.
        var (factory, orgId, userId) = await SeedAsync();
        var (caseId, requestId) = await SeedCaseFromRequestAsync(factory, orgId, userId);

        var record = await GetRequestAsync(factory, userId, orgId, caseId);

        Assert.Equal(requestId, record.ClientRequestId);
        Assert.Equal("<p>Footsteps upstairs every night around 2am.</p>", record.Description);
        Assert.Equal(new DateTime(2026, 3, 4, 9, 30, 0, DateTimeKind.Utc), record.SubmittedUtc);
        Assert.Equal("12 Ghost Lane", record.StreetAddress1);
        Assert.Equal("Apt 3", record.StreetAddress2);
        Assert.Equal("Nashville", record.City);
        Assert.Equal(ClientGender.Female, record.Gender);
        Assert.Equal(1984, record.BirthYear);
    }

    [Fact]
    public async Task GetClientRequest_ReturnsTheRequestsAttachments_InSubmissionOrder()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var (caseId, _) = await SeedCaseFromRequestAsync(factory, orgId, userId, attachmentCount: 3);

        var record = await GetRequestAsync(factory, userId, orgId, caseId);

        Assert.Equal(3, record.Files.Count);
        Assert.Equal(["evidence0.jpg", "evidence1.jpg", "evidence2.jpg"],
                     record.Files.Select(f => f.FileName));
        Assert.All(record.Files, f => Assert.Equal("image/jpeg", f.ContentType));
    }

    [Fact]
    public async Task GetClientRequest_NonMember_ReturnsForbid()
    {
        // The request carries a client's home address and demographics.
        var (factory, orgId, userId) = await SeedAsync();
        var (caseId, _) = await SeedCaseFromRequestAsync(factory, orgId, userId);

        var result = await Build(factory, Guid.NewGuid()).GetClientRequest(orgId, caseId, default);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task GetClientRequest_PlainMember_CanRead()
    {
        // Reading the originating request is ordinary case work, not an admin action.
        var (factory, orgId, userId) = await SeedAsync(makeAdmin: false);
        var (caseId, _) = await SeedCaseFromRequestAsync(factory, orgId, userId);

        var record = await GetRequestAsync(factory, userId, orgId, caseId);

        Assert.Equal("Nashville", record.City);
    }

    [Fact]
    public async Task GetClientRequest_ForACaseWithNoRequest_ReturnsNotFound()
    {
        // Normal, not an error: cases can be raised internally rather than from a submission.
        var (factory, orgId, userId) = await SeedAsync();
        var (caseId, _) = await SeedCaseFromRequestAsync(factory, orgId, userId, linkRequest: false);

        var result = await Build(factory, userId).GetClientRequest(orgId, caseId, default);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetClientRequest_ForACaseInAnotherOrg_ReturnsNotFound()
    {
        // Being a member of the org named in the route must not resolve another org's case.
        var (factory, orgId, userId) = await SeedAsync();
        var otherOrgId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Organizations.Add(new Organization
            {
                Id = otherOrgId, Name = "Other", UrlName = "other",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
            });
            await db.SaveChangesAsync();
        }
        var (foreignCaseId, _) = await SeedCaseFromRequestAsync(factory, otherOrgId, userId);

        var result = await Build(factory, userId).GetClientRequest(orgId, foreignCaseId, default);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetClientRequest_ForAnUnknownCase_ReturnsNotFound()
    {
        var (factory, orgId, userId) = await SeedAsync();

        var result = await Build(factory, userId).GetClientRequest(orgId, Guid.NewGuid(), default);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetClientRequest_WhenTheRequestRowIsGone_ReturnsNotFoundNotAnError()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var (caseId, requestId) = await SeedCaseFromRequestAsync(factory, orgId, userId);
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.ClientRequests.Remove(await db.ClientRequests.SingleAsync(r => r.Id == requestId));
            await db.SaveChangesAsync();
        }

        var result = await Build(factory, userId).GetClientRequest(orgId, caseId, default);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    // ── Investigator binder (C3) ──────────────────────────────────────────────

    private static async Task<Guid> SeedInvestigationAsync(
        IDbContextFactory<BenDataContext> factory, Guid caseId, Guid userId, string title = "Night visit")
    {
        var invId = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        db.Investigations.Add(new Investigation
        {
            Id = invId, CaseId = caseId, Title = title,
            ScheduledDateTime = DateTime.UtcNow.AddDays(1),
            Status = InvestigationStatus.Scheduled,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        await db.SaveChangesAsync();
        return invId;
    }

    private static async Task<Guid> SeedTimelineEntryAsync(
        IDbContextFactory<BenDataContext> factory, Guid caseId, Guid userId,
        string title, Guid? investigationId = null,
        CaseTimelineEntryType type = CaseTimelineEntryType.InvestigatorNote,
        DateTime? eventDateTime = null, DateTime? dateCreated = null, Guid? id = null)
    {
        id ??= Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        db.CaseTimelineEntries.Add(new CaseTimelineEntry
        {
            Id = id.Value, CaseId = caseId, AuthorAppUserId = userId,
            EntryType = type, Title = title,
            EventDateTime = eventDateTime,
            Visibility = CaseTimelineVisibility.OrgOnly,
            InvestigationId = investigationId,
            DateCreated = dateCreated ?? DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        await db.SaveChangesAsync();
        return id.Value;
    }

    private static async Task<List<CaseTimelineEntryRecord>> TimelineAsync(
        IDbContextFactory<BenDataContext> factory, Guid userId, Guid orgId, Guid caseId,
        Guid? investigationId = null)
    {
        var result = await Build(factory, userId).GetTimeline(orgId, caseId, investigationId, default);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return Assert.IsAssignableFrom<IEnumerable<CaseTimelineEntryRecord>>(ok.Value).ToList();
    }

    [Fact]
    public async Task Timeline_EntriesAtTheSameMoment_OrderedByWhoLoggedFirst()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var caseId = await CreateCaseAsync(factory, orgId, userId);

        // Two people reporting the same moment is normal, not an edge case. Both entries
        // carry the identical EventDateTime, so event time alone can't order them.
        var moment = new DateTime(2026, 3, 14, 21, 30, 0, DateTimeKind.Utc);

        // Seeded in the OPPOSITE order to DateCreated on purpose: a single-key sort leaves
        // ties in provider order (insertion order, under a stable sort), which would put
        // "Logged second" first. Only the DateCreated tiebreaker gets this right, so this
        // test fails against the old query rather than passing by luck.
        await SeedTimelineEntryAsync(factory, caseId, userId, "Logged second",
            eventDateTime: moment, dateCreated: moment.AddHours(2));
        await SeedTimelineEntryAsync(factory, caseId, userId, "Logged first",
            eventDateTime: moment, dateCreated: moment.AddHours(1));

        var titles = (await TimelineAsync(factory, userId, orgId, caseId))
            .Select(e => e.Title).ToList();

        Assert.Equal(["Logged first", "Logged second"], titles);
    }

    [Fact]
    public async Task Timeline_SameMomentOrdering_IsStableAcrossRequests()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var caseId = await CreateCaseAsync(factory, orgId, userId);
        var moment = new DateTime(2026, 3, 14, 21, 30, 0, DateTimeKind.Utc);

        // Same event time AND same DateCreated — a simultaneous double-report, where Id is
        // the only tiebreaker left. Seeded in descending Id order so insertion order can
        // never coincide with the expected ascending order: without ThenBy(Id) the stable
        // sort would hand back the seeded order and fail.
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        foreach (var id in ids.OrderByDescending(x => x))
            await SeedTimelineEntryAsync(factory, caseId, userId, $"Account {id:N}",
                eventDateTime: moment, dateCreated: moment, id: id);

        var returned = (await TimelineAsync(factory, userId, orgId, caseId)).Select(e => e.Id).ToList();

        // Asserts .NET's Guid ordering because these tests run on the InMemory provider;
        // SQL Server sorts uniqueidentifier differently, so the *sequence* would differ
        // there. The guarantee that matters is the same either way — some fixed total
        // order, so tied entries don't reshuffle between requests.
        Assert.Equal(ids.OrderBy(x => x).ToList(), returned);
    }

    [Fact]
    public async Task Timeline_FilteredByInvestigation_ReturnsOnlyThatBinder()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var caseId = (await CreateCaseAsync(factory, orgId, userId));
        var invA = await SeedInvestigationAsync(factory, caseId, userId, "Visit A");
        var invB = await SeedInvestigationAsync(factory, caseId, userId, "Visit B");
        await SeedTimelineEntryAsync(factory, caseId, userId, "From A", invA);
        await SeedTimelineEntryAsync(factory, caseId, userId, "From B", invB);
        await SeedTimelineEntryAsync(factory, caseId, userId, "Unattached", null);

        var binder = await TimelineAsync(factory, userId, orgId, caseId, invA);

        Assert.Equal(["From A"], binder.Select(e => e.Title));
    }

    [Fact]
    public async Task Timeline_Unfiltered_StillIncludesBinderEntries()
    {
        // The reason a binder reuses the timeline rather than a separate store: what an
        // investigator records during a visit belongs on the case's history automatically.
        var (factory, orgId, userId) = await SeedAsync();
        var caseId = await CreateCaseAsync(factory, orgId, userId);
        var invId = await SeedInvestigationAsync(factory, caseId, userId);
        await SeedTimelineEntryAsync(factory, caseId, userId, "During the visit", invId);
        await SeedTimelineEntryAsync(factory, caseId, userId, "Desk research", null);

        var all = await TimelineAsync(factory, userId, orgId, caseId);

        Assert.Contains(all, e => e.Title == "During the visit");
        Assert.Contains(all, e => e.Title == "Desk research");
    }

    [Fact]
    public async Task Timeline_EntryCarriesItsInvestigationId()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var caseId = await CreateCaseAsync(factory, orgId, userId);
        var invId = await SeedInvestigationAsync(factory, caseId, userId);
        await SeedTimelineEntryAsync(factory, caseId, userId, "Tagged", invId);
        await SeedTimelineEntryAsync(factory, caseId, userId, "Untagged", null);

        var all = await TimelineAsync(factory, userId, orgId, caseId);

        Assert.Equal(invId, Assert.Single(all, e => e.Title == "Tagged").InvestigationId);
        Assert.Null(Assert.Single(all, e => e.Title == "Untagged").InvestigationId);
    }

    [Fact]
    public async Task AddTimelineEntry_CanRecordAnInstrumentReadingAgainstAnInvestigation()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var caseId = await CreateCaseAsync(factory, orgId, userId);
        var invId = await SeedInvestigationAsync(factory, caseId, userId);

        var result = await Build(factory, userId).AddTimelineEntry(orgId, caseId,
            new UpsertTimelineEntryRequest(CaseTimelineEntryType.InstrumentReading,
                DateTime.UtcNow, "EMF spike", "<p>4.2 mG at the stairwell.</p>",
                CaseTimelineVisibility.OrgOnly, [], invId), default);

        var created = Assert.IsType<CaseTimelineEntryRecord>(
            Assert.IsType<CreatedAtActionResult>(result.Result).Value);
        Assert.Equal(CaseTimelineEntryType.InstrumentReading, created.EntryType);
        Assert.Equal(invId, created.InvestigationId);
    }

    /// <summary>Creates a case directly, avoiding the CMS-page side effects of the Create endpoint.</summary>
    private static async Task<Guid> CreateCaseAsync(
        IDbContextFactory<BenDataContext> factory, Guid orgId, Guid userId)
    {
        var caseId = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        db.Cases.Add(new Case
        {
            Id = caseId, OrganizationId = orgId, Title = "Binder case",
            CaseYear = DateTime.UtcNow.Year, OrgCaseNumber = 9,
            StreetAddress1 = "1 Main", City = "Nashville", State = "TN",
            ZipCode = "37201", Country = "US",
            DateCaseOpened = DateTime.UtcNow, DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = userId,
        });
        await db.SaveChangesAsync();
        return caseId;
    }

    // ── Readable case addresses (item #89) ───────────────────────────────────

    private static async Task<Guid> SeedCaseForPublishingAsync(
        IDbContextFactory<BenDataContext> factory, Guid orgId, Guid userId, string title)
    {
        var caseId = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        db.Cases.Add(new Case
        {
            Id = caseId, OrganizationId = orgId, Title = title,
            CaseYear = 2026, OrgCaseNumber = 7,
            StreetAddress1 = "1 Main", City = "Nashville", State = "TN",
            ZipCode = "37201", Country = "US",
            DateCaseOpened = DateTime.UtcNow, DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = userId,
        });
        await db.SaveChangesAsync();
        return caseId;
    }

    [Fact]
    public async Task Publishing_a_case_gives_it_a_readable_address()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var caseId = await SeedCaseForPublishingAsync(factory, orgId, userId, "The Mill House Investigation");

        var result = await Build(factory, userId, isAdmin: true).Update(
            orgId, caseId,
            new UpdateCaseRequest("The Mill House Investigation", null, CaseStatus.Public, "A Client", true, null),
            default);
        Assert.IsType<OkObjectResult>(result.Result);

        await using var db = await factory.CreateDbContextAsync();
        var saved = await db.Cases.AsNoTracking().SingleAsync(c => c.Id == caseId);
        Assert.Equal("the-mill-house-investigation", saved.UrlName);
    }

    /// <summary>
    /// A private case has no address to promise, so it does not get one.
    /// </summary>
    [Fact]
    public async Task An_unpublished_case_has_no_address()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var caseId = await SeedCaseForPublishingAsync(factory, orgId, userId, "The Mill House");

        await Build(factory, userId, isAdmin: true).Update(
            orgId, caseId,
            new UpdateCaseRequest("The Mill House", null, CaseStatus.Active, null, false, null),
            default);

        await using var db = await factory.CreateDbContextAsync();
        Assert.Null((await db.Cases.AsNoTracking().SingleAsync(c => c.Id == caseId)).UrlName);
    }

    /// <summary>
    /// The title becomes part of a public URL, and a URL outlives the page it points at — it sits
    /// in browser histories, referrer headers and pasted links. Publishing a case whose title is a
    /// street address is refused rather than quietly slugged.
    /// </summary>
    [Fact]
    public async Task Publishing_a_case_titled_with_a_street_address_is_refused()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var caseId = await SeedCaseForPublishingAsync(factory, orgId, userId, "42 Elm Street Hauntings");

        var result = await Build(factory, userId, isAdmin: true).Update(
            orgId, caseId,
            new UpdateCaseRequest("42 Elm Street Hauntings", null, CaseStatus.Public, "A Client", true, null),
            default);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        var message = Assert.IsType<string>(bad.Value);
        Assert.Contains("street address", message, StringComparison.OrdinalIgnoreCase);

        await using var db = await factory.CreateDbContextAsync();
        Assert.Null((await db.Cases.AsNoTracking().SingleAsync(c => c.Id == caseId)).UrlName);
    }

    /// <summary>
    /// An address is a promise. Renaming the case afterwards leaves it alone, or every link
    /// somebody has already shared breaks.
    /// </summary>
    [Fact]
    public async Task Renaming_a_published_case_does_not_change_its_address()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var caseId = await SeedCaseForPublishingAsync(factory, orgId, userId, "The Mill House");

        var ctrl = Build(factory, userId, isAdmin: true);
        await ctrl.Update(orgId, caseId,
            new UpdateCaseRequest("The Mill House", null, CaseStatus.Public, "A Client", true, null), default);

        await ctrl.Update(orgId, caseId,
            new UpdateCaseRequest("The Mill House (revisited)", null, CaseStatus.Public, "A Client", true, null), default);

        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal("the-mill-house", (await db.Cases.AsNoTracking().SingleAsync(c => c.Id == caseId)).UrlName);
    }

    /// <summary>Two cases with the same title in one group get distinct addresses.</summary>
    [Fact]
    public async Task A_second_case_with_the_same_title_gets_its_own_address()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var ctrl = Build(factory, userId, isAdmin: true);

        var first  = await SeedCaseForPublishingAsync(factory, orgId, userId, "The Mill House");
        await ctrl.Update(orgId, first,
            new UpdateCaseRequest("The Mill House", null, CaseStatus.Public, "A Client", true, null), default);

        var secondId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Cases.Add(new Case
            {
                Id = secondId, OrganizationId = orgId, Title = "The Mill House",
                CaseYear = 2026, OrgCaseNumber = 8,
                StreetAddress1 = "2 Main", City = "Nashville", State = "TN",
                ZipCode = "37201", Country = "US",
                DateCaseOpened = DateTime.UtcNow, DateCreated = DateTime.UtcNow,
                CreatedByAppUserId = userId,
            });
            await db.SaveChangesAsync();
        }

        await ctrl.Update(orgId, secondId,
            new UpdateCaseRequest("The Mill House", null, CaseStatus.Public, "A Client", true, null), default);

        await using var check = await factory.CreateDbContextAsync();
        var slugs = await check.Cases.AsNoTracking()
            .Where(c => c.UrlName != null).Select(c => c.UrlName!).ToListAsync();

        Assert.Equal(2, slugs.Count);
        Assert.Equal(2, slugs.Distinct().Count());
    }

    /// <summary>
    /// The public address is cut from the PUBLIC title. A case whose private title carries the
    /// client's surname — the accept dialog builds exactly that by default — used to publish with
    /// the pseudonym on the page and the surname in the URL (2026-09-06 evaluation, W-P1).
    /// </summary>
    [Fact]
    public async Task Publishing_never_puts_the_clients_name_in_the_address()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var caseId = Guid.NewGuid(); var clientId = Guid.NewGuid(); var requestId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Users.Add(new AppUser
            {
                Id = clientId, UserName = "c@t.com", NormalizedUserName = "C@T.COM", Email = "c@t.com", NormalizedEmail = "C@T.COM",
                FirstName = "Casey", LastName = "Evaluator", DisplayName = "Casey Evaluator", DateCreated = DateTime.UtcNow,
            });
            db.ClientRequests.Add(new ClientRequest
            {
                Id = requestId, AppUserId = clientId, Status = ClientRequestStatus.Assigned,
                City = "Nashville", State = "TN", ZipCode = "37203",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = clientId,
            });
            db.Cases.Add(new Case
            {
                Id = caseId, OrganizationId = orgId, Title = "Evaluator, Nashville TN",
                CaseYear = 2026, OrgCaseNumber = 5, ClientRequestId = requestId, IsPrivateEngagement = true,
                StreetAddress1 = "2500 West End Ave", City = "Nashville", State = "TN", ZipCode = "37203", Country = "US",
                DateCaseOpened = DateTime.UtcNow, DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
            });
            await db.SaveChangesAsync();
        }

        var result = await Build(factory, userId, isAdmin: true).Update(
            orgId, caseId,
            new UpdateCaseRequest("Evaluator, Nashville TN", null, CaseStatus.Public, "The Westside Family", true, null),
            default);
        Assert.IsType<OkObjectResult>(result.Result);

        await using var db2 = await factory.CreateDbContextAsync();
        var saved = await db2.Cases.AsNoTracking().SingleAsync(c => c.Id == caseId);
        Assert.NotNull(saved.UrlName);
        Assert.DoesNotContain("evaluator", saved.UrlName, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("casey", saved.UrlName, StringComparison.OrdinalIgnoreCase);
    }
}
