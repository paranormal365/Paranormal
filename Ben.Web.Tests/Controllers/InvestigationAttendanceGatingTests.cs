using AutoMapper;
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
/// The attendance endpoint carries two different rights, and they are gated separately.
/// </summary>
/// <remarks>
/// Answering your own invitation is yours by definition. Saying who actually turned up, what job
/// they did, or who is leading the visit is managing it. Both arrive through the same PUT, so the
/// split has to happen inside the action — and getting it wrong in the permissive direction would
/// let anybody mark themselves the lead and thereby grant themselves everything else.
/// </remarks>
public class InvestigationAttendanceGatingTests
{
    private static readonly Guid OrgId = Guid.NewGuid();

    private static IMapper Mapper()
    {
        var m = new Mock<IMapper>();
        m.Setup(x => x.Map<InvestigationAttendeeRecord>(It.IsAny<object>()))
         .Returns<object>(o => o is InvestigationAttendee a
            ? new InvestigationAttendeeRecord
            {
                Id = a.Id, InvestigationId = a.InvestigationId, AppUserId = a.AppUserId,
                AssignedRole = a.AssignedRole, IsLead = a.IsLead, Rsvp = a.Rsvp,
                DidAttend = a.DidAttend, DateCreated = a.DateCreated,
                CreatedByAppUserId = a.CreatedByAppUserId,
            }
            : new InvestigationAttendeeRecord { DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.Empty });
        return m.Object;
    }

    private static InvestigationController Build(IDbContextFactory<BenDataContext> f, Guid userId)
        => new(f, Mapper(), new Ben.Data.WebApi.Services.Billing.SubscriptionLimitGuard(f), new Ben.Service.RepositoryService.Services.OrganizationSecurityService(f))
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

    private sealed record World(
        IDbContextFactory<BenDataContext> Factory, Guid CaseId, Guid InvestigationId,
        Guid CreatorId, Guid PlainMemberId, Guid PlainAttendeeRowId, Guid CreatorAttendeeRowId);

    private static async Task<World> SeedAsync()
    {
        var factory = TestDbFactory.Create();
        var creator = Guid.NewGuid();
        var plain = Guid.NewGuid();
        var caseId = Guid.NewGuid();
        var invId = Guid.NewGuid();
        var plainRow = Guid.NewGuid();
        var creatorRow = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();

        db.Organizations.Add(new Organization
        { Id = OrgId, Name = "BenCo", UrlName = "benco", DateCreated = DateTime.UtcNow });
        foreach (var userId in new[] { creator, plain })
        {
            // The accounts have to exist: the endpoint reloads the attendee through its AppUser
            // navigation, and a required include with no principal row yields nothing at all.
            db.Users.Add(new AppUser
            {
                Id = userId, UserName = $"{userId:N}@test", Email = $"{userId:N}@test",
                DisplayName = userId == creator ? "The Creator" : "A Member",
            });
            db.OrganizationUserMemberships.Add(new OrganizationUserMembership
            {
                Id = Guid.NewGuid(), OrganizationId = OrgId, AppUserId = userId,
                Role = OrganizationMemberRole.Member, IsActive = true, DateCreated = DateTime.UtcNow,
            });
        }
        db.Cases.Add(new Case
        {
            Id = caseId, OrganizationId = OrgId, Title = "A case", CaseYear = 2026, OrgCaseNumber = 1,
            StreetAddress1 = "1 Somewhere Rd", City = "Nashville", State = "TN", ZipCode = "37201",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = creator,
        });
        db.Investigations.Add(new Investigation
        {
            Id = invId, OrganizationId = OrgId, CaseId = caseId, Title = "Night visit",
            ScheduledDateTime = DateTime.UtcNow.AddDays(3),
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = creator,
        });
        db.InvestigationAttendees.AddRange(
            new InvestigationAttendee
            {
                Id = plainRow, InvestigationId = invId, AppUserId = plain,
                Rsvp = RsvpStatus.Invited, DateCreated = DateTime.UtcNow, CreatedByAppUserId = creator,
            },
            new InvestigationAttendee
            {
                Id = creatorRow, InvestigationId = invId, AppUserId = creator,
                Rsvp = RsvpStatus.Invited, DateCreated = DateTime.UtcNow, CreatedByAppUserId = creator,
            });

        await db.SaveChangesAsync();
        await TestSeeds.BridgeAsync(factory, OrgId);
        return new World(factory, caseId, invId, creator, plain, plainRow, creatorRow);
    }

