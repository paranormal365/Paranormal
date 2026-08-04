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
/// Tests for InvestigationController — scheduling, CRUD, cancel, and attendee management.
/// </summary>
public class InvestigationControllerTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var options = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PooledDbContextFactory<BenDataContext>(options);
    }

    private static IMapper CreateMapper()
    {
        var m = new Mock<IMapper>();
        m.Setup(x => x.Map<InvestigationRecord>(It.IsAny<object>()))
            .Returns<object>(o => o is Investigation inv
                ? new InvestigationRecord { Id = inv.Id, CaseId = inv.CaseId, Title = inv.Title, Status = inv.Status, ScheduledDateTime = inv.ScheduledDateTime, CreatedByAppUserId = inv.CreatedByAppUserId }
                : new InvestigationRecord { Title = "", ScheduledDateTime = DateTime.UtcNow, CreatedByAppUserId = Guid.Empty });
        m.Setup(x => x.Map<IEnumerable<InvestigationRecord>>(It.IsAny<object>()))
            .Returns<object>(o => o is IEnumerable<Investigation> list
                ? list.Select(inv => new InvestigationRecord { Id = inv.Id, CaseId = inv.CaseId, Title = inv.Title, Status = inv.Status, ScheduledDateTime = inv.ScheduledDateTime, CreatedByAppUserId = inv.CreatedByAppUserId })
                : []);
        m.Setup(x => x.Map<InvestigationAttendeeRecord>(It.IsAny<object>()))
            .Returns<object>(o => o is InvestigationAttendee a
                ? new InvestigationAttendeeRecord { Id = a.Id, InvestigationId = a.InvestigationId, AppUserId = a.AppUserId, AssignedRole = a.AssignedRole, DidAttend = a.DidAttend, DateCreated = a.DateCreated, CreatedByAppUserId = a.CreatedByAppUserId }
                : new InvestigationAttendeeRecord { DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.Empty });
        m.Setup(x => x.Map<IEnumerable<InvestigationAttendeeRecord>>(It.IsAny<object>()))
            .Returns<object>(o => o is IEnumerable<InvestigationAttendee> list
                ? list.Select(a => new InvestigationAttendeeRecord { Id = a.Id, InvestigationId = a.InvestigationId, AppUserId = a.AppUserId, AssignedRole = a.AssignedRole, DidAttend = a.DidAttend, DateCreated = a.DateCreated, CreatedByAppUserId = a.CreatedByAppUserId })
                : []);
        return m.Object;
    }

    private static InvestigationController BuildController(IDbContextFactory<BenDataContext> factory, Guid userId)
    {
        var ctrl = new InvestigationController(factory, CreateMapper());
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Bearer"))
            }
        };
        return ctrl;
    }

    private static async Task<(IDbContextFactory<BenDataContext> factory, Guid orgId, Guid caseId, Guid userId, AppUser member)> SeedAsync()
    {
        var factory = CreateFactory();
        var orgId   = Guid.NewGuid();
        var caseId  = Guid.NewGuid();
        var userId  = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();
        var user = new AppUser { Id = userId, UserName = "member@test.com", NormalizedUserName = "MEMBER@TEST.COM", Email = "member@test.com", NormalizedEmail = "MEMBER@TEST.COM", DateCreated = DateTime.UtcNow };
        db.Users.Add(user);
        db.Organizations.Add(new Organization { Id = orgId, Name = "Test Org", UrlName = "test", DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId });
        db.OrganizationUserMemberships.Add(new OrganizationUserMembership
        {
            Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = userId,
            Role = OrganizationMemberRole.Manager, IsActive = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        db.Cases.Add(new Case
        {
            Id = caseId, OrganizationId = orgId, Title = "Test Case",
            CaseYear = 2026, OrgCaseNumber = 1,
            StreetAddress1 = "123 Main", City = "Nashville", State = "TN", ZipCode = "37201", Country = "US",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        await db.SaveChangesAsync();
        return (factory, orgId, caseId, userId, user);
    }

    private static UpsertInvestigationRequest MakeRequest(string title = "Night Visit #1") =>
        new(title, "Description", "Basement", DateTime.UtcNow.AddDays(7), null,
            InvestigationStatus.Scheduled, null, null);

    // ── GetAll ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_NonMember_ReturnsForbid()
    {
        var (factory, orgId, caseId, _, _) = await SeedAsync();
        var ctrl = BuildController(factory, Guid.NewGuid());

        var result = await ctrl.GetAll(orgId, caseId, default);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task GetAll_Member_ReturnsEmptyList()
    {
        var (factory, orgId, caseId, userId, _) = await SeedAsync();
        var ctrl = BuildController(factory, userId);

        var result = await ctrl.GetAll(orgId, caseId, default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsAssignableFrom<IEnumerable<InvestigationRecord>>(ok.Value);
        Assert.Empty(list);
    }

    // ── Create ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_ValidRequest_ReturnsCreated()
    {
        var (factory, orgId, caseId, userId, _) = await SeedAsync();
        var ctrl = BuildController(factory, userId);

        var result = await ctrl.Create(orgId, caseId, MakeRequest(), default);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var dto = Assert.IsType<InvestigationRecord>(created.Value);
        Assert.Equal("Night Visit #1", dto.Title);
        Assert.Equal(caseId, dto.CaseId);
    }

    [Fact]
    public async Task Create_AutoCreatesCalendarEvent()
    {
        var (factory, orgId, caseId, userId, _) = await SeedAsync();
        var ctrl = BuildController(factory, userId);

        await ctrl.Create(orgId, caseId, MakeRequest(), default);

        await using var db = await factory.CreateDbContextAsync();
        Assert.True(await db.OrgCalendarEvents.AnyAsync(e => e.CaseId == caseId));
    }

    [Fact]
    public async Task Create_CaseNotFound_ReturnsNotFound()
    {
        var (factory, orgId, _, userId, _) = await SeedAsync();
        var ctrl = BuildController(factory, userId);

        var result = await ctrl.Create(orgId, Guid.NewGuid(), MakeRequest(), default);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task Create_NonMember_ReturnsForbid()
    {
        var (factory, orgId, caseId, _, _) = await SeedAsync();
        var ctrl = BuildController(factory, Guid.NewGuid());

        var result = await ctrl.Create(orgId, caseId, MakeRequest(), default);

        Assert.IsType<ForbidResult>(result.Result);
    }

    // ── Update ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_ExistingInvestigation_ReturnsOk()
    {
        var (factory, orgId, caseId, userId, _) = await SeedAsync();
        var ctrl   = BuildController(factory, userId);
        var create = await ctrl.Create(orgId, caseId, MakeRequest(), default);
        var invId  = ((InvestigationRecord)((CreatedAtActionResult)create.Result!).Value!).Id;

        var updated = MakeRequest("Night Visit Updated") with { Status = InvestigationStatus.Completed };
        var result  = await ctrl.Update(orgId, caseId, invId, updated, default);

        var ok  = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<InvestigationRecord>(ok.Value);
        Assert.Equal("Night Visit Updated", dto.Title);
        Assert.Equal(InvestigationStatus.Completed, dto.Status);
    }

    [Fact]
    public async Task Update_MissingId_ReturnsNotFound()
    {
        var (factory, orgId, caseId, userId, _) = await SeedAsync();
        var ctrl = BuildController(factory, userId);

        var result = await ctrl.Update(orgId, caseId, Guid.NewGuid(), MakeRequest(), default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_ExistingInvestigation_ReturnsNoContent()
    {
        var (factory, orgId, caseId, userId, _) = await SeedAsync();
        var ctrl   = BuildController(factory, userId);
        var create = await ctrl.Create(orgId, caseId, MakeRequest(), default);
        var invId  = ((InvestigationRecord)((CreatedAtActionResult)create.Result!).Value!).Id;

        var result = await ctrl.Delete(orgId, caseId, invId, default);

        Assert.IsType<NoContentResult>(result);
        await using var db = await factory.CreateDbContextAsync();
        Assert.False(await db.Investigations.AnyAsync(i => i.Id == invId));
    }

    [Fact]
    public async Task Delete_MissingId_ReturnsNotFound()
    {
        var (factory, orgId, caseId, userId, _) = await SeedAsync();
        var ctrl = BuildController(factory, userId);

        var result = await ctrl.Delete(orgId, caseId, Guid.NewGuid(), default);

        Assert.IsType<NotFoundResult>(result);
    }

    // ── Cancel ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cancel_ScheduledInvestigation_SetsStatusAndPostsMessage()
    {
        var (factory, orgId, caseId, userId, _) = await SeedAsync();
        var ctrl   = BuildController(factory, userId);
        var create = await ctrl.Create(orgId, caseId, MakeRequest(), default);
        var invId  = ((InvestigationRecord)((CreatedAtActionResult)create.Result!).Value!).Id;

        var result = await ctrl.Cancel(orgId, caseId, invId, default);

        Assert.IsType<NoContentResult>(result);
        await using var db = await factory.CreateDbContextAsync();
        var inv = await db.Investigations.FindAsync(invId);
        Assert.Equal(InvestigationStatus.Cancelled, inv!.Status);
        Assert.True(await db.CaseMessages.AnyAsync(m => m.CaseId == caseId));
    }

    [Fact]
    public async Task Cancel_AlreadyCancelled_ReturnsConflict()
    {
        var (factory, orgId, caseId, userId, _) = await SeedAsync();
        var ctrl   = BuildController(factory, userId);
        var create = await ctrl.Create(orgId, caseId, MakeRequest(), default);
        var invId  = ((InvestigationRecord)((CreatedAtActionResult)create.Result!).Value!).Id;
        await ctrl.Cancel(orgId, caseId, invId, default);

        var result = await ctrl.Cancel(orgId, caseId, invId, default);

        Assert.IsType<ConflictObjectResult>(result);
    }

    // ── Attendees ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddAttendee_ValidUser_ReturnsCreated()
    {
        var (factory, orgId, caseId, userId, member) = await SeedAsync();
        var ctrl   = BuildController(factory, userId);
        var create = await ctrl.Create(orgId, caseId, MakeRequest(), default);
        var invId  = ((InvestigationRecord)((CreatedAtActionResult)create.Result!).Value!).Id;

        var result = await ctrl.AddAttendee(orgId, caseId, invId,
            new AddInvestigationAttendeeRequest(member.Id, "Lead Investigator"), default);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var dto = Assert.IsType<InvestigationAttendeeRecord>(created.Value);
        Assert.Equal(member.Id, dto.AppUserId);
        Assert.Equal("Lead Investigator", dto.AssignedRole);
    }

    [Fact]
    public async Task AddAttendee_MissingInvestigation_ReturnsNotFound()
    {
        var (factory, orgId, caseId, userId, member) = await SeedAsync();
        var ctrl = BuildController(factory, userId);

        var result = await ctrl.AddAttendee(orgId, caseId, Guid.NewGuid(),
            new AddInvestigationAttendeeRequest(member.Id, null), default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetAttendees_ReturnsSeededAttendee()
    {
        var (factory, orgId, caseId, userId, member) = await SeedAsync();
        var ctrl   = BuildController(factory, userId);
        var create = await ctrl.Create(orgId, caseId, MakeRequest(), default);
        var invId  = ((InvestigationRecord)((CreatedAtActionResult)create.Result!).Value!).Id;
        await ctrl.AddAttendee(orgId, caseId, invId, new AddInvestigationAttendeeRequest(member.Id, "Investigator"), default);

        var result = await ctrl.GetAttendees(orgId, caseId, invId, default);

        var ok   = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsAssignableFrom<IEnumerable<InvestigationAttendeeRecord>>(ok.Value);
        Assert.Single(list);
    }

    [Fact]
    public async Task UpdateAttendance_MarksAttended()
    {
        var (factory, orgId, caseId, userId, member) = await SeedAsync();
        var ctrl   = BuildController(factory, userId);
        var create = await ctrl.Create(orgId, caseId, MakeRequest(), default);
        var invId  = ((InvestigationRecord)((CreatedAtActionResult)create.Result!).Value!).Id;
        var addResult = await ctrl.AddAttendee(orgId, caseId, invId, new AddInvestigationAttendeeRequest(member.Id, null), default);
        var attendeeId = ((InvestigationAttendeeRecord)((CreatedAtActionResult)addResult.Result!).Value!).Id;

        var result = await ctrl.UpdateAttendance(orgId, caseId, invId, attendeeId,
            new UpdateAttendanceRequest(true, "Lead", RsvpStatus.Accepted), default);

        var ok  = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<InvestigationAttendeeRecord>(ok.Value);
        Assert.True(dto.DidAttend);
        Assert.Equal("Lead", dto.AssignedRole);
    }

    [Fact]
    public async Task RemoveAttendee_DeletesRow()
    {
        var (factory, orgId, caseId, userId, member) = await SeedAsync();
        var ctrl   = BuildController(factory, userId);
        var create = await ctrl.Create(orgId, caseId, MakeRequest(), default);
        var invId  = ((InvestigationRecord)((CreatedAtActionResult)create.Result!).Value!).Id;
        var addResult = await ctrl.AddAttendee(orgId, caseId, invId, new AddInvestigationAttendeeRequest(member.Id, null), default);
        var attendeeId = ((InvestigationAttendeeRecord)((CreatedAtActionResult)addResult.Result!).Value!).Id;

        var result = await ctrl.RemoveAttendee(orgId, caseId, invId, attendeeId, default);

        Assert.IsType<NoContentResult>(result);
        await using var db = await factory.CreateDbContextAsync();
        Assert.False(await db.InvestigationAttendees.AnyAsync(a => a.Id == attendeeId));
    }

    [Fact]
    public async Task RemoveAttendee_MissingId_ReturnsNotFound()
    {
        var (factory, orgId, caseId, userId, _) = await SeedAsync();
        var ctrl   = BuildController(factory, userId);
        var create = await ctrl.Create(orgId, caseId, MakeRequest(), default);
        var invId  = ((InvestigationRecord)((CreatedAtActionResult)create.Result!).Value!).Id;

        var result = await ctrl.RemoveAttendee(orgId, caseId, invId, Guid.NewGuid(), default);

        Assert.IsType<NotFoundResult>(result);
    }
}
