using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// The duty board (item 158): eligibility is soft, single-holder displaces, the Lead duty writes
/// through to IsLead, and nothing here crosses an organization boundary.
/// </summary>
public sealed class InvestigationDutyTests
{
    private static IDbContextFactory<BenDataContext> CreateFactory()
    {
        var options = new DbContextOptionsBuilder<BenDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PooledDbContextFactory<BenDataContext>(options);
    }

    private sealed record World(
        IDbContextFactory<BenDataContext> Factory, Guid OrgId, Guid AdminId,
        Guid InvestigationId, Guid AttendeeJuniorId, Guid AttendeeSeniorId,
        Guid LeadDutyId, Guid EvidenceDutyId, Guid JuniorLevelId, Guid SeniorLevelId);

    /// <summary>An org with a two-rung ladder, two attendees (one per rung), a single-holder
    /// Lead duty requiring the senior rung, and an unrestricted Evidence duty.</summary>
    private static async Task<World> SeedAsync()
    {
        var factory = CreateFactory();
        var orgId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var juniorUser = Guid.NewGuid();
        var seniorUser = Guid.NewGuid();
        var invId = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();
        db.Organizations.Add(new Organization { Id = orgId, Name = "G", UrlName = "g", DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId });

        var junior = new OrganizationMemberLevel { Id = Guid.NewGuid(), OrganizationId = orgId, Name = "Junior Investigator", SortOrder = 2, IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId };
        var senior = new OrganizationMemberLevel { Id = Guid.NewGuid(), OrganizationId = orgId, Name = "Senior Investigator", SortOrder = 4, IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId };
        db.OrganizationMemberLevels.AddRange(junior, senior);

        db.OrganizationUserMemberships.AddRange(
            new OrganizationUserMembership { Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = adminId, Role = OrganizationMemberRole.Administrator, IsActive = true, DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId },
            new OrganizationUserMembership { Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = juniorUser, Role = OrganizationMemberRole.Member, IsActive = true, MemberLevelId = junior.Id, DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId },
            new OrganizationUserMembership { Id = Guid.NewGuid(), OrganizationId = orgId, AppUserId = seniorUser, Role = OrganizationMemberRole.Member, IsActive = true, MemberLevelId = senior.Id, DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId });

        db.Investigations.Add(new Investigation
        {
            Id = invId, OrganizationId = orgId, Title = "Visit", Visibility = InvestigationVisibility.GroupOnly,
            ScheduledDateTime = DateTime.UtcNow.AddDays(3), Status = InvestigationStatus.Scheduled,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId,
        });

        var attJunior = new InvestigationAttendee { Id = Guid.NewGuid(), InvestigationId = invId, AppUserId = juniorUser, Rsvp = RsvpStatus.Accepted, DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId };
        var attSenior = new InvestigationAttendee { Id = Guid.NewGuid(), InvestigationId = invId, AppUserId = seniorUser, Rsvp = RsvpStatus.Accepted, DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId };
        db.InvestigationAttendees.AddRange(attJunior, attSenior);

        var lead = new InvestigationDuty { Id = Guid.NewGuid(), OrganizationId = orgId, Name = "Lead Investigator", SortOrder = 1, IsActive = true, IsSingleHolder = true, MinimumMemberLevelId = senior.Id, DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId };
        var evidence = new InvestigationDuty { Id = Guid.NewGuid(), OrganizationId = orgId, Name = "Evidence Collection", SortOrder = 2, IsActive = true, IsSingleHolder = false, DateCreated = DateTime.UtcNow, CreatedByAppUserId = adminId };
        db.InvestigationDuties.AddRange(lead, evidence);

        await db.SaveChangesAsync();
        await TestSeeds.BridgeAsync(factory, orgId);
        return new World(factory, orgId, adminId, invId, attJunior.Id, attSenior.Id, lead.Id, evidence.Id, junior.Id, senior.Id);
    }