    // ── RSVP is yours ─────────────────────────────────────────────────────────

    [Fact]
    public async Task A_member_can_answer_their_own_invitation()
    {
        var w = await SeedAsync();

        var result = await Build(w.Factory, w.PlainMemberId).UpdateAttendance(
            OrgId, w.CaseId, w.InvestigationId, w.PlainAttendeeRowId,
            new UpdateAttendanceRequest(null, null, RsvpStatus.Accepted), default);

        Assert.IsType<OkObjectResult>(result.Result);

        await using var db = await w.Factory.CreateDbContextAsync();
        Assert.Equal(RsvpStatus.Accepted,
            (await db.InvestigationAttendees.FirstAsync(a => a.Id == w.PlainAttendeeRowId)).Rsvp);
    }

    [Fact]
    public async Task A_member_cannot_answer_somebody_elses_invitation()
    {
        var w = await SeedAsync();

        var result = await Build(w.Factory, w.PlainMemberId).UpdateAttendance(
            OrgId, w.CaseId, w.InvestigationId, w.CreatorAttendeeRowId,
            new UpdateAttendanceRequest(null, null, RsvpStatus.Declined), default);

        Assert.IsType<ForbidResult>(result.Result);
    }

    // ── The rest is management ────────────────────────────────────────────────

    [Fact]
    public async Task A_member_cannot_mark_themselves_as_having_attended()
    {
        var w = await SeedAsync();

        var result = await Build(w.Factory, w.PlainMemberId).UpdateAttendance(
            OrgId, w.CaseId, w.InvestigationId, w.PlainAttendeeRowId,
            new UpdateAttendanceRequest(DidAttend: true, null), default);

        // Their own row, but not their call — attendance is the record of what happened, and
        // P5b's check-in is the deliberate, provenance-carrying way to claim it.
        Assert.IsType<ForbidResult>(result.Result);

        await using var db = await w.Factory.CreateDbContextAsync();
        Assert.Null((await db.InvestigationAttendees.FirstAsync(a => a.Id == w.PlainAttendeeRowId)).DidAttend);
    }

    [Fact]
    public async Task A_member_cannot_make_themselves_the_lead()
    {
        var w = await SeedAsync();

        var result = await Build(w.Factory, w.PlainMemberId).UpdateAttendance(
            OrgId, w.CaseId, w.InvestigationId, w.PlainAttendeeRowId,
            new UpdateAttendanceRequest(null, null, IsLead: true), default);

        // The one that would unravel everything else: lead is a way to earn manage, so a member
        // who could set it on themselves would have granted themselves the whole gate.
        Assert.IsType<ForbidResult>(result.Result);

        await using var db = await w.Factory.CreateDbContextAsync();
        Assert.False((await db.InvestigationAttendees.FirstAsync(a => a.Id == w.PlainAttendeeRowId)).IsLead);
    }

    [Fact]
    public async Task Whoever_manages_the_visit_can_appoint_a_lead()
    {
        var w = await SeedAsync();

        var result = await Build(w.Factory, w.CreatorId).UpdateAttendance(
            OrgId, w.CaseId, w.InvestigationId, w.PlainAttendeeRowId,
            new UpdateAttendanceRequest(null, null, IsLead: true), default);

        Assert.IsType<OkObjectResult>(result.Result);

        await using var db = await w.Factory.CreateDbContextAsync();
        Assert.True((await db.InvestigationAttendees.FirstAsync(a => a.Id == w.PlainAttendeeRowId)).IsLead);
    }

