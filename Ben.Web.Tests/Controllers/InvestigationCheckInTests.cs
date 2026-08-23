using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Arrival check-in, and the provenance that makes it worth having.
/// </summary>
/// <remarks>
/// "Checked in from the site at 21:04" and "a manager ticked a box the following Tuesday" are
/// different grades of evidence. A single <c>DidAttend</c> boolean cannot tell them apart, so
/// check-in is its own endpoint and leaves <c>AttendanceRecordedByAppUserId</c> null, while the
/// override stamps whoever made the call. Most of these tests exist to keep that distinction real.
/// </remarks>
public class InvestigationCheckInTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid CreatorId = Guid.NewGuid();
    private static readonly Guid AttendeeId = Guid.NewGuid();
    private static readonly Guid OtherMemberId = Guid.NewGuid();

    private sealed record World(
        IDbContextFactory<BenDataContext> Factory, Guid InvestigationId, Guid AttendeeRowId);

    private static OrgInvestigationsController Build(IDbContextFactory<BenDataContext> f, Guid userId)
        => new(f, new Mock<IMapper>().Object, new Mock<IAuditLogService>().Object, new Ben.Service.RepositoryService.Services.OrganizationSecurityService(f))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Bearer"))
                }
            }
        };

    private static async Task<World> SeedAsync()
    {
        var factory = TestDbFactory.Create();
        var invId = Guid.NewGuid();
        var attendeeRow = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();

        db.Organizations.Add(new Organization
        { Id = OrgId, Name = "BenCo", UrlName = "benco", DateCreated = DateTime.UtcNow });

        foreach (var (id, name) in new[]
                 {
                     (CreatorId, "The Creator"),
                     (AttendeeId, "An Attendee"),
                     (OtherMemberId, "Another Member"),
                 })
        {
            db.Users.Add(new AppUser
            { Id = id, UserName = $"{id:N}@t", Email = $"{id:N}@t", DisplayName = name });
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = OrgId, AppUserId = id,
                Role = OrganizationMemberRole.Member, IsActive = true, DateCreated = DateTime.UtcNow,
            });
        }

        db.Investigations.Add(new Investigation
        {
            Id = invId, OrganizationId = OrgId, Title = "Night visit",
            ScheduledDateTime = DateTime.UtcNow.AddDays(-1),
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = CreatorId,
        });
        db.InvestigationAttendees.Add(new InvestigationAttendee
        {
            Id = attendeeRow, InvestigationId = invId, AppUserId = AttendeeId,
            Rsvp = RsvpStatus.Accepted, DateCreated = DateTime.UtcNow, CreatedByAppUserId = CreatorId,
        });

        await db.SaveChangesAsync();
        await TestSeeds.BridgeAsync(factory, OrgId);
        return new World(factory, invId, attendeeRow);
    }

    private static async Task<InvestigationAttendee> RowAsync(World w)
    {
        await using var db = await w.Factory.CreateDbContextAsync();
        return await db.InvestigationAttendees.FirstAsync(a => a.Id == w.AttendeeRowId);
    }

    // ── Provenance ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Checking_yourself_in_leaves_the_record_self_reported()
    {
        var w = await SeedAsync();

        var result = await Build(w.Factory, AttendeeId).CheckIn(
            OrgId, w.InvestigationId, new CheckInRequest(), default);

        Assert.IsType<OkObjectResult>(result.Result);

        var row = await RowAsync(w);
        Assert.True(row.DidAttend);
        Assert.NotNull(row.DateArrived);
        // The whole point: null means they said it themselves.
        Assert.Null(row.AttendanceRecordedByAppUserId);
    }

    [Fact]
    public async Task An_override_names_who_recorded_it()
    {
        var w = await SeedAsync();

        var result = await Build(w.Factory, CreatorId).OverrideAttendance(
            OrgId, w.InvestigationId, w.AttendeeRowId,
            new OverrideAttendanceRequest(DidAttend: true, DateTime.UtcNow.AddHours(-3)), default);

        Assert.IsType<OkObjectResult>(result.Result);

        var row = await RowAsync(w);
        Assert.True(row.DidAttend);
        // Stamped, so the two cases are afterwards distinguishable — which a shared bool could not do.
        Assert.Equal(CreatorId, row.AttendanceRecordedByAppUserId);
    }

    [Fact]
    public async Task Marking_someone_absent_is_also_attributed()
    {
        var w = await SeedAsync();

        await Build(w.Factory, CreatorId).OverrideAttendance(
            OrgId, w.InvestigationId, w.AttendeeRowId,
            new OverrideAttendanceRequest(DidAttend: false), default);

        var row = await RowAsync(w);
        Assert.False(row.DidAttend);
        Assert.Null(row.DateArrived);
        // "Who says they weren't there" matters as much as who says they were.
        Assert.Equal(CreatorId, row.AttendanceRecordedByAppUserId);
    }

    [Fact]
    public async Task Checking_in_after_being_marked_absent_restores_it_to_your_own_word()
    {
        var w = await SeedAsync();
        await Build(w.Factory, CreatorId).OverrideAttendance(
            OrgId, w.InvestigationId, w.AttendeeRowId,
            new OverrideAttendanceRequest(DidAttend: false), default);

        await Build(w.Factory, AttendeeId).CheckIn(
            OrgId, w.InvestigationId, new CheckInRequest(), default);

        var row = await RowAsync(w);
        Assert.True(row.DidAttend);
        // Cleared rather than left pointing at the manager: it is the attendee's statement now.
        Assert.Null(row.AttendanceRecordedByAppUserId);
    }

    // ── Who may do what ───────────────────────────────────────────────────────

    [Fact]
    public async Task Someone_not_on_the_team_cannot_check_in()
    {
        var w = await SeedAsync();

        // A member of the group, but not on this investigation.
        var result = await Build(w.Factory, OtherMemberId).CheckIn(
            OrgId, w.InvestigationId, new CheckInRequest(), default);

        // Otherwise anyone could add themselves to the record of any visit they never went on.
        Assert.IsType<ForbidResult>(result.Result);
        Assert.Null((await RowAsync(w)).DidAttend);
    }

    [Fact]
    public async Task An_ordinary_attendee_cannot_record_somebody_elses_attendance()
    {
        var w = await SeedAsync();

        var result = await Build(w.Factory, AttendeeId).OverrideAttendance(
            OrgId, w.InvestigationId, w.AttendeeRowId,
            new OverrideAttendanceRequest(DidAttend: true), default);

        // They can check themselves in; recording attendance is a different act.
        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task The_lead_of_the_visit_can_record_attendance()
    {
        var w = await SeedAsync();
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            db.InvestigationAttendees.Add(new InvestigationAttendee
            {
                Id = Guid.NewGuid(), InvestigationId = w.InvestigationId, AppUserId = OtherMemberId,
                IsLead = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = CreatorId,
            });
            await db.SaveChangesAsync();
        }

        var result = await Build(w.Factory, OtherMemberId).OverrideAttendance(
            OrgId, w.InvestigationId, w.AttendeeRowId,
            new OverrideAttendanceRequest(DidAttend: true), default);

        // Same person, refused in the test above, allowed here purely because they lead this visit.
        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(OtherMemberId, (await RowAsync(w)).AttendanceRecordedByAppUserId);
    }

    // ── Stated arrival time ───────────────────────────────────────────────────

    [Fact]
    public async Task A_late_check_in_can_state_when_they_actually_arrived()
    {
        var w = await SeedAsync();
        var lastNight = DateTime.UtcNow.AddHours(-14);

        await Build(w.Factory, AttendeeId).CheckIn(
            OrgId, w.InvestigationId, new CheckInRequest(lastNight), default);

        var row = await RowAsync(w);
        // The ordinary path, not an exception: cellars and woodland have no signal, so people
        // check in the next morning and say when they got there.
        Assert.Equal(lastNight, row.DateArrived!.Value, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task An_arrival_time_in_the_future_is_refused()
    {
        var w = await SeedAsync();

        var result = await Build(w.Factory, AttendeeId).CheckIn(
            OrgId, w.InvestigationId, new CheckInRequest(DateTime.UtcNow.AddDays(2)), default);

        // A typo rather than a memory.
        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Null((await RowAsync(w)).DidAttend);
    }

    [Fact]
    public async Task Checking_in_with_no_time_uses_now()
    {
        var w = await SeedAsync();

        await Build(w.Factory, AttendeeId).CheckIn(
            OrgId, w.InvestigationId, new CheckInRequest(null), default);

        var row = await RowAsync(w);
        Assert.NotNull(row.DateArrived);
        Assert.True(DateTime.UtcNow - row.DateArrived!.Value < TimeSpan.FromMinutes(1));
    }

    // ── Roster ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_roster_reports_whether_arrival_was_self_reported()
    {
        var w = await SeedAsync();
        await Build(w.Factory, AttendeeId).CheckIn(OrgId, w.InvestigationId, new CheckInRequest(), default);

        var roster = Assert.IsAssignableFrom<IEnumerable<InvestigationRosterEntry>>(
            Assert.IsType<OkObjectResult>(
                (await Build(w.Factory, CreatorId).GetRoster(OrgId, w.InvestigationId, default)).Result).Value)
            .ToList();

        var entry = Assert.Single(roster);
        Assert.True(entry.SelfReported);
        Assert.Equal("An Attendee", entry.DisplayName);
    }

    [Fact]
    public async Task The_roster_marks_an_override_as_not_self_reported()
    {
        var w = await SeedAsync();
        await Build(w.Factory, CreatorId).OverrideAttendance(
            OrgId, w.InvestigationId, w.AttendeeRowId,
            new OverrideAttendanceRequest(DidAttend: true), default);

        var roster = Assert.IsAssignableFrom<IEnumerable<InvestigationRosterEntry>>(
            Assert.IsType<OkObjectResult>(
                (await Build(w.Factory, CreatorId).GetRoster(OrgId, w.InvestigationId, default)).Result).Value)
            .ToList();

        Assert.False(Assert.Single(roster).SelfReported);
    }

    // ── The point of it all ───────────────────────────────────────────────────

    [Fact]
    public async Task Checking_in_puts_the_visit_on_your_own_map()
    {
        var w = await SeedAsync();

        await Build(w.Factory, AttendeeId).CheckIn(OrgId, w.InvestigationId, new CheckInRequest(), default);

        var mine = new MyInvestigationsController(w.Factory)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, AttendeeId.ToString())], "Bearer"))
                }
            }
        };

        var attended = Assert.IsAssignableFrom<IEnumerable<AttendedInvestigationItem>>(
            Assert.IsType<OkObjectResult>((await mine.GetAttended(default)).Result).Value).ToList();

        // The end-to-end reason check-in exists: P5's map was correct but empty, because nothing
        // could mark attendance without a manager doing it.
        Assert.Equal("Night visit", Assert.Single(attended).Title);
    }
}