    private static OrgInvestigationsController Build(IDbContextFactory<BenDataContext> factory, Guid userId)
    {
        var ctrl = new OrgInvestigationsController(
            factory, new Mock<IMapper>().Object,
            new Mock<Ben.Service.RepositoryService.GenericInterfaces.IAuditLogService>().Object, new Ben.Service.RepositoryService.Services.OrganizationSecurityService(factory));
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

    [Fact]
    public async Task Assigning_below_the_minimum_title_is_refused_with_a_sentence()
    {
        var w = await SeedAsync();
        var ctrl = Build(w.Factory, w.AdminId);

        var result = await ctrl.AssignDuty(w.OrgId, w.InvestigationId, w.AttendeeJuniorId, w.LeadDutyId,
            new AssignDutyRequest(Override: false), default);

        var refused = Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Contains("Senior Investigator", refused.Value!.ToString());
        Assert.Contains("Assign anyway", refused.Value!.ToString());
    }

    [Fact]
    public async Task The_override_assigns_and_records_that_it_was_deliberate()
    {
        var w = await SeedAsync();
        var ctrl = Build(w.Factory, w.AdminId);

        var result = await ctrl.AssignDuty(w.OrgId, w.InvestigationId, w.AttendeeJuniorId, w.LeadDutyId,
            new AssignDutyRequest(Override: true), default);

        Assert.IsType<OkObjectResult>(result.Result);
        await using var db = await w.Factory.CreateDbContextAsync();
        var assignment = await db.InvestigationDutyAssignments.SingleAsync();
        Assert.True(assignment.EligibilityOverridden);
    }

    [Fact]
    public async Task An_eligible_assignee_is_not_marked_overridden()
    {
        var w = await SeedAsync();
        var ctrl = Build(w.Factory, w.AdminId);

        var result = await ctrl.AssignDuty(w.OrgId, w.InvestigationId, w.AttendeeSeniorId, w.LeadDutyId,
            new AssignDutyRequest(Override: false), default);

        Assert.IsType<OkObjectResult>(result.Result);
        await using var db = await w.Factory.CreateDbContextAsync();
        Assert.False((await db.InvestigationDutyAssignments.SingleAsync()).EligibilityOverridden);
    }

    [Fact]
    public async Task A_single_holder_duty_displaces_and_the_lead_duty_writes_through_to_IsLead()
    {
        var w = await SeedAsync();
        var ctrl = Build(w.Factory, w.AdminId);

        Assert.IsType<OkObjectResult>((await ctrl.AssignDuty(w.OrgId, w.InvestigationId, w.AttendeeSeniorId, w.LeadDutyId,
            new AssignDutyRequest(), default)).Result);
        Assert.IsType<OkObjectResult>((await ctrl.AssignDuty(w.OrgId, w.InvestigationId, w.AttendeeJuniorId, w.LeadDutyId,
            new AssignDutyRequest(Override: true), default)).Result);

        await using var db = await w.Factory.CreateDbContextAsync();
        var holders = await db.InvestigationDutyAssignments.Where(x => x.InvestigationDutyId == w.LeadDutyId).ToListAsync();
        Assert.Single(holders);
        Assert.Equal(w.AttendeeJuniorId, holders[0].InvestigationAttendeeId);

        // The write-through: the lead flag follows the duty, so InvestigationAccess and every
        // lead badge keep one source of truth.
        Assert.True((await db.InvestigationAttendees.FindAsync(w.AttendeeJuniorId))!.IsLead);
        Assert.False((await db.InvestigationAttendees.FindAsync(w.AttendeeSeniorId))!.IsLead);
    }

    [Fact]
    public async Task Unassigning_the_lead_duty_clears_IsLead()
    {
        var w = await SeedAsync();
        var ctrl = Build(w.Factory, w.AdminId);
        Assert.IsType<OkObjectResult>((await ctrl.AssignDuty(w.OrgId, w.InvestigationId, w.AttendeeSeniorId, w.LeadDutyId,
            new AssignDutyRequest(), default)).Result);

        Assert.IsType<OkObjectResult>((await ctrl.UnassignDuty(w.OrgId, w.InvestigationId, w.AttendeeSeniorId, w.LeadDutyId, default)).Result);

        await using var db = await w.Factory.CreateDbContextAsync();
        Assert.False((await db.InvestigationAttendees.FindAsync(w.AttendeeSeniorId))!.IsLead);
        Assert.Empty(await db.InvestigationDutyAssignments.ToListAsync());
    }

    [Fact]
    public async Task A_multi_holder_duty_accumulates()
    {
        var w = await SeedAsync();
        var ctrl = Build(w.Factory, w.AdminId);

        Assert.IsType<OkObjectResult>((await ctrl.AssignDuty(w.OrgId, w.InvestigationId, w.AttendeeSeniorId, w.EvidenceDutyId,
            new AssignDutyRequest(), default)).Result);
        Assert.IsType<OkObjectResult>((await ctrl.AssignDuty(w.OrgId, w.InvestigationId, w.AttendeeJuniorId, w.EvidenceDutyId,
            new AssignDutyRequest(), default)).Result);

        await using var db = await w.Factory.CreateDbContextAsync();
        Assert.Equal(2, await db.InvestigationDutyAssignments.CountAsync(x => x.InvestigationDutyId == w.EvidenceDutyId));
    }

    [Fact]
    public async Task A_duty_from_another_group_is_not_assignable()
    {
        var w = await SeedAsync();
        var other = await SeedAsync();
        var ctrl = Build(w.Factory, w.AdminId);

        var result = await ctrl.AssignDuty(w.OrgId, w.InvestigationId, w.AttendeeSeniorId, other.EvidenceDutyId,
            new AssignDutyRequest(), default);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task A_plain_member_cannot_hand_out_duties()
    {
        var w = await SeedAsync();
        await using var db = await w.Factory.CreateDbContextAsync();
        var memberUser = (await db.InvestigationAttendees.FindAsync(w.AttendeeJuniorId))!.AppUserId;
        var ctrl = Build(w.Factory, memberUser);

        var result = await ctrl.AssignDuty(w.OrgId, w.InvestigationId, w.AttendeeJuniorId, w.EvidenceDutyId,
            new AssignDutyRequest(), default);

        Assert.IsType<ForbidResult>(result.Result);
    }
}