    [Fact]
    public async Task Appointing_a_lead_hands_over_the_ability_to_manage()
    {
        var w = await SeedAsync();

        await Build(w.Factory, w.CreatorId).UpdateAttendance(
            OrgId, w.CaseId, w.InvestigationId, w.PlainAttendeeRowId,
            new UpdateAttendanceRequest(null, null, IsLead: true), default);

        // Now the same member who was refused above can do the manage-gated thing. This is the
        // delegation actually working end to end, rather than a flag that is merely stored.
        var result = await Build(w.Factory, w.PlainMemberId).UpdateAttendance(
            OrgId, w.CaseId, w.InvestigationId, w.CreatorAttendeeRowId,
            new UpdateAttendanceRequest(DidAttend: true, null), default);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task Setting_only_an_rsvp_no_longer_wipes_whether_you_attended()
    {
        var w = await SeedAsync();
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            var row = await db.InvestigationAttendees.FirstAsync(a => a.Id == w.PlainAttendeeRowId);
            row.DidAttend = true;
            await db.SaveChangesAsync();
        }

        await Build(w.Factory, w.PlainMemberId).UpdateAttendance(
            OrgId, w.CaseId, w.InvestigationId, w.PlainAttendeeRowId,
            new UpdateAttendanceRequest(null, null, RsvpStatus.Accepted), default);

        // DidAttend used to be assigned unconditionally, so an RSVP-only request silently erased
        // the attendance record. Found while splitting the gates rather than by anyone reporting it.
        await using var check = await w.Factory.CreateDbContextAsync();
        var after = await check.InvestigationAttendees.FirstAsync(a => a.Id == w.PlainAttendeeRowId);
        Assert.True(after.DidAttend);
        Assert.Equal(RsvpStatus.Accepted, after.Rsvp);
    }

    // ── The tightened record endpoints ────────────────────────────────────────

    [Fact]
    public async Task An_ordinary_member_can_no_longer_edit_or_cancel_the_investigation()
    {
        var w = await SeedAsync();
        var asPlain = Build(w.Factory, w.PlainMemberId);

        var update = await asPlain.Update(
            OrgId, w.CaseId, w.InvestigationId,
            new UpsertInvestigationRequest("Moved", null, null, DateTime.UtcNow.AddDays(9), null,
                InvestigationStatus.Scheduled, null, null), default);
        var cancel = await asPlain.Cancel(OrgId, w.CaseId, w.InvestigationId, default);
        var delete = await asPlain.Delete(OrgId, w.CaseId, w.InvestigationId, default);
        var addAttendee = await asPlain.AddAttendee(
            OrgId, w.CaseId, w.InvestigationId,
            new AddInvestigationAttendeeRequest(Guid.NewGuid(), null), default);
        var removeAttendee = await asPlain.RemoveAttendee(
            OrgId, w.CaseId, w.InvestigationId, w.CreatorAttendeeRowId, default);

        Assert.IsType<ForbidResult>(update.Result);
        Assert.IsType<ForbidResult>(cancel);
        Assert.IsType<ForbidResult>(delete);
        Assert.IsType<ForbidResult>(addAttendee.Result);
        Assert.IsType<ForbidResult>(removeAttendee);
    }

    [Fact]
    public async Task A_missing_investigation_is_still_not_found_rather_than_forbidden()
    {
        var w = await SeedAsync();

        // Ordering matters: gating before the lookup turned every "no such id" into a 403 and
        // broke three existing tests. Membership is already proved, so looking first leaks nothing.
        var result = await Build(w.Factory, w.CreatorId).Delete(
            OrgId, w.CaseId, Guid.NewGuid(), default);

        Assert.IsType<NotFoundResult>(result);
    }
}
