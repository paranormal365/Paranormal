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
/// The case page's door onto a visit's team, held to the same rules as the group's (10/16/2026).
/// </summary>
/// <remarks>
/// <para>Found while adding the door for visits with no case: this one checked that the caller
/// manages the visit and nothing about the person being added. A stranger or a Viewer could be put
/// on a case's team, a second add of the same person was a 500 from the unique index, nothing was
/// audited, and removing somebody who held a duty failed in SQL Server on the NoAction key.</para>
///
/// <para>The same refusal set as <see cref="InvestigationTeamTests"/>, through the case-nested
/// route. Both doors now share <c>InvestigationTeam</c>, so these are what stop them drifting.</para>
/// </remarks>
public class CaseInvestigationTeamTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid OtherOrgId = Guid.NewGuid();
    private static readonly Guid CaseId = Guid.NewGuid();
    private static readonly Guid CreatorId = Guid.NewGuid();
    private static readonly Guid PlainMemberId = Guid.NewGuid();
    private static readonly Guid NewcomerId = Guid.NewGuid();
    private static readonly Guid LapsedMemberId = Guid.NewGuid();
    private static readonly Guid ClosedAccountId = Guid.NewGuid();
    private static readonly Guid ViewerId = Guid.NewGuid();
    private static readonly Guid OutsiderId = Guid.NewGuid();

    private sealed record World(
        IDbContextFactory<BenDataContext> Factory,
        Mock<IAuditLogService> Audit,
        Guid InvestigationId,
        Guid PlainMemberRow);

    private static InvestigationController Build(World w, Guid userId)
        => new(w.Factory, new Mock<IMapper>().Object,
               new Ben.Data.WebApi.Services.Billing.SubscriptionLimitGuard(w.Factory),
               new Ben.Service.RepositoryService.Services.OrganizationSecurityService(w.Factory),
               TestMailer.Quiet(), new Ben.Data.WebApi.Services.CmsMarkupSanitizer(), w.Audit.Object)
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

    /// <summary>One case, one visit on it scheduled by a plain member, one other plain member on the team.</summary>
    private static async Task<World> SeedAsync()
    {
        var factory = TestDbFactory.Create();
        var invId = Guid.NewGuid();
        var plainRow = Guid.NewGuid();

        await using var db = await factory.CreateDbContextAsync();

        db.Organizations.Add(new Organization
        { Id = OrgId, Name = "BenCo", UrlName = "benco", DateCreated = DateTime.UtcNow });
        db.Organizations.Add(new Organization
        { Id = OtherOrgId, Name = "Elsewhere", UrlName = "elsewhere", DateCreated = DateTime.UtcNow });

        void Person(Guid id, string name, bool closed = false)
            => db.Users.Add(new AppUser
               {
                   Id = id, UserName = $"{id:N}@t", Email = $"{id:N}@t", DisplayName = name,
                   DateClosed = closed ? DateTime.UtcNow.AddDays(-1) : null,
               });

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
        Person(ClosedAccountId, "A former member", closed: true);
        Person(ViewerId, "A Viewer");
        Person(OutsiderId, "An Outsider");

        // Plain members throughout, so the only manage right in the room is the creator's.
        Member(CreatorId, OrgId, OrganizationMemberRole.Member);
        Member(PlainMemberId, OrgId, OrganizationMemberRole.Member);
        Member(NewcomerId, OrgId, OrganizationMemberRole.Member);
        Member(LapsedMemberId, OrgId, OrganizationMemberRole.Member, active: false);
        // Closing an account leaves the membership row active so the person's work stays attributed.
        Member(ClosedAccountId, OrgId, OrganizationMemberRole.Member);
        Member(ViewerId, OrgId, OrganizationMemberRole.Viewer);
        Member(OutsiderId, OtherOrgId, OrganizationMemberRole.Member);

        db.Cases.Add(new Case
        {
            Id = CaseId, OrganizationId = OrgId, Title = "The house on the hill",
            CaseYear = 2026, OrgCaseNumber = 1,
            StreetAddress1 = "1 Hill Rd", City = "Nashville", State = "TN", ZipCode = "37201", Country = "US",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = CreatorId,
        });
        db.Investigations.Add(new Investigation
        {
            Id = invId, OrganizationId = OrgId, CaseId = CaseId, Title = "First night",
            ScheduledDateTime = DateTime.UtcNow.AddDays(1),
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = CreatorId,
        });
        db.InvestigationAttendees.Add(new InvestigationAttendee
        {
            Id = plainRow, InvestigationId = invId, AppUserId = PlainMemberId,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = CreatorId,
        });

        await db.SaveChangesAsync();
        await TestSeeds.BridgeAsync(factory, OrgId);
        return new World(factory, new Mock<IAuditLogService>(), invId, plainRow);
    }

    private static async Task<List<InvestigationAttendee>> TeamAsync(World w)
    {
        await using var db = await w.Factory.CreateDbContextAsync();
        return await db.InvestigationAttendees.Where(a => a.InvestigationId == w.InvestigationId).ToListAsync();
    }

    private static Task<ActionResult<Ben.Service.Models.Entities.InvestigationAttendeeRecord>> AddAsync(
        World w, Guid asUser, Guid who)
        => Build(w, asUser).AddAttendee(
            OrgId, CaseId, w.InvestigationId, new AddInvestigationAttendeeRequest(who, null), default);

    private static Task<IActionResult> RemoveAsync(World w, Guid asUser, Guid row)
        => Build(w, asUser).RemoveAttendee(OrgId, CaseId, w.InvestigationId, row, default);

    // ── Adding ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Whoever_manages_the_visit_can_put_a_member_on_its_team_and_it_is_audited()
    {
        var w = await SeedAsync();

        var result = await AddAsync(w, CreatorId, NewcomerId);

        Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Contains(await TeamAsync(w), a => a.AppUserId == NewcomerId);
        w.Audit.Verify(a => a.LogCreateAsync(
            nameof(InvestigationAttendee), It.IsAny<Guid>(), It.IsAny<InvestigationAttendee>(),
            CreatorId, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task A_member_who_does_not_manage_the_visit_cannot_add_anybody()
    {
        var w = await SeedAsync();

        var result = await AddAsync(w, PlainMemberId, NewcomerId);

        Assert.IsType<ForbidResult>(result.Result);
        Assert.DoesNotContain(await TeamAsync(w), a => a.AppUserId == NewcomerId);
    }

    [Theory]
    [InlineData("outsider")]
    [InlineData("lapsed")]
    [InlineData("closed")]
    [InlineData("nobody")]
    public async Task Somebody_who_is_not_an_active_member_cannot_be_put_on_the_team(string who)
    {
        var w = await SeedAsync();
        var target = who switch
        {
            "outsider" => OutsiderId,
            "lapsed"   => LapsedMemberId,
            "closed"   => ClosedAccountId,
            _          => Guid.NewGuid(),
        };

        var result = await AddAsync(w, CreatorId, target);

        var refusal = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("isn't an active member", (string)refusal.Value!);
        Assert.DoesNotContain(await TeamAsync(w), a => a.AppUserId == target);
    }

    [Fact]
    public async Task A_viewer_cannot_be_put_on_the_team()
    {
        var w = await SeedAsync();

        var result = await AddAsync(w, CreatorId, ViewerId);

        var refusal = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("viewer", (string)refusal.Value!);
        Assert.DoesNotContain(await TeamAsync(w), a => a.AppUserId == ViewerId);
    }

    [Fact]
    public async Task Nobody_is_on_a_team_twice()
    {
        var w = await SeedAsync();

        var result = await AddAsync(w, CreatorId, PlainMemberId);

        // In SQL Server this was a 500 from the unique index; in the InMemory provider, a second row.
        var refusal = Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Equal("Plain Member is already on this team.", refusal.Value);
        Assert.Single(await TeamAsync(w), a => a.AppUserId == PlainMemberId);
    }

    // ── Taking off ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Taking_somebody_off_takes_their_duties_too_and_is_audited()
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

        var result = await RemoveAsync(w, CreatorId, w.PlainMemberRow);

        Assert.IsType<NoContentResult>(result);
        Assert.Empty(await TeamAsync(w));
        await using var after = await w.Factory.CreateDbContextAsync();
        // The NoAction key: left behind, this assignment made SQL Server refuse the delete.
        Assert.Empty(await after.InvestigationDutyAssignments.ToListAsync());
        w.Audit.Verify(a => a.LogDeleteAsync(
            nameof(InvestigationAttendee), w.PlainMemberRow, It.IsAny<InvestigationAttendee>(),
            CreatorId, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task Somebody_recorded_as_there_stays_on_the_team()
    {
        var w = await SeedAsync();
        await using (var db = await w.Factory.CreateDbContextAsync())
        {
            (await db.InvestigationAttendees.SingleAsync(a => a.Id == w.PlainMemberRow)).DidAttend = true;
            await db.SaveChangesAsync();
        }

        var result = await RemoveAsync(w, CreatorId, w.PlainMemberRow);

        var refusal = Assert.IsType<ConflictObjectResult>(result);
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
                Narrative = "A door closed on its own.",
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = PlainMemberId,
            });
            await db.SaveChangesAsync();
        }

        var result = await RemoveAsync(w, CreatorId, w.PlainMemberRow);

        var refusal = Assert.IsType<ConflictObjectResult>(result);
        Assert.Contains("written up what they saw", (string)refusal.Value!);
        Assert.Single(await TeamAsync(w));
    }

    [Fact]
    public async Task A_member_who_does_not_manage_the_visit_cannot_take_anybody_off()
    {
        var w = await SeedAsync();

        var result = await RemoveAsync(w, NewcomerId, w.PlainMemberRow);

        Assert.IsType<ForbidResult>(result);
        Assert.Single(await TeamAsync(w));
    }
}
