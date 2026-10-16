using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
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
/// Putting people on a visit's team, and taking them off, when the visit has no case.
/// </summary>
/// <remarks>
/// <para>Found on production 10/16/2026 while setting up App Review's demo data as the group's
/// Owner: a visit booked from the group's Investigations page has no case, the only add-attendee
/// endpoint was nested under a case, and so the team could never be filled. Check-in, the lead
/// and duties all need somebody on the team, so all three were unreachable too.</para>
///
/// <para>Every visit here is case-less on purpose — that is the door that was missing.</para>
/// </remarks>
public class InvestigationTeamTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid OtherOrgId = Guid.NewGuid();
    private static readonly Guid CreatorId = Guid.NewGuid();
    private static readonly Guid PlainMemberId = Guid.NewGuid();
    private static readonly Guid NewcomerId = Guid.NewGuid();
    private static readonly Guid LapsedMemberId = Guid.NewGuid();
    private static readonly Guid ViewerId = Guid.NewGuid();
    private static readonly Guid OutsiderId = Guid.NewGuid();

    private sealed record World(
        IDbContextFactory<BenDataContext> Factory,
        Mock<IAuditLogService> Audit,
        Guid InvestigationId,
        Guid OtherOrgInvestigationId,
        Guid PlainMemberRow);

    private static OrgInvestigationsController Build(World w, Guid userId)
        => new(w.Factory, new Mock<IMapper>().Object, w.Audit.Object,
               new Ben.Service.RepositoryService.Services.OrganizationSecurityService(w.Factory))
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

    /// <summary>
    /// One case-less visit scheduled by a plain member, with one other plain member already on
    /// it, and a cast of people who may or may not be added.
    /// </summary>
    private static async Task<World> SeedAsync()
    {
        var factory = TestDbFactory.Create();
        var invId = Guid.NewGuid();
        var otherInvId = Guid.NewGuid();
        var plainRow = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();

        db.Organizations.Add(new Organization
        { Id = OrgId, Name = "BenCo", UrlName = "benco", DateCreated = DateTime.UtcNow });
        db.Organizations.Add(new Organization
        { Id = OtherOrgId, Name = "Elsewhere", UrlName = "elsewhere", DateCreated = DateTime.UtcNow });

        void Person(Guid id, string name)
            => db.Users.Add(new AppUser
               { Id = id, UserName = $"{id:N}@t", Email = $"{id:N}@t", DisplayName = name });

        void Member(Guid id, Guid orgId, OrganizationMemberRole role, bool active = true)
            => db.OrganizationUserMemberships.Add(new OrganizationUserMembership
               {
                   Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = id,
                   Role = role, IsActive = active, DateCreated = DateTime.UtcNow,
               });

        Person(CreatorId, "The Creator");
        Person(PlainMemberId, "Plain Member");
        Person(NewcomerId, "New Comer");
        Person(LapsedMemberId, "Lapsed Member");
        Person(ViewerId, "A Viewer");
        Person(OutsiderId, "An Outsider");

        // Plain members throughout, so the only manage right in the room is the creator's.
        Member(CreatorId, OrgId, OrganizationMemberRole.Member);
        Member(PlainMemberId, OrgId, OrganizationMemberRole.Member);
        Member(NewcomerId, OrgId, OrganizationMemberRole.Member);
        Member(LapsedMemberId, OrgId, OrganizationMemberRole.Member, active: false);
        Member(ViewerId, OrgId, OrganizationMemberRole.Viewer);
        // The outsider belongs to a different group entirely.
        Member(OutsiderId, OtherOrgId, OrganizationMemberRole.Member);

        db.Investigations.Add(new Investigation
        {
            Id = invId, OrganizationId = OrgId, Title = "Landmark night",
            ScheduledDateTime = DateTime.UtcNow.AddDays(1),
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = CreatorId,
        });
        db.Investigations.Add(new Investigation
        {
            Id = otherInvId, OrganizationId = OtherOrgId, Title = "Their night",
            ScheduledDateTime = DateTime.UtcNow.AddDays(1),
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = OutsiderId,
        });

        db.InvestigationAttendees.Add(new InvestigationAttendee
        {
            Id = plainRow, InvestigationId = invId, AppUserId = PlainMemberId,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = CreatorId,
        });

        await db.SaveChangesAsync();
        await TestSeeds.BridgeAsync(factory, OrgId);
        return new World(factory, new Mock<IAuditLogService>(), invId, otherInvId, plainRow);
    }

    private static async Task<List<InvestigationAttendee>> TeamAsync(World w, Guid? investigationId = null)
    {
        await using var db = await w.Factory.CreateDbContextAsync();
        var id = investigationId ?? w.InvestigationId;
        return await db.InvestigationAttendees.Where(a => a.InvestigationId == id).ToListAsync();
    }

    private static Task<ActionResult<IEnumerable<InvestigationRosterEntry>>> AddAsync(
        World w, Guid asUser, Guid who, Guid? investigationId = null)
        => Build(w, asUser).AddTeamMember(
            OrgId, investigationId ?? w.InvestigationId, new AddTeamMemberRequest(who), default);

    // ── Adding ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Whoever_manages_the_visit_can_put_a_member_on_its_team()
    {
        var w = await SeedAsync();

        var result = await AddAsync(w, CreatorId, NewcomerId);

        var roster = Assert.IsAssignableFrom<IEnumerable<InvestigationRosterEntry>>(
            Assert.IsType<OkObjectResult>(result.Result).Value).ToList();
        // The whole roster comes back, so the panel can redraw from one answer.
        Assert.Equal(2, roster.Count);
        Assert.Contains(roster, r => r.AppUserId == NewcomerId);
        Assert.Contains(await TeamAsync(w), a => a.AppUserId == NewcomerId && a.CreatedByAppUserId == CreatorId);
    }

    [Fact]
    public async Task Adding_somebody_is_audited()
    {
        var w = await SeedAsync();

        await AddAsync(w, CreatorId, NewcomerId);

        w.Audit.Verify(a => a.LogCreateAsync(
            nameof(InvestigationAttendee), It.IsAny<Guid>(), It.IsAny<InvestigationAttendee>(),
            CreatorId, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task Once_on_the_team_they_can_check_in()
    {
        var w = await SeedAsync();
        await AddAsync(w, CreatorId, NewcomerId);

        // The point of the whole change: check-in refuses anybody without a team row, so before
        // this door existed nobody could ever arrive at a case-less visit.
        var result = await Build(w, NewcomerId).CheckIn(OrgId, w.InvestigationId, new CheckInRequest(), default);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task A_member_who_does_not_manage_the_visit_cannot_add_anybody()
    {
        var w = await SeedAsync();

        var result = await AddAsync(w, PlainMemberId, NewcomerId);

        Assert.IsType<ForbidResult>(result.Result);
        Assert.DoesNotContain(await TeamAsync(w), a => a.AppUserId == NewcomerId);
    }

    [Fact]
    public async Task A_member_cannot_add_themselves()
    {
        var w = await SeedAsync();

        var result = await AddAsync(w, NewcomerId, NewcomerId);

        // Being on the team is what lets somebody file an account of the night, so joining one is
        // the manager's call, not the member's.
        Assert.IsType<ForbidResult>(result.Result);
        Assert.DoesNotContain(await TeamAsync(w), a => a.AppUserId == NewcomerId);
    }

    [Fact]
    public async Task Somebody_outside_the_group_gets_nothing()
    {
        var w = await SeedAsync();

        var result = await AddAsync(w, OutsiderId, OutsiderId);

        Assert.IsType<ForbidResult>(result.Result);
        Assert.Single(await TeamAsync(w));
    }

    [Fact]
    public async Task Somebody_who_is_not_in_the_group_cannot_be_put_on_its_team()
    {
        var w = await SeedAsync();

        var result = await AddAsync(w, CreatorId, OutsiderId);

        var refusal = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("isn't an active member", (string)refusal.Value!);
        Assert.DoesNotContain(await TeamAsync(w), a => a.AppUserId == OutsiderId);
    }

    [Fact]
    public async Task A_former_member_cannot_be_put_on_the_team()
    {
        var w = await SeedAsync();

        var result = await AddAsync(w, CreatorId, LapsedMemberId);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.DoesNotContain(await TeamAsync(w), a => a.AppUserId == LapsedMemberId);
    }

    [Fact]
    public async Task A_viewer_cannot_be_put_on_the_team()
    {
        var w = await SeedAsync();

        var result = await AddAsync(w, CreatorId, ViewerId);

        // A Viewer cannot check in or file an account, so a team row for one does nothing.
        var refusal = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("viewer", (string)refusal.Value!);
        Assert.DoesNotContain(await TeamAsync(w), a => a.AppUserId == ViewerId);
    }

    [Fact]
    public async Task Nobody_is_on_a_team_twice()
    {
        var w = await SeedAsync();

        var result = await AddAsync(w, CreatorId, PlainMemberId);

        // SQL Server's unique index would answer this with a 500; the InMemory provider would
        // quietly make a second row. Both are wrong, so the endpoint says it in words.
        var refusal = Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Contains("already on this team", (string)refusal.Value!);
        Assert.Single(await TeamAsync(w), a => a.AppUserId == PlainMemberId);
    }

    [Fact]
    public async Task Another_groups_visit_is_not_found()
    {
        var w = await SeedAsync();

        var result = await AddAsync(w, CreatorId, NewcomerId, w.OtherOrgInvestigationId);

        // Matched on the group in the address, so this group's manager reaches nothing of theirs.
        Assert.IsType<NotFoundResult>(result.Result);
        Assert.Empty(await TeamAsync(w, w.OtherOrgInvestigationId));
    }

    // ── Taking off ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Whoever_manages_the_visit_can_take_somebody_off_with_their_duties()
    {
        var w = await SeedAsync();
        var dutyId = Guid.NewGuid();
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            db.InvestigationDuties.Add(new InvestigationDuty
            {
                Id = dutyId, OrganizationId = OrgId, Name = "Audio", IsActive = true,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = CreatorId,
            });
            db.InvestigationDutyAssignments.Add(new InvestigationDutyAssignment
            {
                Id = Guid.NewGuid(), InvestigationAttendeeId = w.PlainMemberRow, InvestigationDutyId = dutyId,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = CreatorId,
            });
            await db.SaveChangesAsync();
        }

        var result = await Build(w, CreatorId).RemoveTeamMember(OrgId, w.InvestigationId, w.PlainMemberRow, default);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Empty(await TeamAsync(w));
        await using var after = await w.Factory.CreateDbContextAsync();
        // The assignment's key to the team row is NoAction, so leaving it would make SQL Server
        // refuse the delete outright.
        Assert.Empty(await after.InvestigationDutyAssignments.ToListAsync());
        w.Audit.Verify(a => a.LogDeleteAsync(
            nameof(InvestigationAttendee), w.PlainMemberRow, It.IsAny<InvestigationAttendee>(),
            CreatorId, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task A_member_who_does_not_manage_the_visit_cannot_take_anybody_off()
    {
        var w = await SeedAsync();

        var result = await Build(w, NewcomerId).RemoveTeamMember(OrgId, w.InvestigationId, w.PlainMemberRow, default);

        Assert.IsType<ForbidResult>(result.Result);
        Assert.Single(await TeamAsync(w));
    }

    [Fact]
    public async Task Somebody_recorded_as_there_stays_on_the_team()
    {
        var w = await SeedAsync();
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            var row = await db.InvestigationAttendees.SingleAsync(a => a.Id == w.PlainMemberRow);
            row.DidAttend = true;
            await db.SaveChangesAsync();
        }

        var result = await Build(w, CreatorId).RemoveTeamMember(OrgId, w.InvestigationId, w.PlainMemberRow, default);

        // Who attended is evidence. The refusal says how to correct a mistake instead.
        var refusal = Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Contains("Untick Attended", (string)refusal.Value!);
        Assert.Single(await TeamAsync(w));
    }

    [Fact]
    public async Task Somebody_who_wrote_it_up_stays_on_the_team()
    {
        var w = await SeedAsync();
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            db.InvestigationFindings.Add(new InvestigationFinding
            {
                Id = Guid.NewGuid(), InvestigationId = w.InvestigationId, AppUserId = PlainMemberId,
                Narrative = "Footsteps on the landing.",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = PlainMemberId,
            });
            await db.SaveChangesAsync();
        }

        var result = await Build(w, CreatorId).RemoveTeamMember(OrgId, w.InvestigationId, w.PlainMemberRow, default);

        Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Single(await TeamAsync(w));
    }

    [Fact]
    public async Task A_row_on_another_visit_is_not_found()
    {
        var w = await SeedAsync();

        var result = await Build(w, CreatorId).RemoveTeamMember(OrgId, w.InvestigationId, Guid.NewGuid(), default);

        Assert.IsType<NotFoundResult>(result.Result);
    }
}
