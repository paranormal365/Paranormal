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
                ? new CaseTimelineEntryRecord { Id = e.Id, CaseId = e.CaseId, AuthorAppUserId = e.AuthorAppUserId, EntryType = e.EntryType, Title = e.Title, Body = e.Body, IsPublic = e.IsPublic, DateCreated = e.DateCreated }
                : new CaseTimelineEntryRecord { DateCreated = DateTime.UtcNow });
        m.Setup(x => x.Map<IEnumerable<CaseTimelineEntryRecord>>(It.IsAny<object>()))
            .Returns<object>(o => o is IEnumerable<CaseTimelineEntry> list
                ? list.Select(e => new CaseTimelineEntryRecord { Id = e.Id, CaseId = e.CaseId, EntryType = e.EntryType, Title = e.Title, DateCreated = e.DateCreated })
                : []);
        return m.Object;
    }

    private static CaseController Build(IDbContextFactory<BenDataContext> factory, Guid userId, bool isAdmin = false, bool isSuperAdmin = false)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
        if (isSuperAdmin) claims.Add(new Claim(ClaimTypes.Role, RoleNames.SuperAdmin));
        var ctrl = new CaseController(factory, CreateMapper());
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
        var ok = Assert.IsType<OkObjectResult>((await ctrl.GetTimeline(orgId, caseId, default)).Result);
        Assert.Empty((IEnumerable<CaseTimelineEntryRecord>)ok.Value!);
    }

    [Fact]
    public async Task AddTimelineEntry_CreatesEntry()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var ctrl   = Build(factory, userId);
        var caseId = ((CaseRecord)((CreatedAtActionResult)(await ctrl.Create(orgId, MakeCreateRequest(), default)).Result!).Value!).Id;

        var req    = new UpsertTimelineEntryRequest(CaseTimelineEntryType.Evidence, DateTime.UtcNow, "Strange noise", "Heard in basement", true, []);
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

        var req     = new UpsertTimelineEntryRequest(CaseTimelineEntryType.Evidence, null, "To delete", null, false, []);
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
        var entryId = ((CaseTimelineEntryRecord)((CreatedAtActionResult)(await admin.AddTimelineEntry(orgId, caseId, new UpsertTimelineEntryRequest(CaseTimelineEntryType.Evidence, null, "X", null, false, []), default)).Result!).Value!).Id;

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
        var entryId = ((CaseTimelineEntryRecord)((CreatedAtActionResult)(await victim.AddTimelineEntry(victimOrgId, caseId, new UpsertTimelineEntryRequest(CaseTimelineEntryType.Evidence, null, "Private", null, false, []), default)).Result!).Value!).Id;

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
            new UpsertTimelineEntryRequest(CaseTimelineEntryType.Evidence, null, "Hijacked", null, false, []), default);
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
}
