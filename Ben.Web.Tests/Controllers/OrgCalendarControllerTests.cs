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
/// Tests for OrgCalendarEventController — calendar event CRUD and attendee management.
/// OrgCalendarController.cs also contains OrgCalendarEventTypeController; both are tested here.
/// </summary>
public class OrgCalendarControllerTests
{
    // Non-pooled: Create/Update use FirstAsync with optional Includes (EventType, Case, Attendees)
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
        m.Setup(x => x.Map<OrgCalendarEventRecord>(It.IsAny<object>()))
            .Returns<object>(o => o is OrgCalendarEvent e
                ? new OrgCalendarEventRecord { Id = e.Id, OrganizationId = e.OrganizationId, Title = e.Title, StartDateTime = e.StartDateTime, EndDateTime = e.EndDateTime, IsAllDay = e.IsAllDay, IsPublic = e.IsPublic, DateCreated = e.DateCreated }
                : new OrgCalendarEventRecord { Title = "", StartDateTime = DateTime.UtcNow, EndDateTime = DateTime.UtcNow, DateCreated = DateTime.UtcNow });
        m.Setup(x => x.Map<IEnumerable<OrgCalendarEventRecord>>(It.IsAny<object>()))
            .Returns<object>(o => o is IEnumerable<OrgCalendarEvent> list
                ? list.Select(e => new OrgCalendarEventRecord { Id = e.Id, OrganizationId = e.OrganizationId, Title = e.Title, StartDateTime = e.StartDateTime, EndDateTime = e.EndDateTime, DateCreated = e.DateCreated })
                : []);
        m.Setup(x => x.Map<OrgCalendarEventAttendeeRecord>(It.IsAny<object>()))
            .Returns<object>(o => o is OrgCalendarEventAttendee a
                ? new OrgCalendarEventAttendeeRecord { Id = a.Id, OrgCalendarEventId = a.OrgCalendarEventId, AppUserId = a.AppUserId, RsvpStatus = a.RsvpStatus, DateCreated = a.DateCreated }
                : new OrgCalendarEventAttendeeRecord { DateCreated = DateTime.UtcNow });
        m.Setup(x => x.Map<IEnumerable<OrgCalendarEventAttendeeRecord>>(It.IsAny<object>()))
            .Returns<object>(o => o is IEnumerable<OrgCalendarEventAttendee> list
                ? list.Select(a => new OrgCalendarEventAttendeeRecord { Id = a.Id, OrgCalendarEventId = a.OrgCalendarEventId, AppUserId = a.AppUserId, RsvpStatus = a.RsvpStatus, DateCreated = a.DateCreated })
                : []);
        return m.Object;
    }

    private static OrgCalendarEventController Build(IDbContextFactory<BenDataContext> factory, Guid userId)
    {
        var ctrl = new OrgCalendarEventController(factory, CreateMapper());
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

    private static async Task<(IDbContextFactory<BenDataContext>, Guid orgId, Guid userId)> SeedAsync(bool makeAdmin = true)
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

    private static UpsertCalendarEventRequest MakeEventRequest(string title = "Test Event") =>
        new(title, null, null, DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(1).AddHours(2), false, false, null, null, null);

    // ── GetAll ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_NonMember_ReturnsForbid()
    {
        var (factory, orgId, _) = await SeedAsync();
        var ctrl = Build(factory, Guid.NewGuid());
        Assert.IsType<ForbidResult>((await ctrl.GetAll(orgId, null, null, default)).Result);
    }

    [Fact]
    public async Task GetAll_Member_ReturnsEmptyList()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var ctrl = Build(factory, userId);
        var ok = Assert.IsType<OkObjectResult>((await ctrl.GetAll(orgId, null, null, default)).Result);
        Assert.Empty((IEnumerable<OrgCalendarEventRecord>)ok.Value!);
    }

    // ── Create ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_Member_ReturnsCreated()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var ctrl   = Build(factory, userId);
        var result = await ctrl.Create(orgId, MakeEventRequest("Team Meeting"), default);
        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var dto = Assert.IsType<OrgCalendarEventRecord>(created.Value);
        Assert.Equal("Team Meeting", dto.Title);
    }

    [Fact]
    public async Task Create_NonMember_ReturnsForbid()
    {
        var (factory, orgId, _) = await SeedAsync();
        var ctrl = Build(factory, Guid.NewGuid());
        Assert.IsType<ForbidResult>((await ctrl.Create(orgId, MakeEventRequest(), default)).Result);
    }

    // ── Update ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_ExistingEvent_ReturnsUpdated()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var ctrl    = Build(factory, userId);
        var eventId = ((OrgCalendarEventRecord)((CreatedAtActionResult)(await ctrl.Create(orgId, MakeEventRequest(), default)).Result!).Value!).Id;

        var result = await ctrl.Update(orgId, eventId, MakeEventRequest("Updated Event"), default);
        var ok  = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<OrgCalendarEventRecord>(ok.Value);
        Assert.Equal("Updated Event", dto.Title);
    }

    [Fact]
    public async Task Update_MissingEvent_ReturnsNotFound()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var ctrl = Build(factory, userId);
        Assert.IsType<NotFoundResult>((await ctrl.Update(orgId, Guid.NewGuid(), MakeEventRequest(), default)).Result);
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_ExistingEvent_ReturnsNoContent()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var ctrl    = Build(factory, userId);
        var eventId = ((OrgCalendarEventRecord)((CreatedAtActionResult)(await ctrl.Create(orgId, MakeEventRequest(), default)).Result!).Value!).Id;

        Assert.IsType<NoContentResult>(await ctrl.Delete(orgId, eventId, default));
        await using var db = await factory.CreateDbContextAsync();
        Assert.False(await db.OrgCalendarEvents.AnyAsync(e => e.Id == eventId));
    }

    [Fact]
    public async Task Delete_MissingEvent_ReturnsNotFound()
    {
        var (factory, orgId, userId) = await SeedAsync();
        Assert.IsType<NotFoundResult>(await Build(factory, userId).Delete(orgId, Guid.NewGuid(), default));
    }

    // ── Attendees ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddAttendee_ReturnsCreated()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var ctrl    = Build(factory, userId);
        var eventId = ((OrgCalendarEventRecord)((CreatedAtActionResult)(await ctrl.Create(orgId, MakeEventRequest(), default)).Result!).Value!).Id;

        var result  = await ctrl.AddAttendee(orgId, eventId, new AddAttendeeRequest(userId, "Lead"), default);
        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var dto     = Assert.IsType<OrgCalendarEventAttendeeRecord>(created.Value);
        Assert.Equal(userId, dto.AppUserId);
        Assert.Equal(RsvpStatus.Invited, dto.RsvpStatus);
    }

    [Fact]
    public async Task Rsvp_UpdatesAttendeeStatus()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var ctrl    = Build(factory, userId);
        var eventId = ((OrgCalendarEventRecord)((CreatedAtActionResult)(await ctrl.Create(orgId, MakeEventRequest(), default)).Result!).Value!).Id;
        var attendeeId = ((OrgCalendarEventAttendeeRecord)((CreatedAtActionResult)(await ctrl.AddAttendee(orgId, eventId, new AddAttendeeRequest(userId, null), default)).Result!).Value!).Id;

        var result = await ctrl.Rsvp(orgId, eventId, attendeeId, new RsvpRequest(RsvpStatus.Accepted), default);
        var ok  = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<OrgCalendarEventAttendeeRecord>(ok.Value);
        Assert.Equal(RsvpStatus.Accepted, dto.RsvpStatus);
    }

    [Fact]
    public async Task RemoveAttendee_AdminCanRemove()
    {
        var (factory, orgId, userId) = await SeedAsync();
        var ctrl    = Build(factory, userId);
        var eventId = ((OrgCalendarEventRecord)((CreatedAtActionResult)(await ctrl.Create(orgId, MakeEventRequest(), default)).Result!).Value!).Id;
        var attendeeId = ((OrgCalendarEventAttendeeRecord)((CreatedAtActionResult)(await ctrl.AddAttendee(orgId, eventId, new AddAttendeeRequest(userId, null), default)).Result!).Value!).Id;

        Assert.IsType<NoContentResult>(await ctrl.RemoveAttendee(orgId, eventId, attendeeId, default));
        await using var db = await factory.CreateDbContextAsync();
        Assert.False(await db.OrgCalendarEventAttendees.AnyAsync(a => a.Id == attendeeId));
    }

    [Fact]
    public async Task GetAttendees_EventBelongsToDifferentOrg_ReturnsNotFound()
    {
        // The core of the fix: GetAttendees checked org membership but never that eventId
        // actually belonged to the route orgId (unlike its own Delete/AddAttendee siblings).
        var (factory, victimOrgId, victimUserId) = await SeedAsync();
        var victim  = Build(factory, victimUserId);
        var eventId = ((OrgCalendarEventRecord)((CreatedAtActionResult)(await victim.Create(victimOrgId, MakeEventRequest(), default)).Result!).Value!).Id;

        var attackerOrgId = Guid.NewGuid();
        var attackerId    = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Organizations.Add(new Organization { Id = attackerOrgId, Name = "Attacker Org", UrlName = "attacker", DateCreated = DateTime.UtcNow, CreatedByAppUserId = attackerId });
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership { Id = Guid.NewGuid(), OrganizationId = attackerOrgId, AppUserId = attackerId, Role = OrganizationMemberRole.Owner, IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = attackerId });
            await db.SaveChangesAsync();
        }
        var attacker = Build(factory, attackerId);

        var result = await attacker.GetAttendees(attackerOrgId, eventId, default);

        Assert.IsType<NotFoundResult>(result.Result);
    }
}
